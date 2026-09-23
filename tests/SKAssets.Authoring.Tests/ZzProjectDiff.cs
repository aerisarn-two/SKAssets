using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Our project's graphs against the ones they were copied from, in the terms the engine
// reads them: the variables and what they start at, the character properties, and the
// events. Anything different that was not meant to be is a fault.
public sealed class ZzProjectDiff
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string ours = Environment.GetEnvironmentVariable("HKX_OURS") ?? "";
        string theirs = Environment.GetEnvironmentVariable("HKX_THEIRS") ?? "";
        if (outDir.Length == 0 || !Directory.Exists(ours) || !Directory.Exists(theirs)) return;
        var sb = new StringBuilder();

        Dictionary<string, string> Read(string folder)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories))
            {
                if (path.Contains("Animations", StringComparison.OrdinalIgnoreCase)) continue;
                HavokFile file;
                try { file = HavokFile.Load(path); } catch { continue; }
                string who = Path.GetFileName(path);

                foreach (hkbBehaviorGraphData data in file.All<hkbBehaviorGraphData>())
                {
                    var strings = data.m_stringData;
                    if (strings is null) continue;
                    var words = data.m_variableInitialValues?.m_wordVariableValues;

                    for (int i = 0; i < strings.m_variableNames.Count; i++)
                    {
                        string type = i < data.m_variableInfos.Count ? data.m_variableInfos[i].m_type.ToString() : "?";
                        string value = words is not null && i < words.Count ? words[i].m_value.ToString() : "?";
                        found[$"variable {strings.m_variableNames[i]}"] = $"{type} start {value}   (in {who})";
                    }

                    foreach (string name in strings.m_characterPropertyNames) found[$"property {name}"] = $"(in {who})";
                    found[$"counts {who}"] = $"{strings.m_eventNames.Count} events, {strings.m_variableNames.Count} variables, "
                        + $"{strings.m_characterPropertyNames.Count} properties";
                }
            }

            return found;
        }

        var mine = Read(ours);
        var vanilla = Read(theirs);

        // A name is the same thing under a different spelling when only the creature changed.
        string Plain(string s) => s.Replace("HouseCat", "SabreCat", StringComparison.OrdinalIgnoreCase)
                                   .Replace("HouseCat", "SabreCat", StringComparison.Ordinal);

        foreach ((string key, string value) in mine.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string plain = Plain(key);
            if (!vanilla.TryGetValue(plain, out string? was)) { sb.AppendLine($"ONLY OURS  {key} = {value}"); continue; }
            if (!string.Equals(Plain(value), was, StringComparison.Ordinal))
                sb.AppendLine($"DIFFERS    {key}\n     ours {value}\n   theirs {was}");
        }

        foreach ((string key, string value) in vanilla.OrderBy(p => p.Key, StringComparer.Ordinal))
            if (!mine.ContainsKey(key) && !mine.ContainsKey(key.Replace("SabreCat", "HouseCat", StringComparison.Ordinal)))
                sb.AppendLine($"ONLY THEIRS {key} = {value}");

        File.WriteAllText(Path.Combine(outDir, "project_diff.txt"), sb.ToString());
    }
}
