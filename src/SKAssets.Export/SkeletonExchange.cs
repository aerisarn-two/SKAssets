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
        public static FbxDocument Export(
            NifModel mesh,
            SkeletonFile? havok = null,
            RagdollNaming naming = RagdollNaming.Prefixed)
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
                    bodies[body.RigBone is { Length: > 0 } r ? r : body.Name] = body;
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

            SkeletonFile read = FbxSkeletonReader.Read(document);
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

        private static void WriteRigBones(FbxDocument document, IEnumerable<string> bones)
        {
            var scene = new FbxScene(document);
            FbxObject? root = scene.RootModels().FirstOrDefault() ?? scene.OfClass("Model").FirstOrDefault();

            root?.Properties.SetUserString(RigBonesProperty, string.Join(Separator, bones));
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
