using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Each block's declared size against the size it is written at, ours beside a shipped
// one of the same kind. A reader that consumes a different count says "stream size
// mismatch" and gives up on the file.
public sealed class ZzNifBlockSizes
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (outDir.Length == 0) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();

        foreach (string path in (Environment.GetEnvironmentVariable("NIF_LIST") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(path)) { sb.AppendLine($"{path}: missing"); continue; }
            var model = NifModel.Load(path, db);
            var declared = model.FindItem(model.Header, "Block Size")?.Children.Select(c => (int)c.Value.ToUInt()).ToList() ?? [];

            // What the writer would declare now, over the same tree.
            model.UpdateHeader();
            var remeasured = model.FindItem(model.Header, "Block Size")?.Children.Select(c => (int)c.Value.ToUInt()).ToList() ?? [];

            sb.AppendLine($"== {Path.GetFileName(path)}: {model.Blocks.Count} blocks, version {model.Version:x}/{model.UserVersion}/{model.BSVersion}, header says {declared.Count} sizes, file {new FileInfo(path).Length} bytes");
            for (int i = 0; i < model.Blocks.Count; i++)
            {
                string flag = i < declared.Count && i < remeasured.Count && declared[i] != remeasured[i] ? "  <== DIFFERS" : "";
                sb.AppendLine($"   [{i,2}] {model.Blocks[i].Name,-28} '{model.GetName(model.Blocks[i])}' declared {(i < declared.Count ? declared[i] : -1),7} remeasured {(i < remeasured.Count ? remeasured[i] : -1),7}{flag}");
            }
            sb.AppendLine($"   sum declared {declared.Sum()}, sum remeasured {remeasured.Sum()}");
        }

        File.WriteAllText(Path.Combine(outDir, "nif_block_sizes.txt"), sb.ToString());
    }
}
