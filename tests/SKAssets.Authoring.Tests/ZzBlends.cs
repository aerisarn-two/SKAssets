using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every parametric blend in a project: what drives it, where its arms sit, and how fast
// each arm plays. A ladder whose arms are in one scale and whose parameter is in another
// plays the wrong clip for the speed asked for.
public sealed class ZzBlends
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (outDir.Length == 0) return;
        var sb = new StringBuilder();

        foreach (string folder in (Environment.GetEnvironmentVariable("HKX_FOLDERS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(folder)) continue;
            sb.AppendLine($"== {folder}");

            foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories).Order())
            {
                if (path.Contains("Animations", StringComparison.OrdinalIgnoreCase)) continue;
                HavokFile file;
                try { file = HavokFile.Load(path); } catch { continue; }

                var names = file.First<hkbBehaviorGraphStringData>()?.m_variableNames;
                string Variable(hkbBindable? held, string field)
                {
                    var binding = held?.m_variableBindingSet?.m_bindings
                        .FirstOrDefault(b => b.m_memberPath == field);
                    return binding is null ? "-"
                        : names is not null && binding.m_variableIndex >= 0 && binding.m_variableIndex < names.Count
                            ? names[binding.m_variableIndex] : $"#{binding.m_variableIndex}";
                }

                foreach (hkbBlenderGenerator blend in file.All<hkbBlenderGenerator>())
                {
                    if (blend.m_children.Count < 2) continue;
                    var arms = blend.m_children.Select(c =>
                    {
                        string what = c.m_generator is hkbClipGenerator clip
                            ? $"{clip.m_name} x{clip.m_playbackSpeed:F2}"
                            : c.m_generator?.m_name ?? "?";
                        return $"{c.m_weight:F1}={what}";
                    });
                    sb.AppendLine($"   {Path.GetFileName(path),-26} {blend.m_name,-34} on '{Variable(blend, "blendParameter")}' "
                        + $"flags 0x{blend.m_flags:X} sync {blend.m_indexOfSyncMasterChild}");
                    sb.AppendLine($"        {string.Join("  ", arms)}");
                }
            }
        }

        File.WriteAllText(Path.Combine(outDir, "blends.txt"), sb.ToString());
    }
}
