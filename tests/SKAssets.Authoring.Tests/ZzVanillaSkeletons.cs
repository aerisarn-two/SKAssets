using HKFBX.Hkx;
using NIFSharp;

using Xunit;
namespace SKAssets.Authoring.Tests;

// Do the game's own skeleton.nif and skeleton.hkx agree, creature by creature?
public sealed class ZzVanillaSkeletons
{
    [Fact]
    public void Check()
    {
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        if (meshes.Length == 0) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var lines = new List<string>();
        foreach (string hkx in Directory.EnumerateFiles(meshes, "skeleton*.hkx", SearchOption.AllDirectories).Order())
        {
            string nif = Path.ChangeExtension(hkx, ".nif");
            if (!File.Exists(nif)) continue;
            try
            {
                var findings = CreatureChecks.Skeleton(NifModel.Load(nif, db), HkxSkeletonFile.Read(hkx));
                lines.Add($"{Path.GetRelativePath(meshes, hkx)}: {string.Join("; ", findings)}");
            }
            catch (Exception e) { lines.Add($"{Path.GetRelativePath(meshes, hkx)}: {e.Message}"); }
        }
        File.WriteAllText(Path.Combine(outDir, "vanilla_skeletons.txt"), string.Join("\n", lines));
    }
}
