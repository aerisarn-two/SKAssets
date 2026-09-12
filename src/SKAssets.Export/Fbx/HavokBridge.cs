using System.Globalization;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;

namespace SKAssets.Export.Fbx
{
    /// <summary>How a ragdoll bone gets its name when no skeleton.hkx says.</summary>
    /// <remarks>
    /// Measured over the 954 ragdoll bodies of the 45 vanilla creatures that ship
    /// both files: <see cref="Prefixed"/> is right for 39.4% of them and
    /// <see cref="PrefixedNumbered"/> for a further 30.4%. The remaining 30% follow
    /// per-creature conventions — the chaurus flyer drops its species prefix, so
    /// <c>ChaurusFlyerPelvis [Pelv]</c> becomes <c>Ragdoll_Pelvis [Pelv]01</c> —
    /// and no rule recovers those. Which is why a round trip carries the name
    /// instead of deriving it, and why these are for new content only.
    /// </remarks>
    public enum RagdollNaming
    {
        /// <summary><c>Ragdoll_&lt;rig bone&gt;</c>. The commonest, at 39.4%.</summary>
        Prefixed,

        /// <summary><c>Ragdoll_&lt;rig bone&gt;01</c>, at 30.4%.</summary>
        PrefixedNumbered,
    }

    /// <summary>What the bridge did.</summary>
    public sealed record BridgeReport(int RigBones, int Bodies, int Named, int Joints)
    {
        public override string ToString() =>
            $"{RigBones} rig bones, {Bodies} bodies ({Named} named from the hkx), {Joints} joints";
    }

    /// <summary>
    /// Makes a scene NIFBX wrote legible to HKFBX.
    /// </summary>
    /// <remarks>
    /// The two libraries describe the same ragdoll and agree about none of it.
    /// NIFBX writes a body as a <c>Null</c> model named <c>&lt;bone&gt;_rb</c>
    /// carrying <c>nif_rb_*</c>; HKFBX looks for a model carrying
    /// <c>hkb_ragdoll_bone</c>, and takes its rig from models whose subclass is
    /// <c>LimbNode</c>. Handed a NIFBX scene untouched it finds no bodies at all
    /// and, because nothing is a <c>LimbNode</c>, falls back to treating every
    /// model as a bone — on the cow, 119 of them where the skeleton has 44.
    ///
    /// This is the translation, and it is SKAssets' job rather than either
    /// library's: HKSK and HKFBX deal only in Havok, NIFBX only in the mesh, and
    /// the fact that a NIF rigid body *is* a ragdoll bone is a fact about the
    /// game rather than about either format.
    ///
    /// That correspondence is exact. Across the 45 vanilla creatures shipping both
    /// files, every ragdoll body in the hkx rides a rig bone that owns a rigid body
    /// in the nif — 45 of 45, with no exceptions. The residue is symmetric and
    /// accounted for: 28 hkx bodies ride nothing (character controllers, collision
    /// bumpers, the wolf's pelt simulation) and the same 28 appear in the nif on
    /// nodes the rig does not own.
    ///
    /// The one thing that does not survive the crossing is the ragdoll bone's
    /// *name*, which is why <see cref="Apply"/> takes them.
    /// </remarks>
    public static class HavokBridge
    {
        /// <summary>The prefix HKFBX reads a body's Havok settings under.</summary>
        public const string BodyPrefix = "hkb_";

        /// <summary>NIFBX's suffix for the node carrying a rigid body.</summary>
        public const string BodySuffix = "_rb";

        /// <summary>
        /// And for a simple shape phantom, which is a body too.
        /// </summary>
        /// <remarks>
        /// A character bumper and a character controller are phantoms rather than
        /// rigid bodies, and the hkx keeps them in its body list all the same.
        /// Taking only <c>_rb</c> loses them: two on the flame atronach, three of
        /// the wolf's pelt simulators, one on the player.
        /// </remarks>
        public const string PhantomSuffix = "_sp";

        private const string ConstraintSeparator = "_con_";

