using System.Text;
using HKFBX.Fbx;
using LeanMeshIO;
using Xunit;
namespace SKAssets.Export.Tests;

// What the reference skeleton FBX holds, node by node: parents, local transforms, the
// carried exact values, and the rig node types -- to author a new creature's the same way.
public sealed class ZzProbeReference
{
    [Fact]
    public void Probe()
    {
        string dir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        string path = Environment.GetEnvironmentVariable("HKSK_PROBE_FBX") ?? Path.Combine(dir, "sabrecat_skeleton.fbx");
        if (!File.Exists(path)) return;   // a probe over a file somebody has, not a test of the build
        var doc = FbxDocument.Load(path);
        var scene = new FbxScene(doc);
        var sb = new StringBuilder();
        var models = scene.OfClass("Model").ToList();
        var parentOf = new Dictionary<long, FbxObject>();
        foreach (var m in models) foreach (var c in scene.ChildrenOf(m.Id)) parentOf[c.Id] = m;
        foreach (var o in models)
        {
            string sub = o.SubClass ?? "";
            string p = parentOf.TryGetValue(o.Id, out var parent) ? parent.Name : "-";
            var (tx, ty, tz) = o.Properties.GetVector3("Lcl Translation");
            var (rx, ry, rz) = o.Properties.GetVector3("Lcl Rotation");
            string pose = o.Properties.GetString("sk_pose");
            string fb = o.Properties.GetString("sk_frame_b");
            string ragdoll = o.Properties.GetString("hkb_ragdoll_bone");
            string ctype = o.Properties.GetString("constraint_type");
            string frame = o.Properties.GetString("constraint_frame");
            if (ragdoll.Length > 0 || ctype.Length > 0 || frame.Length > 0 || o.Name.EndsWith("_capsule") || sub == "LimbNode" || sub == "Root")
                sb.AppendLine($"{sub,-9} {o.Name,-70} parent={p,-50} lcl=T({tx:F3},{ty:F3},{tz:F3}) Rdeg({rx:F2},{ry:F2},{rz:F2}) pose=[{pose[..Math.Min(60, pose.Length)]}] frameB=[{fb[..Math.Min(40, fb.Length)]}] type={ctype} frame={frame}");
        }
        File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_probe.txt"), sb.ToString());
    }
}
