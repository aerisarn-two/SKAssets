using System.Text;
using HKSK.Cache;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// The speed table a project carries: for each state and heading, what the animation
// delivers when the engine asks for a speed. A block cloned from another creature makes
// the engine play the clips at that creature's rate.
public sealed class ZzSpeedBlock
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (outDir.Length == 0 || meshes.Length == 0) return;
        var sb = new StringBuilder();

        void Show(string label, string root, string project)
        {
            SkyrimCache caches;
            try { caches = SkyrimCache.Load(root); } catch (Exception e) { sb.AppendLine($"{label}: {e.Message}"); return; }
            SpeedProjectBlock? block = caches.SpeedData?.Block(project);
            if (block is null) { sb.AppendLine($"{label} {project}: no speed block"); return; }

            sb.AppendLine($"{label} {project}: version {block.Version}, {block.Entries.Count} entries");
            foreach (SpeedEntry entry in block.Entries)
            {
                sb.AppendLine($"   key {entry.Key}: {entry.Records.Count} headings");
                foreach (SpeedRecord record in entry.Records.Take(3))
                {
                    var points = record.Points;
                    sb.AppendLine($"      heading {record.Direction,6:F1}: {points.Count} points, "
                        + $"asked {points[0].X:F1}..{points[^1].X:F1}, delivered {points[0].Y:F1}..{points[^1].Y:F1}");
                    sb.AppendLine($"         at 30 it delivers {record.Sample(30f):F1}, at 60 {record.Sample(60f):F1}, "
                        + $"at 120 {record.Sample(120f):F1}, at 240 {record.Sample(240f):F1}");
                }
            }
        }

        Show("ours", Path.Combine(mod, "Data", "Meshes"), "HouseCatProject");
        Show("vanilla", meshes, "SabreCatProject");
        File.WriteAllText(Path.Combine(outDir, "speed_block.txt"), sb.ToString());
    }
}