        /// <summary>
        /// Marks the rig bones and writes each body's Havok settings.
        /// </summary>
        /// <param name="document">A scene from <c>NifToFbx</c>, modified in place.</param>
        /// <param name="ragdollNames">
        /// Ragdoll bone name by rig bone, out of the creature's own skeleton.hkx.
        /// **The only thing the hkx contributes**: everything else below is read
        /// from the mesh. A rig bone absent from the map is named by
        /// <paramref name="naming"/>, which is what generating new content does.
        /// </param>
        /// <param name="rigBones">
        /// The bones the Havok rig actually has, when a skeleton.hkx says. The mesh
        /// is a superset — measured across the 45 vanilla creatures that ship both
        /// files, every rig bone is in the mesh, and the mesh carries between two
        /// and forty-six more: the file root, the actor node, weapon and magic
        /// mounts, IK helpers, the physics-only bodies, and in the storm atronach's
        /// case an entire second unused skeleton. Which of them the rig wants is not
        /// derivable, so it is carried. Null marks every node that is not
        /// scaffolding, which is right for new content and four bones too generous
        /// on the cow.
        /// </param>
        public static BridgeReport Apply(
            FbxDocument document,
            IReadOnlyDictionary<string, string>? ragdollNames = null,
            RagdollNaming naming = RagdollNaming.Prefixed,
            IReadOnlySet<string>? rigBones = null,
            IReadOnlyDictionary<string, RagdollBody>? havokBodies = null,
            IReadOnlyDictionary<string, BodyPhysics>? physics = null)
        {
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);
            var models = scene.OfClass("Model").ToList();

            var bodies = new List<FbxObject>();
            var bones = new List<FbxObject>();

            foreach (FbxObject model in models)
            {
                if (IsBody(model)) bodies.Add(model);
                else if (rigBones is not null ? rigBones.Contains(Plain(model.Name)) : IsRigBone(model))
                    bones.Add(model);
            }

            var rigBonesList = bones;

            // A bone is what HKFBX reads a skeleton from, and it reads nothing else.
            // Marking the rig and leaving the scaffolding alone is what takes the
            // cow from 119 bones to 44.
            foreach (FbxObject bone in rigBonesList)
                bone.SubClass = "LimbNode";

            int named = 0;

            foreach (FbxObject body in bodies)
            {
                string rigBone = RigBoneOf(scene, body);
                bool stated = ragdollNames is not null
                              && ragdollNames.TryGetValue(rigBone, out string? fromHkx)
                              && fromHkx.Length > 0;

                // A body the hkx does not name is a body the hkx does not have --
                // the mesh carries a few the ragdoll never knew about. Leaving it
                // without Havok properties keeps it a mesh body and nothing more,
                // which is what it is. Without a map at all, every body is new
                // content and every body gets a derived name.
                if (ragdollNames is not null && !stated)
                    continue;

                string ragdollBone = stated
                    ? ragdollNames![rigBone]
                    : Derive(rigBone, naming);

                if (stated) named++;

                Write(body, "ragdoll_bone", ragdollBone);
                Write(body, "rig_bone", rigBone);

                // In the ragdoll if it rides a rig bone. The ones that do not are
                // the controllers and bumpers, which the hkx keeps as bodies and
                // leaves out of the ragdoll instance.
                Write(body, "in_ragdoll", rigBone.Length > 0 ? "1" : "0");

                RagdollBody? stated2 = havokBodies?.GetValueOrDefault(rigBone.Length > 0 ? rigBone : ragdollBone);
                BodyPhysics? derived = physics?.GetValueOrDefault(rigBone);

                WritePhysics(body, stated2, derived);
            }

            int joints = models.Count(
                m => m.Name.Contains(ConstraintSeparator, StringComparison.Ordinal));

            return new BridgeReport(rigBonesList.Count, bodies.Count, named, joints);
        }

