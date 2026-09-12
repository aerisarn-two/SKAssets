using System.Numerics;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;
using NIFSharp;

namespace SKAssets.Export.Fbx
{
    /// <summary>Which file a node came from, when only one of them had it.</summary>
    public static class BoneOrigin
    {
        /// <summary>The property saying so. Absent means both files agree on it.</summary>
        public const string Property = "sk_origin";

        /// <summary>In the Havok files and not in the mesh.</summary>
        public const string Havok = "havok";

        /// <summary>In the mesh and not in the Havok files.</summary>
        public const string Mesh = "nif";
    }

    /// <summary>What the union added.</summary>
    public sealed record UnionReport(int HavokOnlyBones, int HavokOnlyBodies, int MeshOnly)
    {
        public override string ToString() =>
            $"{HavokOnlyBones} havok-only bones, {HavokOnlyBodies} havok-only bodies, {MeshOnly} mesh-only nodes";
    }

    /// <summary>
    /// Puts every bone both files know about into one scene, and says where each
    /// came from.
    /// </summary>
    /// <remarks>
    /// The mesh is very nearly a superset of the rig, but not quite, and the gap is
    /// what stopped six of the forty-five vanilla creatures round tripping exactly.
    /// It runs both ways:
    ///
    /// <list type="bullet">
    /// <item><b>Havok has bones the mesh does not.</b> The three <c>x_</c> bones
    /// every actor rig declares and no mesh carries, and the werewolf's five leaked
    /// rigging helpers — <c>UpperarmOverride</c>, <c>ExposeTransform</c> and the
    /// rest, four on the left and one on the right, which is how you can tell they
    /// were an accident.</item>
    /// <item><b>Havok has bodies the mesh does not.</b> The wolf's three pelt
    /// simulators, and one apiece on the netch, the frostbite spider and the
    /// wisp.</item>
    /// <item><b>The mesh has nodes Havok does not.</b> Weapon and magic mounts,
    /// camera attachments, IK helpers, and in the storm atronach's case a whole
    /// second unused skeleton — between two and forty-six of them.</item>
    /// </list>
    ///
    /// Dropping either side loses something real, so the scene carries both and
    /// marks the difference. An importer then takes what its own format wants: the
    /// Havok side ignores nothing, the mesh side ignores what is marked
    /// <see cref="BoneOrigin.Havok"/>.
    /// </remarks>
    public static class BoneUnion
    {
        /// <summary>
        /// Adds what the Havok files have and the mesh does not, and marks what
        /// only the mesh has.
        /// </summary>
        public static UnionReport Apply(FbxDocument document, SkeletonFile havok)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(havok);

            var scene = new FbxScene(document);

            var byName = new Dictionary<string, FbxObject>(StringComparer.OrdinalIgnoreCase);
            foreach (FbxObject model in scene.OfClass("Model"))
                byName.TryAdd(NameEncoding.Unsanitize(model.Name), model);

            // What the bridge already placed, by the ragdoll name it wrote.
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FbxObject model in scene.OfClass("Model"))
            {
                string ragdoll = model.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone");
                if (ragdoll.Length > 0) placed.Add(ragdoll);
            }

            int bones = AddMissingBones(scene, havok, byName);
            int bodies = AddMissingBodies(scene, havok, byName, placed);
            int meshOnly = MarkMeshOnly(scene, havok, byName);

            scene.Flush();

