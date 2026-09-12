using System.Globalization;
using System.Numerics;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;

namespace SKAssets.Export.Fbx
{
    /// <summary>
    /// The transforms, carried exactly beside the channels a viewer reads.
    /// </summary>
    /// <remarks>
    /// An FBX node holds its placement as <c>Lcl Translation</c> and
    /// <c>Lcl Rotation</c>, and the rotation is Euler angles. A quaternion turned
    /// into Euler angles and back is not the quaternion it started as, so a rig
    /// whose poses go out through those channels and come back cannot be the rig
    /// that went out: over the 2,602 bones of the 45 vanilla creatures, 64 came back
    /// bit-identical.
    ///
    /// Worse than the rounding, the conversion has places where it is not merely
    /// imprecise. Eight creatures had bones land tens of units out, because Euler
    /// extraction folds near a gimbal and the fold is not always undone the same way.
    ///
    /// So the channels stay — a DCC needs them to show and edit the skeleton — and
    /// the exact value travels beside them in a property. On the way back in the
    /// property wins where it is present, and the channels are the fallback for a
    /// scene that has been through a tool which did not preserve it. That is the
    /// same arrangement the collision filters and capsule shapes already use, and
    /// those are the two fields that survive the trip intact.
    ///
    /// Written with round-trip formatting. .NET's default for a float has been
    /// shortest-round-trippable since Core 3.0, so parsing what is written gives the
    /// float back exactly; <c>G6</c> or <c>G9</c> would not.
    /// </remarks>
    public static class ExactPose
    {
        /// <summary>A bone's reference pose, or a body's transform.</summary>
        public const string Property = "sk_pose";

        /// <summary>Records the exact pose of every bone and body the scene holds.</summary>
        public static int Write(FbxDocument document, SkeletonFile file)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(file);

            var scene = new FbxScene(document);
            var byName = new Dictionary<string, FbxObject>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in scene.OfClass("Model"))
                byName.TryAdd(NameEncoding.Unsanitize(model.Name), model);

            var bodies = new Dictionary<string, RagdollBody>(StringComparer.OrdinalIgnoreCase);

            foreach (RagdollBody body in file.Bodies)
                bodies.TryAdd(body.Name, body);

            static IEnumerable<FbxObject> byBody(FbxScene scene) =>
                scene.OfClass("Model").Where(model =>
                    model.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone").Length > 0);

            int written = 0;

            foreach (Bone bone in file.Rig.Bones)
                if (byName.TryGetValue(bone.Name, out FbxObject? node))
                {
                    node.Properties.SetUserString(Property, Format(bone.ReferencePose));
                    written++;
                }

            // A body's node is the one already marked with its ragdoll name, which
            // is the only way to find it reliably: the suffix differs between a
            // rigid body and a phantom, the node may be named for the bone it rides
            // or for itself, and one the union invented is named for neither.
            foreach (FbxObject node in byBody(scene))
            {
                string name = node.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone");

                if (bodies.TryGetValue(name, out RagdollBody? body))
                {
                    node.Properties.SetUserString(Property, Format(body.Transform));
                    written++;
                }
            }

            scene.Flush();
            return written;
        }

        /// <summary>
        /// Puts the carried poses back, where the scene carried any.
        /// </summary>
        public static SkeletonFile Apply(SkeletonFile file, FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);
            var poses = new Dictionary<string, BoneTransform>(StringComparer.OrdinalIgnoreCase);
            var byBodyName = new Dictionary<string, BoneTransform>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in scene.OfClass("Model"))
            {
                string stored = model.Properties.GetString(Property);

                if (stored.Length == 0 || Parse(stored) is not { } pose)
                    continue;

                poses.TryAdd(NameEncoding.Unsanitize(model.Name), pose);

                // A body answers to its ragdoll name, the same way it was found on
                // the way out.
                string body = model.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone");

                if (body.Length > 0)
                    byBodyName.TryAdd(body, pose);
            }

            if (poses.Count == 0 && byBodyName.Count == 0)
                return file;

            var bones = new List<Bone>(file.Rig.Bones.Count);

            foreach (Bone bone in file.Rig.Bones)
                bones.Add(poses.TryGetValue(bone.Name, out BoneTransform exact)
                    ? bone with { ReferencePose = exact }
                    : bone);

            var bodies = new List<RagdollBody>(file.Bodies.Count);

            foreach (RagdollBody body in file.Bodies)
                bodies.Add(byBodyName.TryGetValue(body.Name, out BoneTransform exact)
                    ? body with { Transform = exact }
                    : body);

            return new SkeletonFile
            {
                Rig = new Skeleton { Name = file.Rig.Name, Bones = bones },
                FloatSlots = file.FloatSlots,
                Ragdoll = file.Ragdoll,
                Bodies = bodies,
                Joints = file.Joints,
                RigToRagdoll = file.RigToRagdoll,
                RagdollToRig = file.RagdollToRig,
            };
        }

        /// <summary>Ten numbers: translation, rotation, scale.</summary>
        private static string Format(BoneTransform t) =>
            string.Join(' ', new[]
            {
                t.Translation.X, t.Translation.Y, t.Translation.Z,
                t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W,
                t.Scale.X, t.Scale.Y, t.Scale.Z,
            }.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        private static BoneTransform? Parse(string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 10)
                return null;

            var values = new float[10];

            for (int i = 0; i < 10; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return null;

            return new BoneTransform(
                new Vector3(values[0], values[1], values[2]),
                new Quaternion(values[3], values[4], values[5], values[6]),
                new Vector3(values[7], values[8], values[9]));
        }
    }
}
