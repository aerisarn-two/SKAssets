using System.Numerics;
using System.Text;
using HKFBX.Fbx;
using LeanMeshIO;
using SKAssets.Export;
using Xunit;
namespace SKAssets.Export.Tests;

// The cat's clip FBX against its ragdoll FBX: one rig, one rest pose, a manifest,
// and root motion that goes forward.
public sealed class ZzCatClips
{
    [Fact]
    public void Probe()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (mod.Length == 0) return;
        var sb = new StringBuilder();
        var clips = FbxDocument.Load(Path.Combine(mod, "Cat_Simple_Clips.fbx"));
        var ragdoll = FbxDocument.Load(Path.Combine(Path.GetDirectoryName(mod)!, "catsimple_fbx", "Cat_Simple_Ragdoll.fbx"));
        var a = FbxAnimationReader.ReadSkeleton(clips);
        var b = SkeletonExchange.ImportHavok(ragdoll).Rig;
        sb.AppendLine($"clip rig {a.Count} bones, ragdoll rig {b.Count}; same names: {a.Bones.Select(x => x.Name).Order().SequenceEqual(b.Bones.Select(x => x.Name).Order())}");
        var byName = b.Bones.ToDictionary(x => x.Name);
        float worstT = 0, worstR = 0; string worst = "";
        foreach (var bone in a.Bones)
        {
            if (!byName.TryGetValue(bone.Name, out var other)) { sb.AppendLine($"  missing {bone.Name}"); continue; }
            float t = Vector3.Distance(bone.ReferencePose.Translation, other.ReferencePose.Translation);
            float r = 1 - MathF.Abs(Quaternion.Dot(bone.ReferencePose.Rotation, other.ReferencePose.Rotation));
            if (t > worstT || r > worstR) worst = bone.Name;
            worstT = MathF.Max(worstT, t); worstR = MathF.Max(worstR, r);
        }
        sb.AppendLine($"rest pose: worst translation {worstT:F4}, worst rotation 1-dot {worstR:E2} at {worst}");
        var manifest = ClipExchange.Manifest(clips);
        var stacks = new FbxScene(clips).OfClass("AnimationStack").Select(s => s.Name).ToList();
        int matched = stacks.Count(s => ClipExchange.Names(s).Any(manifest.ContainsKey));
        sb.AppendLine($"manifest rows {manifest.Count}, stacks {stacks.Count}, stacks in manifest {matched}; e.g. {string.Join(", ", stacks.Take(3))}");
        foreach (string take in new[] { "WalkForward", "RunForward", "Turn_L_RM", "TurnCannedL90", "Idle_2", "JumpRun_RM" })
        {
            string? stack = stacks.FirstOrDefault(s => ClipExchange.Names(s).Contains(take));
            if (stack is null) { sb.AppendLine($"  {take}: no stack"); continue; }
            var motion = FbxAnimationReader.ReadRootMotion(clips, a, stack);
            var anim = FbxAnimationReader.ReadAnimation(clips, a, takeName: stack);
            var end = motion.TranslationAt(motion.Duration);
            var turn = motion.RotationAt(motion.Duration);
            float yaw = MathF.Atan2(2 * (turn.W * turn.Z + turn.X * turn.Y), 1 - 2 * (turn.Y * turn.Y + turn.Z * turn.Z)) * 180 / MathF.PI;
            sb.AppendLine($"  {take}: frames {anim.FrameCount} duration {anim.Duration:F3} tracks {anim.TrackCount}; root end ({end.X:F2},{end.Y:F2},{end.Z:F2}) yaw {yaw:F1} deg; speed {end.Length() / MathF.Max(motion.Duration, 1e-3f):F1} u/s");
        }
        File.WriteAllText(Path.Combine(mod, "clips_probe.txt"), sb.ToString());
    }
}