            return new UnionReport(bones, bodies, meshOnly);
        }

        /// <summary>
        /// Rig bones the mesh lacks, placed where the rig puts them.
        /// </summary>
        /// <remarks>
        /// Parents first, walking the rig in its own order, so a bone whose parent
        /// is itself missing finds it already there — the werewolf's helpers hang
        /// off real bones, but nothing guarantees that in general.
        /// </remarks>
        private static int AddMissingBones(
            FbxScene scene, SkeletonFile havok, Dictionary<string, FbxObject> byName)
        {
            int added = 0;

            for (int i = 0; i < havok.Rig.Bones.Count; i++)
            {
                Bone bone = havok.Rig.Bones[i];

                if (byName.ContainsKey(bone.Name))
                    continue;

                FbxObject node = FbxMeshWriter.AddModel(
                    scene, NameEncoding.Sanitize(bone.Name), "LimbNode", Transform(bone.ReferencePose));

                node.Properties.SetUserString(BoneOrigin.Property, BoneOrigin.Havok);

                FbxObject? parent = bone.ParentIndex >= 0
                    ? byName.GetValueOrDefault(havok.Rig.Bones[bone.ParentIndex].Name)
                    : null;

                if (parent is not null) scene.Connect(node, parent);
                else scene.ConnectToRoot(node);

                byName[bone.Name] = node;
                added++;
            }

            return added;
        }

        /// <summary>
        /// Bodies the mesh lacks, with the Havok settings the mesh cannot supply.
        /// </summary>
        /// <remarks>
        /// These have no rig bone — they are pelt simulators and the like — so
        /// there is no node to hang them off and they go under the rig's root.
        /// </remarks>
        private static int AddMissingBodies(
            FbxScene scene, SkeletonFile havok, Dictionary<string, FbxObject> byName,
            IReadOnlySet<string> placed)
        {
            int added = 0;

            foreach (RagdollBody body in havok.Bodies)
            {
                // By the body's own name rather than by the bone it rides, because
                // two bodies can ride one bone: the frostbite spider's left leg body
                // claims the right leg's bone, which is a defect in the shipped file
                // and still has to survive the trip. Ragdoll names are unique where
                // rig bones are not.
                if (placed.Contains(body.Name))
                    continue;

                FbxObject node = FbxMeshWriter.AddModel(
                    scene, NameEncoding.Sanitize(body.Name + HavokBridge.BodySuffix), "Null",
                    Transform(body.Transform));

                node.Properties.SetUserString(BoneOrigin.Property, BoneOrigin.Havok);
                node.Properties.SetUserString(HavokBridge.BodyPrefix + "ragdoll_bone", body.Name);
                node.Properties.SetUserString(HavokBridge.BodyPrefix + "rig_bone", body.RigBone ?? string.Empty);
                node.Properties.SetUserString(HavokBridge.BodyPrefix + "in_ragdoll", body.InRagdoll ? "1" : "0");

                // Every setting, not just the three the ragdoll cannot run without.
                // A body invented here has no node in the mesh, so the bridge never
                // reached it and nothing else will: leaving the rest out gave six
                // bodies a friction of zero where the file said 0.5 -- the wolf's
                // pelt simulators, the netch's capsule, the wisp's controller.
                HavokBridge.WriteBody(node, body);

                FbxObject? under = byName.GetValueOrDefault(havok.Rig.Bones[0].Name);
                if (under is not null) scene.Connect(node, under);
                else scene.ConnectToRoot(node);

                byName[body.Name + HavokBridge.BodySuffix] = node;
                added++;
            }

            return added;
        }

        /// <summary>
        /// Marks the nodes only the mesh has, so a Havok importer can pass them by.
        /// </summary>
        private static int MarkMeshOnly(
            FbxScene scene, SkeletonFile havok, Dictionary<string, FbxObject> byName)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Bone bone in havok.Rig.Bones) known.Add(bone.Name);
            foreach (RagdollBody body in havok.Bodies)
            {
                known.Add(body.Name);
                if (body.RigBone is { Length: > 0 } rig)
                {
                    known.Add(rig);
                    known.Add(rig + HavokBridge.BodySuffix);
                    known.Add(rig + HavokBridge.PhantomSuffix);
                }
            }

            int marked = 0;

            foreach (FbxObject model in scene.OfClass("Model"))
            {
                if (model.SubClass == "Mesh") continue;
                if (model.Properties.GetString(BoneOrigin.Property).Length > 0) continue;

                string plain = NameEncoding.Unsanitize(model.Name);

                if (known.Contains(plain)) continue;

                model.Properties.SetUserString(BoneOrigin.Property, BoneOrigin.Mesh);
                marked++;
            }

            return marked;
        }

        private static NifTransform Transform(BoneTransform pose)
        {
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(pose.Rotation);

            // Into the column form the file stores and NifTransform expects.
            var rotation = new NifMatrix33
            {
                M11 = m.M11, M12 = m.M21, M13 = m.M31,
                M21 = m.M12, M22 = m.M22, M23 = m.M32,
                M31 = m.M13, M32 = m.M23, M33 = m.M33,
            };

            return new NifTransform(
                new NifVector3(pose.Translation.X, pose.Translation.Y, pose.Translation.Z),
                rotation,
                pose.Scale.X == 0f ? 1f : pose.Scale.X);
        }
    }
}
