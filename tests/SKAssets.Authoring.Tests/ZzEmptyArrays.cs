using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Arrays that hold nothing, ours beside the file we copied. An empty one is what the
// Creation Kit's allocator complains about with "aSize != 0": a request for no bytes.
public sealed class ZzEmptyArrays
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string ours = Environment.GetEnvironmentVariable("NIF_OURS") ?? "";
        string theirs = Environment.GetEnvironmentVariable("NIF_THEIRS") ?? "";
        if (outDir.Length == 0 || !File.Exists(ours) || !File.Exists(theirs)) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();

        Dictionary<string, (int Empty, int Filled)> Read(string path)
        {
            var model = NifModel.Load(path, db);
            var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

            void Walk(NifItem block, IReadOnlyList<NifItem> siblings, NifItem item, string where)
            {
                // An array is a field with a count beside it, which is how the format states one.
                if (siblings.Any(s => s.Name == "Num " + item.Name || s.Name == item.Name + " Count"))
                {
                    string key = $"{block.Name}/{where}{item.Name}";
                    (int empty, int filled) = counts.GetValueOrDefault(key);
                    counts[key] = item.Children.Count == 0 ? (empty + 1, filled) : (empty, filled + 1);
                }

                foreach (NifItem child in item.Children) Walk(block, item.Children, child, where + item.Name + "/");
            }

            foreach (NifItem block in model.Blocks)
                foreach (NifItem field in block.Children) Walk(block, block.Children, field, "");

            return counts;
        }

        var mine = Read(ours);
        var vanilla = Read(theirs);

        sb.AppendLine($"== {Path.GetFileName(ours)} against {Path.GetFileName(theirs)}");
        foreach ((string key, (int empty, int filled)) in mine.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            (int theirEmpty, int theirFilled) = vanilla.GetValueOrDefault(key);
            string mark = empty > 0 && theirFilled > 0 && theirEmpty == 0 ? "   <== theirs is never empty" : "";
            if (empty > 0 || theirEmpty > 0)
                sb.AppendLine($"   {key,-58} ours {empty} empty / {filled} filled, theirs {theirEmpty} empty / {theirFilled} filled{mark}");
        }

        foreach ((string key, (int empty, int filled)) in vanilla.OrderBy(p => p.Key, StringComparer.Ordinal))
            if (!mine.ContainsKey(key) && filled > 0)
                sb.AppendLine($"   {key,-58} ours has no such block; theirs {empty} empty / {filled} filled");

        File.WriteAllText(Path.Combine(outDir, "empty_arrays.txt"), sb.ToString());
    }
}
