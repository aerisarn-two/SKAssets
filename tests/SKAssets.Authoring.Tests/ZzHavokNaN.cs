using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text;
using HKSK.Havok;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every number in every packfile of a folder, looked at for the ones that are not
// numbers: a NaN in a rest pose, a clip or a ragdoll becomes a NaN transform in the
// game, and a NaN transform culls everything under the node that holds it. A rotation
// of no length is the same fault waiting to happen, since normalising it makes one.
public sealed class ZzHavokNaN
{
    [Fact]
    public void Find()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string folder = Environment.GetEnvironmentVariable("HKX_FOLDER") ?? "";
        if (outDir.Length == 0 || folder.Length == 0 || !Directory.Exists(folder)) return;
        var sb = new StringBuilder();
        int files = 0, bad = 0;
        long looked = 0;

        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories).Order())
        {
            object root;
            try { root = HavokFile.Load(path).Root; } catch (Exception e) { sb.AppendLine($"{path}: {e.GetType().Name}"); continue; }
            files++;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var found = new List<string>();
            long before = Counted;
            Walk(root, "", seen, found);
            looked += Counted - before;
            if (found.Count == 0) continue;
            bad++;
            sb.AppendLine($"{Path.GetRelativePath(folder, path)}: {found.Count}");
            foreach (string one in found.Take(6)) sb.AppendLine($"   {one}");
        }

        sb.AppendLine($"== {files} packfiles, {bad} with a number that is not one, {looked} numbers looked at");
        File.WriteAllText(Path.Combine(outDir, "havok_nan.txt"), sb.ToString());
    }

    private static long Counted;

    private static void Walk(object? value, string where, HashSet<object> seen, List<string> found)
    {
        if (value is null || found.Count > 200) return;

        switch (value)
        {
            case float f: Counted++; if (float.IsNaN(f) || float.IsInfinity(f)) found.Add($"{where} = {f}"); return;
            case bool: case string: case Enum: return;
            case Vector4 v:
                Counted += 4;
                if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)) found.Add($"{where} = {v}");
                else if (where.Contains("otation", StringComparison.Ordinal) && v.Length() < 1e-6f) found.Add($"{where} = {v}, a rotation of no length");
                return;
            case Quaternion q:
                Counted += 4;
                if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W)) found.Add($"{where} = {q}");
                else if (q.Length() < 1e-6f) found.Add($"{where} = {q}, a rotation of no length");
                return;
            case Matrix4x4 m:
                Counted += 16;
                if (!float.IsFinite(m.M11 + m.M22 + m.M33 + m.M44 + m.M41 + m.M42 + m.M43)) found.Add($"{where} = a matrix with a value that is not a number");
                return;
            case IEnumerable list when value is not string:
                int i = 0;
                foreach (object? item in list) Walk(item, $"{where}[{i++}]", seen, found);
                return;
        }

        Type type = value.GetType();
        if (type.IsPrimitive || !seen.Add(value)) return;

        // HKX2 holds everything as properties, m_name and the rest, not as fields.
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            object? held;
            try { held = property.GetValue(value); } catch (TargetInvocationException) { continue; }
            Walk(held, $"{where}.{property.Name}", seen, found);
        }
    }
}
