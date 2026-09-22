using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// What a skeleton NIF carries besides its bones: the root's extra data, the nodes that
// are not bones, and where each of them sits. A rebuilt skeleton has only what the
// converter writes, and the game reads more than that.
public sealed class ZzSkeletonExtras
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
            sb.AppendLine($"== {Path.GetFileName(path)}: {model.Blocks.Count} blocks");

            // Everything that is not a node, and every node with no parent bone above it.
            for (int i = 0; i < model.Blocks.Count; i++)
            {
                NifItem block = model.Blocks[i];
                if (block.Name is "NiNode" or "BSFadeNode") continue;
                sb.AppendLine($"   [{i,3}] {block.Name,-26} '{model.GetName(block)}'");
                void Show(NifItem field, int depth)
                {
                    if (field.Name is "Name" && depth == 0) return;
                    sb.AppendLine($"        {new string(' ', 3 + depth * 3)}{field.Name,-22} {(field.Children.Count == 0 ? field.Value.ToString() : "")}");
                    if (depth < 2) foreach (NifItem child in field.Children) Show(child, depth + 1);
                }
                foreach (NifItem field in block.Children) Show(field, 0);
            }

            // The root's own extra data, which is how the game finds any of it.
            foreach (NifItem b in model.Blocks.Where(b => b.Name == "BSBoneLODExtraData"))
                foreach (NifItem e in model.FindItem(b, "BoneLOD Info")?.Children ?? [])
                    sb.AppendLine($"   bone LOD: {model.GetUInt(e, "Distance")} at '{model.GetString(e, "Bone Name")}'");

            NifItem root = model.Blocks[0];
            sb.AppendLine($"   root [{model.GetName(root)}] is a {root.Name}");
            if (model.FindItem(root, "Extra Data List") is not null)
                foreach (NifItem r in model.GetRefArray(root, "Extra Data List"))
                    sb.AppendLine($"     extra -> {r.Name} '{model.GetName(r)}'");
            else sb.AppendLine("     the root has no extra data list");

            foreach (string field in new[] { "Flags", "Controller", "Collision Object" })
                if (model.FindItem(root, field) is { } f) sb.AppendLine($"     root {field}: {f.Value}");
        }

        // And the same four, read off every skeleton the game ships, to see which of them
        // is a value and which is only a number that has to be there.
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (meshes.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("== the game's own skeletons");
            foreach (string path in Directory.EnumerateFiles(meshes, "skeleton*.nif", SearchOption.AllDirectories).Order())
            {
                NifModel model;
                try { model = NifModel.Load(path, db); } catch { continue; }
                var by = new Dictionary<string, NifItem>(StringComparer.Ordinal);
                foreach (NifItem block in model.Blocks)
                    if (block.Name is "BSXFlags" or "NiIntegerExtraData" or "BSBound" or "BSBoneLODExtraData")
                        by[model.GetName(block) ?? block.Name] = block;

                string Int(string name, string field) => by.TryGetValue(name, out var b) && model.FindItem(b, field) is { } f ? f.Value.ToString()! : "-";
                string Vec(string name, string field) => by.TryGetValue(name, out var b) && model.FindItem(b, field) is { } f ? f.Value.ToString()! : "-";
                sb.AppendLine($"{Path.GetRelativePath(meshes, path),-62} blocks {model.Blocks.Count,4} BSX {Int("BSX", "Integer Data"),4} "
                    + $"SkeletonID {Int("SkeletonID", "Integer Data"),10} boneLOD {Int("BSBoneLOD", "BoneLOD Count"),3} "
                    + $"BBX centre {Vec("BBX", "Center"),-34} size {Vec("BBX", "Dimensions")}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "skeleton_extras.txt"), sb.ToString());
    }
}