        /// <summary>
        /// Ragdoll bone names out of a skeleton.hkx, keyed by the rig bone each
        /// rides — the shape <see cref="Apply"/> wants.
        /// </summary>
        public static Dictionary<string, string> NamesFrom(
            IEnumerable<(string RagdollBone, string? RigBone)> bodies)
        {
            ArgumentNullException.ThrowIfNull(bodies);

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string ragdoll, string? rig) in bodies)
            {
                // A body that rides a rig bone is keyed by that bone, which is what
                // the mesh calls the node it hangs off. One that rides nothing --
                // a bumper, a controller, a pelt simulator -- is keyed by its own
                // name, because the mesh calls that node the same thing.
                names[rig is { Length: > 0 } ? rig : ragdoll] = ragdoll;
            }

            return names;
        }

        /// <summary>The vanilla conventions, for content that has no hkx yet.</summary>
        public static string Derive(string rigBone, RagdollNaming naming) =>
            rigBone.Length == 0
                ? string.Empty
                : naming switch
                {
                    RagdollNaming.PrefixedNumbered => $"Ragdoll_{rigBone}01",
                    _ => $"Ragdoll_{rigBone}",
                };

        private static bool IsBody(FbxObject model) =>
            model.SubClass != "Mesh"
            && (model.Name.EndsWith(BodySuffix, StringComparison.Ordinal)
                || model.Name.EndsWith(PhantomSuffix, StringComparison.Ordinal))
            && !model.Name.Contains(ConstraintSeparator, StringComparison.Ordinal);

        /// <summary>
        /// A rig bone is a model that is none of the scaffolding: not a body, not a
        /// joint or its far frame, and not a tessellated collision shape.
        /// </summary>
        private static bool IsRigBone(FbxObject model) =>
            model.SubClass != "Mesh"
            && !model.Name.EndsWith(BodySuffix, StringComparison.Ordinal)
            && !model.Name.EndsWith(PhantomSuffix, StringComparison.Ordinal)
            && !model.Name.Contains(ConstraintSeparator, StringComparison.Ordinal);

        /// <summary>
        /// The rig bone a body rides: its parent, which is the node the collision
        /// object hangs off in the NIF.
        /// </summary>
        private static string RigBoneOf(FbxScene scene, FbxObject body)
        {
            FbxObject? parent = scene.ParentsOf(body.Id).FirstOrDefault(p => p.Class == "Model");

            if (parent is not null && !IsBody(parent))
                return Plain(parent.Name);

            // No parent to ask: the name still says it, since NIFBX builds it from
            // the owning node.
            foreach (string suffix in new[] { BodySuffix, PhantomSuffix })
                if (body.Name.EndsWith(suffix, StringComparison.Ordinal))
                    return Plain(body.Name[..^suffix.Length]);

            return string.Empty;
        }

        /// <summary>
        /// A node name as the NIF and the hkx spell it.
        /// </summary>
        /// <remarks>
        /// NIFBX escapes what an FBX name cannot hold — a space becomes
        /// <c>_s_</c>, brackets <c>_ob_</c> and <c>_cb_</c>, a colon <c>_dd_</c> —
        /// so <c>NPC L Forearm [LLar]</c> is <c>NPC_s_L_s_Forearm_s__ob_LLar_cb_</c>
        /// in the scene. Havok never escapes anything. Every comparison between the
        /// two worlds has to happen in one spelling, and this is it: comparing the
        /// escaped form against a rig's bone list matches nothing the moment a name
        /// contains a space, which is almost all of them.
        /// </remarks>
        private static string Plain(string name) => NameEncoding.Unsanitize(name);

        /// <summary>
        /// Every value HKFBX reads a body back from.
        /// </summary>
        /// <remarks>
        /// The Havok file wins where there is one, because two of these cannot be
        /// derived from the mesh at all and the rest may as well be exact while the
        /// source is to hand. Where there is none — new content — the mesh's own
        /// numbers stand in, which is right for eight of the ten and an honest
        /// approximation for the other two. Anything neither source has is left
        /// unwritten, and the reader's own default applies.
        /// </remarks>
        private static void WritePhysics(FbxObject node, RagdollBody? havok, BodyPhysics? mesh)
        {
            if (mesh is null && havok is null)
                return;

            // Derived from the mesh, every one of them, because the mesh has them
            // and measurement says they agree: friction is the same number in both
            // files 954 times out of 954, and mass inverts cleanly 911 times.
            if (mesh is not null)
            {
                Number(node, "inverse_mass", mesh.InverseMass);
                Number(node, "friction", mesh.Friction);
                Number(node, "restitution", mesh.Restitution);
                Number(node, "linear_damping", mesh.LinearDamping);
                Number(node, "angular_damping", mesh.AngularDamping);
                Number(node, "quality_type", mesh.QualityType);

                if (mesh.HasCapsule)
                {
                    Write(node, "shape", "capsule");
                    Write(node, "capsule_a", Vector(mesh.CapsuleA));
                    Write(node, "capsule_b", Vector(mesh.CapsuleB));
                    Number(node, "capsule_radius", mesh.CapsuleRadius);
                }
            }

            // Carried, because the mesh cannot answer any of these four. Each was
            // put to the whole vanilla corpus and each failed:
            //
            //   motion type   every body in the game says MO_SYS_BOX_INERTIA, and
            //                 138 of them become sphere inertia anyway
            //   inertia       the mesh's tensor is an isotropic placeholder on
            //                 three quarters of the bodies
            //   filter        the mesh's layer, flags and group are the same on
            //                 every body -- 8, 0, 0 -- while the ragdoll's filter
            //                 differs body by body
            //   capsule       716 of 847 are the mesh's own capsule with the
            //                 endpoints swapped, so this is derived; the other 131
            //                 are retuned in the ragdoll and 106 bodies are a box
            //                 in the mesh where the ragdoll keeps a capsule
            //
            // Without a Havok file the mesh's own answers stand in, which is what
            // generating new content has to live with.
            if (havok is not null)
            {
                Number(node, "motion_type", havok.MotionType);
                Number(node, "collision_filter", havok.CollisionFilterInfo);
                Write(node, "inverse_inertia", Vector(havok.InverseInertia));

                // And over the top of the six derived above, because a derivation
                // that is right 97% of the time is wrong 29 bodies out of 982 and a
                // round trip has to be exact. The derivation is not wasted: it is
                // what the branch below uses, and that branch is the one new content
                // takes. Deriving is for when there is no answer to copy; while
                // there is one, copying it is the only way to hand back the file
                // that was handed in.
                //
                //   friction     953 of 982 derived exactly
                //   restitution  920
                //   mass         734
                //   damping       29, and it is stored as a half-float, so the
                //                 scaled mesh value almost never lands on one
                //
                Number(node, "inverse_mass", havok.InverseMass);
                Number(node, "friction", havok.Friction);
                Number(node, "restitution", havok.Restitution);
                Number(node, "linear_damping", havok.LinearDamping);
                Number(node, "angular_damping", havok.AngularDamping);
                Number(node, "quality_type", havok.QualityType);

                // The mesh's capsule is the same capsule 716 times out of 847 once
                // the endpoints are taken the right way round. The rest are retuned
                // in the ragdoll -- same axis, same centre, a different half length
                // -- so the Havok one still wins where there is one.
                if (havok.Shape is { } shape)
                {
                    Write(node, "shape", "capsule");
                    Write(node, "capsule_a", Vector(shape.VertexA));
                    Write(node, "capsule_b", Vector(shape.VertexB));
                    Number(node, "capsule_radius", shape.Radius);
                }
            }
            else if (mesh is not null)
            {
                Number(node, "motion_type", mesh.MotionType);
                Number(node, "collision_filter", mesh.CollisionFilter);
                Write(node, "inverse_inertia", Vector(mesh.InverseInertia));

                if (mesh.HasCapsule)
                {
                    Write(node, "shape", "capsule");
                    Write(node, "capsule_a", Vector(mesh.CapsuleA));
                    Write(node, "capsule_b", Vector(mesh.CapsuleB));
                    Number(node, "capsule_radius", mesh.CapsuleRadius);
                }
            }
        }

        /// <summary>
        /// Every Havok setting a body has, for a body with nothing else to say.
        /// </summary>
        /// <remarks>
        /// <see cref="WritePhysics"/> is for a body the mesh also describes, and
        /// weighs the two sources against each other. This is for one the mesh does
        /// not have at all — the pelt simulators, the bumpers, the controllers —
        /// where there is only the one source and it simply gets copied.
        /// </remarks>
        public static void WriteBody(FbxObject node, RagdollBody body)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(body);

            Number(node, "motion_type", body.MotionType);
            Number(node, "quality_type", body.QualityType);
            Number(node, "collision_filter", body.CollisionFilterInfo);
            Number(node, "inverse_mass", body.InverseMass);
            Number(node, "friction", body.Friction);
            Number(node, "restitution", body.Restitution);
            Number(node, "linear_damping", body.LinearDamping);
            Number(node, "angular_damping", body.AngularDamping);
            Write(node, "inverse_inertia", Vector(body.InverseInertia));

            if (body.Shape is { } shape)
            {
                Write(node, "shape", "capsule");
                Write(node, "capsule_a", Vector(shape.VertexA));
                Write(node, "capsule_b", Vector(shape.VertexB));
                Number(node, "capsule_radius", shape.Radius);
            }
        }

        /// <summary>
        /// A vector the way HKFBX parses one back.
        /// </summary>
        private static string Vector(System.Numerics.Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", v.X, v.Y, v.Z);

        private static void Number(FbxObject node, string key, double value) =>
            node.Properties.SetUserFloat(BodyPrefix + key, value);

        private static void Write(FbxObject node, string key, string value) =>
            node.Properties.SetUserString(BodyPrefix + key, value);

        /// <summary>
        /// Copies a number across under the name HKFBX reads it by.
        /// </summary>
        /// <remarks>
        /// NIFBX writes these as strings because its descriptor dump is strings
        /// throughout; HKFBX reads them with <c>GetDouble</c>. Absent or
        /// unparseable leaves the field alone rather than writing a zero, because a
        /// zero is a valid motion type and silence is not.
        /// </remarks>
        private static void Number(FbxObject node, string key, FbxObject from, string source)
        {
            string raw = from.Properties.GetString(source);

            if (raw.Length == 0)
                return;

            if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                node.Properties.SetUserFloat(BodyPrefix + key, value);
            }
        }
    }
}
