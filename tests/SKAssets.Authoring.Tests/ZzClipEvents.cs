using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// What an animation tells the graph, ours against the one it replaced. An annotation is
// how a clip says a foot landed, a blow connected, or that it is finished; a clip that
// says less than the one it stands in for leaves the graph waiting.
public sealed class ZzClipEvents
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string ours = Environment.GetEnvironmentVariable("ANIM_OURS") ?? "";
        string theirs = Environment.GetEnvironmentVariable("ANIM_THEIRS") ?? "";
        if (outDir.Length == 0 || !Directory.Exists(ours) || !Directory.Exists(theirs)) return;
        var sb = new StringBuilder();

        SortedSet<string> Events(string path)
        {
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            HavokFile file;
            try { file = HavokFile.Load(path); } catch { return found; }
            foreach (hkaAnnotationTrack track in file.All<hkaAnnotationTrack>())
                foreach (var a in track.m_annotations)
                    if (a.m_text is { Length: > 0 } text) found.Add(text);
            return found;
        }

        var theirsByName = Directory.EnumerateFiles(theirs, "*.hkx")
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

        int same = 0, missing = 0, absent = 0;
        var lost = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(ours, "*.hkx").Order())
        {
            string name = Path.GetFileName(path);
            string was = name.Replace("HouseCat", "SabreCat", StringComparison.OrdinalIgnoreCase);
            if (!theirsByName.TryGetValue(name, out string? theirPath) && !theirsByName.TryGetValue(was, out theirPath))
            { absent++; continue; }

            var mine = Events(path);
            var vanilla = Events(theirPath);
            var gone = vanilla.Except(mine, StringComparer.OrdinalIgnoreCase).ToList();
            var added = mine.Except(vanilla, StringComparer.OrdinalIgnoreCase).ToList();

            if (gone.Count == 0 && added.Count == 0) { same++; continue; }
            missing++;
            sb.AppendLine($"{name}: ours {mine.Count}, theirs {vanilla.Count}");
            if (gone.Count > 0) sb.AppendLine($"    theirs said and ours does not: {string.Join(", ", gone)}");
            if (added.Count > 0) sb.AppendLine($"    ours says and theirs does not: {string.Join(", ", added)}");
            foreach (string one in gone) lost[one] = lost.GetValueOrDefault(one) + 1;
        }

        sb.AppendLine();
        sb.AppendLine($"== {same} animations say the same, {missing} differ, {absent} have no counterpart");
        sb.AppendLine("== what is said less often, and by how many animations:");
        foreach ((string name, int count) in lost.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            sb.AppendLine($"   {name,-44} {count}");

        File.WriteAllText(Path.Combine(outDir, "clip_events.txt"), sb.ToString());
    }
}
