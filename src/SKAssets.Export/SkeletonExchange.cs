using HKFBX.Fbx;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFSharp;
using FbxObject = NIFBX.Fbx.FbxObject;
using FbxScene = NIFBX.Fbx.FbxScene;
using SKAssets.Export.Fbx;

namespace SKAssets.Export
{
    /// <summary>
    /// A creature's skeleton, out to one FBX and back to both files.
    /// </summary>
    /// <remarks>
    /// An actor's skeleton is stored twice and the two halves do not overlap
    /// cleanly. <c>skeleton.nif</c> has the bone tree, the collision shapes, the
    /// rigid bodies and the constraints; <c>skeleton.hkx</c> has the animation rig,
    /// the ragdoll, and the mappers between them. Neither is complete and neither
    /// is redundant.
    ///
    /// What lets them become one file is that they agree. Composed to world space,
    /// the bones both files carry land in the same place to floating point — over
    /// the werewolf's 80 shared bones, a median difference of 0.0000 and a maximum
    /// of 0.28. So the rig is one skeleton stored twice and the FBX carries it once.
    ///
    /// Two things do not survive the crossing and are carried rather than derived,
    /// because measurement says they cannot be:
    ///
    /// <list type="bullet">
    /// <item><b>The ragdoll bone names.</b> Two naming conventions between them
    /// cover 69.8% of the vanilla bodies and the rest follow per-creature habits —
    /// the chaurus flyer drops its species prefix, so <c>ChaurusFlyerPelvis
    /// [Pelv]</c> becomes <c>Ragdoll_Pelvis [Pelv]01</c>. A wrong name is not
    /// cosmetic: the skeleton mappers and the behaviour graph both reference
    /// them.</item>
    /// <item><b>Which nodes are rig bones.</b> The mesh is always a superset,
    /// carrying between two and forty-six extra, and no flag or block type tells
    /// them apart — <c>0x8000E</c> sits on both sides of the line.</item>
    /// </list>
    ///
    /// Everything else — bodies, shapes, mass, damping, filters, the constraint
    /// graph, the ragdoll's own hierarchy and pose — comes out of the mesh, because
    /// a NIF rigid body <b>is</b> a ragdoll bone. That correspondence is exact:
    /// across all 45 vanilla creatures shipping both files, every ragdoll body
    /// rides a rig bone that owns a rigid body in the mesh.
    /// </remarks>
    public static class SkeletonExchange
    {
        /// <summary>The rig's bone list, carried on the scene root.</summary>
        /// <remarks>
        /// Tab separated, because a bone name may contain a space, a bracket or a
        /// colon — <c>NPC L Forearm [LLar]</c> — but never a tab.
        /// </remarks>
        public const string RigBonesProperty = "sk_rig_bones";

        /// <summary>
        /// Says the scene's collision filters are the Havok file's own.
        /// </summary>
        /// <remarks>
        /// Needed because a filter of zero is a real value, not an absent one: 172
        /// vanilla bodies carry it, being the ones outside the ragdoll. Without a
        /// marker, regenerating "the missing ones" renumbers those too and the round
        /// trip stops being exact — which is exactly what happened, 810 of 982.
        /// </remarks>
        public const string FiltersCarriedProperty = "sk_filters_carried";

        private const char Separator = '\t';

        /// <summary>
        /// One FBX from the two files.
        /// </summary>
        /// <param name="mesh">The creature's <c>skeleton.nif</c>.</param>
        /// <param name="havok">
        /// Its <c>skeleton.hkx</c>, when there is one. Supplies the ragdoll bone
        /// names and the rig's bone list and nothing else. Without it the names
        /// follow <paramref name="naming"/> and every non-scaffolding node is taken
        /// as a rig bone, which is what generating new content wants.
        /// </param>
        /// <param name="carryFilters">
        /// Whether a supplied Havok file's collision filters travel with the scene.
        /// They are the only way to reproduce one byte for byte, because the two
        /// identifier fields are an allocation rather than data — a generated filter
        /// is correct and matches vanilla 5.8% of the time. Turn this off to
        /// regenerate them from the ragdoll's own shape instead, which is what
        /// renumbering a rig wants.
        /// </param>
        public static FbxDocument Export(
            NifModel mesh,
            SkeletonFile? havok = null,
            RagdollNaming naming = RagdollNaming.Prefixed,
            bool carryFilters = true)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            FbxDocument document = new NifToFbx(mesh).Convert();

