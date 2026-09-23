using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Any number in a file that is not a number. One in a bound is the worst kind: the
// engine propagates it up the tree and culls everything under it.
public sealed class ZzNaN
{
    [Fact]
    public void Find()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (outDir.Length == 0) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();

        foreach (string path in (Environment.GetEnvironmentVariable("NIF_LIST") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(path)) { sb.AppendLine($"{path}: missing"); continue; }
            var model = NifModel.Load(path, db);
            int found = 0;

            void Walk(NifItem block, NifItem item, string where)
            {
                if (item.Children.Count == 0)
                {
                    string text = item.Value.ToString() ?? "";
                    if (text.Contains("NaN", StringComparison.OrdinalIgnoreCase) || text.Contains("∞", StringComparison.Ordinal)
                        || text.Contains("Infinity", StringComparison.OrdinalIgnoreCase))
                    {
                        found++;
                        if (found <= 20) sb.AppendLine($"   [{model.IndexOf(block),3}] {block.Name} '{model.GetName(block)}' {where}{item.Name} = {text}");
                    }
                    return;
                }

                foreach (NifItem child in item.Children) Walk(block, child, where + item.Name + "/");
            }

            foreach (NifItem block in model.Blocks)
                foreach (NifItem field in block.Children) Walk(block, field, "");

            sb.AppendLine($"== {Path.GetFileName(path)}: {found} values that are not numbers, of {model.Blocks.Count} blocks");

            // What a bound is computed from: the spheres stored, and the scales every one of
            // them is carried up the tree by.
            foreach (NifItem block in model.Blocks)
            {
                if (model.FindItem(block, "Bounding Sphere") is { } sphere)
                    sb.AppendLine($"   bound [{model.IndexOf(block),3}] {block.Name} '{model.GetName(block)}' "
                        + $"centre {model.FindItem(sphere, "Center")?.Value} radius {model.FindItem(sphere, "Radius")?.Value}");

                if (model.FindItem(block, "Scale") is { } scale && float.TryParse(scale.Value.ToString(), out float f) && (f == 0f || float.IsNaN(f)))
                    sb.AppendLine($"   scale [{model.IndexOf(block),3}] {block.Name} '{model.GetName(block)}' = {f}");
            }

            // A transform the engine cannot use: a rotation that is not one, or a scale of
            // nothing. Either makes a NaN out of finite numbers the moment it is composed.
            foreach (NifItem node in model.Blocks.Where(b => b.Name is "NiNode" or "BSFadeNode" or "BSTriShape" or "NiTriShape"))
            {
                NifTransform at = model.GetTransform(node);
                System.Numerics.Matrix4x4 m = at.ToMatrix();
                float det = new System.Numerics.Matrix4x4(m.M11, m.M12, m.M13, 0, m.M21, m.M22, m.M23, 0, m.M31, m.M32, m.M33, 0, 0, 0, 0, 1).GetDeterminant() / (at.Scale * at.Scale * at.Scale);
                if (at.Scale > 1e-6f && Math.Abs(Math.Abs(det) - 1f) < 1e-3f) continue;
                sb.AppendLine($"   transform [{model.IndexOf(node),3}] '{model.GetName(node)}': scale {at.Scale}, rotation determinant {det}");
            }

            foreach (NifItem data in model.Blocks.Where(b => b.Name == "NiSkinData"))
            {
                int zero = 0, spheres = 0;
                foreach (NifItem bone in model.FindItem(data, "Bone List")?.Children ?? [])
                {
                    if (model.FindItem(bone, "Skin Transform/Scale") is { } sc && float.TryParse(sc.Value.ToString(), out float bs) && bs == 0f) zero++;
                    if (model.FindItem(bone, "Bounding Sphere/Radius") is { } r && float.TryParse(r.Value.ToString(), out float rr) && rr == 0f) spheres++;
                }

                sb.AppendLine($"   skin data [{model.IndexOf(data),3}]: {model.FindItem(data, "Bone List")?.Children.Count} bones, "
                    + $"{zero} with a scale of zero, {spheres} with a bound of no size; "
                    + $"overall scale {model.FindItem(data, "Skin Transform/Scale")?.Value}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "nan.txt"), sb.ToString());
    }
}
