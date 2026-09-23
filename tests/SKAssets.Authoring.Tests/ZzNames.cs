using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every name in a project's packfiles that spells a given word, sorted by what kind of
// name it is: a node, a variable, an event, or the text of an annotation.
public sealed class ZzNames
{
    [Fact]
    public void Find()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string folder = Environment.GetEnvironmentVariable("HKX_FOLDER") ?? "";
        string word = Environment.GetEnvironmentVariable("NAME_WORD") ?? "SabreCat";
        if (outDir.Length == 0 || !Directory.Exists(folder)) return;
        var sb = new StringBuilder();

        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories).Order())
        {
            HavokFile file;
            try { file = HavokFile.Load(path); } catch { continue; }
            var lines = new List<string>();

            void Say(string kind, string text, string where = "")
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) lines.Add($"   {kind,-12} '{text}'{where}");
            }

            foreach (hkbBehaviorGraphStringData strings in file.All<hkbBehaviorGraphStringData>())
            {
                for (int i = 0; i < strings.m_eventNames.Count; i++) Say("event", strings.m_eventNames[i], $"  [{i}]");
                for (int i = 0; i < strings.m_variableNames.Count; i++) Say("variable", strings.m_variableNames[i], $"  [{i}]");
                foreach (string name in strings.m_characterPropertyNames) Say("property", name);
            }

            foreach (hkbCharacterStringData strings in file.All<hkbCharacterStringData>())
            {
                Say("character", strings.m_name);
                foreach (string name in strings.m_animationNames) Say("animation", name);
                foreach (string name in strings.m_characterPropertyNames) Say("property", name);
            }

            foreach (IHavokObject held in file.Objects)
                if (held.GetType().GetProperty("m_name") is { CanRead: true } p && p.PropertyType == typeof(string)
                    && p.GetValue(held) is string name && name.Length > 0)
                    Say("node", name, $"  ({held.GetType().Name})");

            foreach (hkbExpressionData e in file.All<hkbExpressionData>()) Say("expression", e.m_expression ?? "");
            foreach (hkbExpressionCondition c in file.All<hkbExpressionCondition>()) Say("condition", c.m_expression ?? "");

            foreach (hkaAnnotationTrack track in file.All<hkaAnnotationTrack>())
                foreach (var a in track.m_annotations)
                    Say("annotation", a.m_text ?? "");

            if (lines.Count == 0) continue;
            sb.AppendLine($"== {Path.GetRelativePath(folder, path)}");
            foreach (string line in lines.Distinct().Order(StringComparer.Ordinal)) sb.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(outDir, "names.txt"), sb.ToString());
    }
}