            var names = havok is null
                ? null
                : HavokBridge.NamesFrom(havok.Bodies.Select(b => (b.Name, b.RigBone)));

            // The mesh's own physics, which stand in wherever the Havok file has
            // nothing to say -- that is, whenever there is no Havok file.
            var derived = Fbx.NifBodyPhysics.Read(mesh);

            Dictionary<string, HKFBX.Model.RagdollBody>? bodies = null;

            if (havok is not null)
            {
                bodies = new Dictionary<string, HKFBX.Model.RagdollBody>(StringComparer.OrdinalIgnoreCase);

                foreach (HKFBX.Model.RagdollBody body in havok.Bodies)
                {
                    HKFBX.Model.RagdollBody carried = carryFilters
                        ? body
                        : body with { CollisionFilterInfo = 0u };

                    bodies[body.RigBone is { Length: > 0 } r ? r : body.Name] = carried;
                }
            }

            HashSet<string>? rig = havok is null
                ? null
                : new HashSet<string>(havok.Rig.Bones.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);

            HavokBridge.Apply(document, names, naming, rig, bodies, derived);

            if (havok is not null)
            {
                // The mesh is nearly a superset of the rig and not quite: Havok has
                // the x_ bones no mesh carries, and a few bodies -- pelt simulators
                // -- with no node to hang off. Carrying both sides and marking which
                // is which is what makes the round trip exact rather than nearly so.
                BoneUnion.Apply(document, havok);
                WriteRigBones(document, havok.Rig.Bones.Select(b => b.Name));

                if (carryFilters)
                    Mark(document, FiltersCarriedProperty, "1");
            }

            return document;
        }

        /// <summary>
        /// The Havok half back out of the FBX.
        /// </summary>
        /// <remarks>
        /// The rig comes back in the order the file lists it rather than the order
        /// the scene happens to hold, because a skeleton is its indices as much as
        /// its names: the mappers, the animation tracks and the ragdoll all address
        /// bones by position.
        /// </remarks>
        public static SkeletonFile ImportHavok(FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            SkeletonFile parsed = FbxSkeletonReader.Read(document);

            // Only fill in filters the scene did not claim to carry. A zero it did
            // carry is the original's own value and must stay.
            SkeletonFile read = Marked(document, FiltersCarriedProperty)
                ? parsed
                : WithFilters(parsed);
            IReadOnlyList<string>? order = ReadRigBones(document);

            if (order is null)
                return read;

            // SkeletonFile is a class rather than a record, so the rig is swapped by
            // rebuilding around it.
            return new SkeletonFile
            {
                Rig = Reorder(read.Rig, order),
                FloatSlots = read.FloatSlots,
                Ragdoll = read.Ragdoll,
                Bodies = read.Bodies,
                Joints = read.Joints,
                RigToRagdoll = read.RigToRagdoll,
                RagdollToRig = read.RagdollToRig,
            };
        }

