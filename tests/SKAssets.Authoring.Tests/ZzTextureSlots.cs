using System.Text;
using NIFSharp;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // Which texture slots the game's own creature bodies fill, and with what.
    public class ZzTextureSlots
    {
        [Fact]
        public void Dump()
        {
            string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
            if (!Directory.Exists(meshes)) return;

            string outDir = Environment.GetEnvironmentVariable("SK_OUT")
                ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sk-bought-dump")).FullName;

            var db = NifXmlDatabase.LoadEmbedded();
            var sb = new StringBuilder();
            var filled = new Dictionary<int, int>();
            var suffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int sets = 0;

            foreach (string path in new[]
                     {
                         "draugr/draugrmale.nif",
                         "canine/character assets wolf/wolf.nif",
                         "sabrecat/sabrecat.nif",
                         "character/character assets/malebody_1.nif",
                     })
            {
                string full = Path.Combine(meshes, "actors", path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) { sb.AppendLine($"(absent) {path}"); continue; }

                NifModel nif = NifModel.Load(full, db);
                sb.AppendLine($"=== {path}");

                foreach (NifItem set in nif.Blocks.Where(b => b.Name == "BSShaderTextureSet"))
                {
                    sets++;
                    var textures = nif.FindItem(set, "Textures");
                    for (int i = 0; i < (textures?.Children.Count ?? 0); i++)
                    {
                        string name = textures!.Children[i].Value.ToString() ?? "";
                        if (name.Length == 0) continue;
                        filled[i] = filled.GetValueOrDefault(i) + 1;
                        sb.AppendLine($"    [{i}] {name}");

                        string stem = Path.GetFileNameWithoutExtension(name);
                        int under = stem.LastIndexOf('_');
                        string suffix = under > 0 ? stem[under..] : "(none)";
                        suffixes[suffix] = suffixes.GetValueOrDefault(suffix) + 1;
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{sets} texture sets");
            sb.AppendLine("slots filled: " + string.Join(", ", filled.OrderBy(k => k.Key).Select(k => $"[{k.Key}] x{k.Value}")));
            sb.AppendLine("suffixes: " + string.Join(", ", suffixes.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}")));

            File.WriteAllText(Path.Combine(outDir, "textureslots.txt"), sb.ToString());
        }
    }
}
