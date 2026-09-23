using System.Numerics;
using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Where a creature stands relative to the ground: the lowest bone of its skeleton and
// the lowest vertex of its body, in the file's own space.
public sealed class ZzGround
{
    [HavokMastersFact]
    public void Measure()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        string meshes = Game.Meshes ?? "";
        if (meshes.Length == 0) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();
        var floors = new List<float>();

        foreach (string path in Directory.EnumerateFiles(Path.Combine(meshes, "actors"), "skeleton.nif", SearchOption.AllDirectories).Order())
        {
            NifModel nif;
            try { nif = NifModel.Load(path, db); } catch { continue; }

            var bones = CreatureChecks.NodeWorld(nif);
            if (bones.Count == 0) continue;
            float lowest = bones.Values.Min(m => m.Translation.Z);
            float highest = bones.Values.Max(m => m.Translation.Z);
            floors.Add(lowest);

            sb.AppendLine($"{Path.GetRelativePath(meshes, path),-62} bones {bones.Count,3}  "
                + $"lowest {lowest,8:F2}  highest {highest,8:F2}");
        }

        sb.AppendLine();
        sb.AppendLine($"== {floors.Count} skeletons: lowest bone at or below 1 unit on {floors.Count(f => f <= 1f)}, "
            + $"below -1 on {floors.Count(f => f < -1f)}, above 1 on {floors.Count(f => f > 1f)}");
        sb.AppendLine($"== the range of the lowest bone: {floors.Min():F2} to {floors.Max():F2}");

        File.WriteAllText(Path.Combine(outDir, "ground.txt"), sb.ToString());
    }
}
