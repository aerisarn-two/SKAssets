using System.Text;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// The animation set data: what the engine consults to let an attack play, ours beside
// the creature it was copied from.
public sealed class ZzSetData
{
    [Fact]
    public void Compare()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (mod.Length == 0 || meshes.Length == 0) return;
        var sb = new StringBuilder();

        void Show(string label, SkyrimCache cache, string project)
        {
            var entry = cache.SetData?.Project(project);
            if (entry is null) { sb.AppendLine($"{label}: no set data for {project}"); return; }
            sb.AppendLine($"{label} {project}: {entry.Sets.Sets.Count} sets");
            for (int i = 0; i < entry.Sets.Sets.Count; i++)
            {
                var set = entry.Sets.Sets[i];
                sb.AppendLine($"  set[{i}] version={set.Version} swapEvents={set.SwapEvents.Count} [{string.Join(", ", set.SwapEvents.Take(10))}]");
                sb.AppendLine($"    hand variables={set.HandVariables.Variables.Count}");
                sb.AppendLine($"    attacks={set.Attacks.Attacks.Count}: {string.Join("; ", set.Attacks.Attacks.Select(a => $"{a.EventName} moving={a.MovingAttack} [{string.Join(" ", a.Clips)}]"))}");
                sb.AppendLine($"    checksums={set.Checksums.Entries.Count}: {string.Join(", ", set.Checksums.Entries.Take(4))}");
            }
        }

        Show("ours", SkyrimCache.Load(Path.Combine(mod, "Data", "Meshes")), "HouseCatProject");
        Show("vanilla", SkyrimCache.Load(meshes), "SabreCatProject");
        File.WriteAllText(Path.Combine(mod, "setdata.txt"), sb.ToString());
    }
}
