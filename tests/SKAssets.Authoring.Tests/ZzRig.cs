using System.Numerics;
using System.Text;
using HKFBX.Hkx;
using HKFBX.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// A Havok rig's bones and where each stands in the rest pose.
public sealed class ZzRig
{
    [Fact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        string path = Environment.GetEnvironmentVariable("RIG_PATH") ?? "";
        if (!File.Exists(path)) return;

        SkeletonFile file = HkxSkeletonFile.Read(path);
        Skeleton rig = file.Rig;
        var world = new Matrix4x4[rig.Count];
        var sb = new StringBuilder();
        var low = new Vector3(float.MaxValue); var high = new Vector3(float.MinValue);

        for (int i = 0; i < rig.Count; i++)
        {
            Bone bone = rig.Bones[i];
            Matrix4x4 local = Matrix4x4.CreateScale(bone.ReferencePose.Scale)
                * Matrix4x4.CreateFromQuaternion(bone.ReferencePose.Rotation)
                * Matrix4x4.CreateTranslation(bone.ReferencePose.Translation);
            world[i] = bone.ParentIndex < 0 ? local : local * world[bone.ParentIndex];
            low = Vector3.Min(low, world[i].Translation);
            high = Vector3.Max(high, world[i].Translation);
        }

        sb.AppendLine($"{Path.GetFileName(path)}: {rig.Count} bones, {file.Bodies.Count} ragdoll bodies");
        sb.AppendLine($"   x {low.X:F1}..{high.X:F1}, y {low.Y:F1}..{high.Y:F1}, z {low.Z:F1}..{high.Z:F1}");
        for (int i = 0; i < rig.Count; i++)
            sb.AppendLine($"   [{i,3}] {rig.Bones[i].Name,-34} parent {rig.Bones[i].ParentIndex,3}  "
                + $"({world[i].Translation.X,8:F2}, {world[i].Translation.Y,8:F2}, {world[i].Translation.Z,8:F2})");

        File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + "_rig.txt"), sb.ToString());
    }
}
