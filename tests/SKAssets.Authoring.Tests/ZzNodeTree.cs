using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// A mesh's node tree and what hangs off each node: which nodes a shape sits under, and
// the extra data the Creation Kit reads before it will accept the file.
public sealed class ZzNodeTree
{
    [Fact]
    public void Dump()
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

            void Extras(NifItem block, string indent)
            {
                foreach (NifItem extra in model.GetRefArray(block, "Extra Data List"))
                {
                    string value = model.FindItem(extra, "String Data") is { } sd ? model.ResolveString(sd)
                        : model.FindItem(extra, "Integer Data") is { } i ? i.Value.ToString()!
                        : "";
                    sb.AppendLine($"{indent}  extra {extra.Name} '{model.GetName(extra)}' {value}");
                }
            }

            void Walk(NifItem block, string indent)
            {
                sb.AppendLine($"{indent}[{model.IndexOf(block),3}] {block.Name} '{model.GetName(block)}'"
                    + (model.FindItem(block, "Flags") is { } f ? $" flags {f.Value}" : "")
                    + (model.GetRef(block, "Skin") is not null ? " skinned" : "")
                    + (model.GetRef(block, "Collision Object") is { } c ? $" collision {c.Name}" : ""));
                Extras(block, indent);
                foreach (NifItem child in model.GetRefArray(block, "Children"))
                    Walk(child, indent + "   ");
            }

            Walk(model.Blocks[0], "   ");

            foreach (NifItem shape in model.Blocks.Where(b => b.Name is "BSTriShape" or "BSDynamicTriShape" or "NiTriShape"))
            {
                if (model.GetRef(shape, "Skin") is not { } skin) continue;
                NifItem? skeletonRoot = model.GetRef(skin, "Skeleton Root");
                sb.AppendLine($"   skin of '{model.GetName(shape)}': {skin.Name}, skeleton root {(skeletonRoot is null ? "NONE" : $"[{model.IndexOf(skeletonRoot)}] {skeletonRoot.Name} '{model.GetName(skeletonRoot)}'")}, "
                    + $"bones {model.GetUInt(skin, "Num Bones")}, partitions {model.FindItem(skin, "Partitions")?.Children.Count ?? 0}");
                foreach (NifItem part in model.FindItem(skin, "Partitions")?.Children ?? [])
                    sb.AppendLine($"      partition: {string.Join(", ", part.Children.Where(c => c.Children.Count == 0).Select(c => $"{c.Name}={c.Value}"))}");
            }

            // Everything that is neither a node nor geometry, listed once.
            foreach (NifItem block in model.Blocks)
                if (block.Name is not ("NiNode" or "BSFadeNode" or "BSLeafAnimNode" or "BSTriShape" or "BSDynamicTriShape" or "NiTriShape"))
                    sb.AppendLine($"   other [{model.IndexOf(block),3}] {block.Name} '{model.GetName(block)}'");
        }

        // The same question of every body the game ships: is a skinned shape ever anywhere
        // but straight under the root?
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (meshes.Length > 0)
        {
            sb.AppendLine();
            int flat = 0, nested = 0, dismembered = 0, files = 0;
            var seen = new Dictionary<uint, int>();
            var zero = new HashSet<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(meshes, "actors"), "*.nif", SearchOption.AllDirectories).Order())
            {
                NifModel model;
                try { model = NifModel.Load(path, db); } catch { continue; }
                var shapes = model.Blocks.Where(b => b.Name is "BSTriShape" or "BSDynamicTriShape" or "NiTriShape"
                                                     && model.GetRef(b, "Skin") is not null).ToList();
                if (shapes.Count == 0) continue;
                files++;

                var underRoot = new HashSet<int>(model.GetRefArray(model.Blocks[0], "Children").Select(model.IndexOf));
                foreach (NifItem shape in shapes)
                {
                    if (underRoot.Contains(model.IndexOf(shape))) flat++;
                    else { nested++; sb.AppendLine($"   nested: {Path.GetRelativePath(meshes, path)} '{model.GetName(shape)}'"); }
                    if (model.GetRef(shape, "Skin") is { Name: "BSDismemberSkinInstance" } d)
                    {
                        dismembered++;
                        foreach (NifItem part in model.FindItem(d, "Partitions")?.Children ?? [])
                        {
                            uint body = model.GetUInt(part, "Body Part");
                            seen[body] = seen.GetValueOrDefault(body) + 1;
                            if (body == 0) zero.Add(Path.GetRelativePath(meshes, path));
                        }
                    }
                }
            }

            sb.AppendLine($"== {files} skinned files: {flat} shapes under the root, {nested} deeper, {dismembered} dismembered");
            sb.AppendLine($"== body parts used: {string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key} x{p.Value}"))}");
            sb.AppendLine($"== files with a body part of 0: {zero.Count}{(zero.Count > 0 ? ": " + string.Join(", ", zero.Take(8)) : "")}");
        }

        File.WriteAllText(Path.Combine(outDir, "node_tree.txt"), sb.ToString());
    }
}
