using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every expression a project's graphs evaluate, ours beside the one it was copied from.
public sealed class ZzExpressions
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

                foreach (hkbEvaluateExpressionModifier m in file.All<hkbEvaluateExpressionModifier>())
                    foreach (var e in m.m_expressions?.m_expressionsData ?? [])
                        if (e.m_expression is { Length: > 0 })
                            sb.AppendLine($"   {Path.GetFileName(path),-26} {m.m_name,-34} {e.m_expression}");

                foreach (hkbExpressionCondition c in file.All<hkbExpressionCondition>())
                    if (c.m_expression is { Length: > 0 } text && text.Contains("Turn", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine($"   {Path.GetFileName(path),-26} {"(condition)",-34} {text}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "expressions.txt"), sb.ToString());
    }
}
