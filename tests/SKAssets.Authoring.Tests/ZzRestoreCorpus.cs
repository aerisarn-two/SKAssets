using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Puts files back into the extracted meshes folder from the game's archives, by the
// path they have there. For a corpus file something has written over.
public sealed class ZzRestoreCorpus
{
    [Fact]
    public void Restore()
    {
        string list = Environment.GetEnvironmentVariable("RESTORE_LIST") ?? "";
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        string data = Environment.GetEnvironmentVariable("SKASSETS_SKYRIM_DATA") ?? "";
        if (list.Length == 0 || meshes.Length == 0 || data.Length == 0) return;

        var wanted = File.ReadAllLines(list).Where(l => l.Trim().Length > 0)
            .ToDictionary(l => ("meshes/" + l.Trim().Replace('\\', '/')).ToLowerInvariant(), l => l.Trim());
        var done = new List<string>();

        foreach (string archive in Directory.GetFiles(data, "*.bsa"))
        {
            var reader = Archive.CreateReader(GameRelease.SkyrimSE, archive);
            foreach (var file in reader.Files)
            {
                string path = file.Path.ToString().Replace('\\', '/').ToLowerInvariant();
                if (!wanted.TryGetValue(path, out string? relative)) continue;
                string to = Path.Combine(meshes, relative.Replace('\\', Path.DirectorySeparatorChar));
                File.WriteAllBytes(to, file.GetBytes());
                done.Add($"{relative} <- {Path.GetFileName(archive)} ({new FileInfo(to).Length} bytes)");
            }
        }

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(list)!, "restored.txt"), string.Join("\n", done));
        Assert.Equal(wanted.Count, done.Count);
    }
}
