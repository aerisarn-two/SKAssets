using System.Text;
using NIFSharp;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // What the game's own skeleton NIFs call their root block.
    public class ZzNifRoots
    {
        [Fact]
        public void Dump()
        {
            string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
            if (!Directory.Exists(meshes)) return;

            string outDir = Environment.GetEnvironmentVariable("SK_OUT") ?? Path.GetTempPath();
            var db = NifXmlDatabase.LoadEmbedded();
            var sb = new StringBuilder();
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            var names = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (string path in Directory
                .GetFiles(Path.Combine(meshes, "actors"), "skeleton*.nif", SearchOption.AllDirectories)
                .Order())
            {
                NifModel nif;
                try { nif = NifModel.Load(path, db); }
                catch (Exception e) { sb.AppendLine($"! {Path.GetRelativePath(meshes, path)}: {e.Message}"); continue; }

                NifItem root = nif.Blocks[0];
                string name = nif.GetString(root, "Name") ?? "";
                kinds[root.Name] = kinds.GetValueOrDefault(root.Name) + 1;
                names[name] = names.GetValueOrDefault(name) + 1;
            }

            // And what hangs directly off that root.
            foreach (string which in new[] { "canine/character assets wolf", "draugr/character assets", "character/character assets" })
            {
                string path = Path.Combine(meshes, "actors", which.Replace('/', Path.DirectorySeparatorChar), "skeleton.nif");
                if (!File.Exists(path)) continue;

                NifModel nif = NifModel.Load(path, db);
                NifItem root = nif.Blocks[0];
                var children = nif.FindItem(root, "Children");

                sb.AppendLine($"=== {which}: root {root.Name} '{nif.GetString(root, "Name")}'");
                foreach (NifItem link in children?.Children ?? [])
                {
                    if (!int.TryParse(link.Value.ToString(), out int index)) continue;
                    if (index < 0 || index >= nif.Blocks.Count) continue;
                    NifItem kid = nif.Blocks[index];
                    sb.AppendLine($"    child {kid.Name} '{nif.GetString(kid, "Name")}'");
                }
            }
            sb.AppendLine();

            sb.AppendLine("root block kinds: " +
                string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}")));
            sb.AppendLine("root block names: " +
                string.Join(", ", names.OrderByDescending(k => k.Value).Select(k => $"'{k.Key}' x{k.Value}")));

            File.WriteAllText(Path.Combine(outDir, "nifroots.txt"), sb.ToString());
        }
    }
}