        /// <summary>
        /// Fills in any collision filter the scene did not carry.
        /// </summary>
        /// <remarks>
        /// A missing filter reads back as zero, and zero is not a harmless default:
        /// it means layer zero, no system group and no identifiers, so every body
        /// collides with the parent it hangs from and the ragdoll tears itself apart
        /// the moment it is enabled. A scene that did not carry its filters gets them
        /// allocated from the ragdoll's own hierarchy instead — correct, though not
        /// the numbers the original had. See <see cref="RagdollFilter"/>.
        /// </remarks>
        public static SkeletonFile WithFilters(SkeletonFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (file.Bodies.Count == 0 || file.Bodies.All(b => b.CollisionFilterInfo != 0u))
                return file;

            // The ragdoll's tree, as body names.
            var parentOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (file.Ragdoll is { } ragdoll)
            {
                var present = new HashSet<string>(
                    file.Bodies.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < ragdoll.Bones.Count; i++)
                {
                    int parent = ragdoll.Bones[i].ParentIndex;

                    if (parent >= 0
                        && present.Contains(ragdoll.Bones[i].Name)
                        && present.Contains(ragdoll.Bones[parent].Name))
                    {
                        parentOf[ragdoll.Bones[i].Name] = ragdoll.Bones[parent].Name;
                    }
                }
            }

            Dictionary<string, uint> allocated = RagdollFilter.Allocate(
                file.Bodies.Select(b => b.Name).ToList(), parentOf);

            var filled = new List<HKFBX.Model.RagdollBody>(file.Bodies.Count);

            foreach (HKFBX.Model.RagdollBody body in file.Bodies)
            {
                // Only the ones that arrived empty: a carried filter is the original
                // and must not be renumbered.
                filled.Add(body.CollisionFilterInfo != 0u
                    ? body
                    : body with
                    {
                        CollisionFilterInfo = body.InRagdoll
                            ? allocated.GetValueOrDefault(body.Name)
                            : RagdollFilter.Pack(RagdollFilter.RagdollLayer,
                                                 RagdollFilter.LooseSystemGroup, 0, 0),
                    });
            }

            return new SkeletonFile
            {
                Rig = file.Rig,
                FloatSlots = file.FloatSlots,
                Ragdoll = file.Ragdoll,
                Bodies = filled,
                Joints = file.Joints,
                RigToRagdoll = file.RigToRagdoll,
                RagdollToRig = file.RagdollToRig,
            };
        }

        /// <summary>The mesh half, which NIFBX rebuilds on its own.</summary>
        public static NifModel ImportMesh(FbxDocument document, NifXmlDatabase database)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(database);

            return new FbxToNif(new FbxScene(document)).Convert(database);
        }

        /// <summary>Whether a scene says which of its nodes the rig wants.</summary>
        public static IReadOnlyList<string>? ReadRigBones(FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);

            foreach (FbxObject o in scene.OfClass("Model"))
            {
                string stored = o.Properties.GetString(RigBonesProperty);

                if (stored.Length > 0)
                    return stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            }

            return null;
        }

        private static void WriteRigBones(FbxDocument document, IEnumerable<string> bones) =>
            Mark(document, RigBonesProperty, string.Join(Separator, bones));

        private static void Mark(FbxDocument document, string property, string value)
        {
            var scene = new FbxScene(document);
            FbxObject? root = scene.RootModels().FirstOrDefault() ?? scene.OfClass("Model").FirstOrDefault();

            root?.Properties.SetUserString(property, value);
        }

        private static bool Marked(FbxDocument document, string property)
        {
            var scene = new FbxScene(document);

            foreach (FbxObject o in scene.OfClass("Model"))
                if (o.Properties.GetString(property).Length > 0)
                    return true;

            return false;
        }

        /// <summary>
        /// The skeleton restricted to <paramref name="order"/> and listed in it.
        /// </summary>
        /// <remarks>
        /// Parent indices are rebuilt against the new positions. A bone whose parent
        /// did not survive is reparented to the nearest ancestor that did rather
        /// than being orphaned, which keeps the tree connected — the alternative is
        /// a rig with several roots, which nothing downstream expects.
        /// </remarks>
        private static Skeleton Reorder(Skeleton skeleton, IReadOnlyList<string> order)
        {
            var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < order.Count; i++)
                position.TryAdd(order[i], i);

            var byName = new Dictionary<string, Bone>(StringComparer.OrdinalIgnoreCase);
            foreach (Bone bone in skeleton.Bones)
                byName.TryAdd(bone.Name, bone);

            var kept = new List<Bone>(order.Count);

            foreach (string name in order)
            {
                if (!byName.TryGetValue(name, out Bone? bone))
                    continue;

                int parent = bone.ParentIndex;
                int index = -1;

                while (parent >= 0 && parent < skeleton.Bones.Count)
                {
                    if (position.TryGetValue(skeleton.Bones[parent].Name, out int found))
                    {
                        index = found;
                        break;
                    }

                    parent = skeleton.Bones[parent].ParentIndex;
                }

                // The carried list is authoritative for spelling as well as for
                // membership: the mesh and the hkx disagree about the case of some
                // bones, and it is the hkx's spelling the mappers and the behaviour
                // graph reference.
                kept.Add(bone with { Name = name, ParentIndex = index });
            }

            return new Skeleton { Name = skeleton.Name, Bones = kept };
        }
    }
}
