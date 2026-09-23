using System.Text;
using HKFBX.Hkx;
using NIFSharp;
using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // A bought rig corrected and written both ways, with the two compared: the checks are the
    // only thing that says whether the Havok skeleton and the NIF one describe one creature.
    public class ZzBoughtSkeleton
    {
        [Fact]
        public void Dump()
        {
            string fbx = Environment.GetEnvironmentVariable("SK_BOUGHT_FBX") ?? "";
            string havok = Environment.GetEnvironmentVariable("SK_BOUGHT_HKX") ?? "";
            if (!File.Exists(fbx)) return;

            // Never the bare temp root. A skeleton.nif dropped there makes every folder-scanning
            // route treat the whole of /tmp as a creature, which is a test failure somewhere else
            // entirely and takes a while to trace back to here.
            string outDir = Environment.GetEnvironmentVariable("SK_OUT")
                ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sk-bought-dump")).FullName;
            var db = NifXmlDatabase.LoadEmbedded();
            var sb = new StringBuilder();

            CorrectedSkeleton made = BoughtSkeleton.Correct(fbx, db);
            sb.AppendLine(made.ToString());
            sb.AppendLine($"  lift {made.Grounded.Lift:0.##} off '{made.Grounded.Lowest}', " +
                          $"shift ({made.Grounded.Shift.X:0.#},{made.Grounded.Shift.Y:0.#}), " +
                          $"turn {made.Grounded.Turn:0.#} degrees");
            sb.AppendLine($"  unmapped: {string.Join(", ", made.Renamed.Unmapped)}");
            sb.AppendLine($"  nif blocks: {made.Mesh.Blocks.Count}");

            string nifPath = Path.Combine(outDir, "skeleton.nif");
            made.Mesh.Save(nifPath);
            sb.AppendLine($"  written to {nifPath}");

            // Where the NIF puts its bones, against where the Havok rig puts them.
            sb.AppendLine();
            sb.AppendLine("--- ground and placement checks on the NIF");
            NifModel back = NifModel.Load(nifPath, db);
            foreach (MeshFinding f in CreatureChecks.Ground(back))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");
            foreach (MeshFinding f in CreatureChecks.BlockSizes(back))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");

            if (File.Exists(havok))
            {
                sb.AppendLine();
                sb.AppendLine("--- the NIF against the Havok skeleton HKSK assembled");
                var rig = HkxSkeletonFile.Read(havok);
                foreach (MeshFinding f in CreatureChecks.Skeleton(back, rig))
                    sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");

                // Which bone the NIF has not got, named rather than counted.
                var nodes = back.Blocks
                    .Where(b => b.Name is "NiNode" or "BSFadeNode" or "NiBillboardNode")
                    .Select(b => back.GetString(b, "Name") ?? "")
                    .ToHashSet(StringComparer.Ordinal);

                sb.AppendLine($"  nif nodes: {nodes.Count}");
                foreach (var bone in rig.Rig.Bones)
                    if (!nodes.Contains(bone.Name))
                        sb.AppendLine($"  !! the NIF has no node called '{bone.Name}'");

                var boned = rig.Rig.Bones.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
                foreach (string node in nodes)
                    if (!boned.Contains(node))
                        sb.AppendLine($"  ?? the NIF has a node '{node}' the rig has not");

                sb.AppendLine($"  nif block 0 is {back.Blocks[0].Name} called '{back.GetString(back.Blocks[0], "Name")}'");
            }
            else sb.AppendLine($"\n(no havok skeleton at '{havok}' to compare against)");

            // --- the body, converted from a copy of the same file with the bones renamed
            sb.AppendLine();
            sb.AppendLine("--- the body");

            NifModel body = BoughtSkeleton.Body(fbx, db);

            string bodyPath = Path.Combine(outDir, "body.nif");
            body.Save(bodyPath);
            NifModel bodyBack = NifModel.Load(bodyPath, db);

            sb.AppendLine($"  {bodyBack.Blocks.Count} blocks");
            sb.AppendLine("  shapes: " + string.Join(", ", bodyBack.Blocks
                .Where(b => b.Name is "BSTriShape" or "NiTriShape" or "BSDynamicTriShape" or "BSSubIndexTriShape")
                .Select(b => $"{b.Name} '{bodyBack.GetString(b, "Name")}'")));

            // What the skin says it is weighted to, which is the thing renaming was for.
            var bones = bodyBack.Blocks
                .Where(b => b.Name is "NiSkinInstance" or "BSDismemberSkinInstance")
                .SelectMany(sk => bodyBack.FindItem(sk, "Bones")?.Children ?? [])
                .Select(link => int.TryParse(link.Value.ToString(), out int i) && i >= 0 && i < bodyBack.Blocks.Count
                    ? bodyBack.GetString(bodyBack.Blocks[i], "Name") ?? ""
                    : "")
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            sb.AppendLine($"  skinned to {bones.Count} bones");
            foreach (string bone in bones.Take(12)) sb.AppendLine($"    {bone}");
            int old = bones.Count(b => b.StartsWith("Bip01", StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"  still under the exporter's names: {old}");

            foreach (MeshFinding f in CreatureChecks.Weights(bodyBack))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");
            foreach (MeshFinding f in CreatureChecks.SkinBounds(bodyBack))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");
            foreach (MeshFinding f in CreatureChecks.WornSlots(bodyBack))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");
            foreach (MeshFinding f in CreatureChecks.Skin(bodyBack, back))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");

            // Is the disagreement one rigid transform shared by every bone? If it is, the whole
            // mesh renders displaced by it and nothing is bent; if it is not, the skin is
            // fighting the skeleton bone by bone.
            sb.AppendLine();
            sb.AppendLine("--- is the bind off by one transform, or by many?");

            var nodeAt = new Dictionary<string, System.Numerics.Matrix4x4>(StringComparer.Ordinal);
            void Walk(NifItem at, System.Numerics.Matrix4x4 parent)
            {
                System.Numerics.Matrix4x4 here = bodyBack.GetTransform(at).ToMatrix() * parent;
                string? name = bodyBack.GetString(at, "Name");
                if (name is { Length: > 0 }) nodeAt[name] = here;
                foreach (NifItem link in bodyBack.FindItem(at, "Children")?.Children ?? [])
                    if (int.TryParse(link.Value.ToString(), out int i) && i >= 0 && i < bodyBack.Blocks.Count)
                        Walk(bodyBack.Blocks[i], here);
            }

            var rigAt = new Dictionary<string, System.Numerics.Matrix4x4>(StringComparer.Ordinal);
            void WalkRig(NifItem at, System.Numerics.Matrix4x4 parent)
            {
                System.Numerics.Matrix4x4 here = back.GetTransform(at).ToMatrix() * parent;
                string? name = back.GetString(at, "Name");
                if (name is { Length: > 0 }) rigAt[name] = here;
                foreach (NifItem link in back.FindItem(at, "Children")?.Children ?? [])
                    if (int.TryParse(link.Value.ToString(), out int i) && i >= 0 && i < back.Blocks.Count)
                        WalkRig(back.Blocks[i], here);
            }

            Walk(bodyBack.Blocks[0], System.Numerics.Matrix4x4.Identity);
            WalkRig(back.Blocks[0], System.Numerics.Matrix4x4.Identity);

            var shared = new List<System.Numerics.Matrix4x4>();
            foreach (string bone in bones)
            {
                if (!nodeAt.TryGetValue(bone, out var mine) || !rigAt.TryGetValue(bone, out var theirs)) continue;
                if (!System.Numerics.Matrix4x4.Invert(mine, out var back2)) continue;
                shared.Add(back2 * theirs);
            }

            // And the comparison the check itself makes: the skeleton's node against the
            // skin's bind matrix, which is a different pair of things.
            sb.AppendLine();
            sb.AppendLine("  skeleton node vs skin bind, per bone:");
            foreach (NifItem sk in bodyBack.Blocks.Where(b => b.Name is "NiSkinInstance" or "BSDismemberSkinInstance"))
            {
                if (bodyBack.GetRef(sk, "Data") is not { } data) continue;
                var overall = bodyBack.FindItem(data, "Skin Transform") is { } o
                    ? bodyBack.GetTransform(o).ToMatrix() : System.Numerics.Matrix4x4.Identity;
                System.Numerics.Matrix4x4.Invert(overall, out var overallInv);

                var list = bodyBack.FindItem(data, "Bone List")?.Children ?? [];
                var nodes = bodyBack.GetRefArray(sk, "Bones").ToList();
                var deltas = new List<System.Numerics.Vector3>();

                for (int i = 0; i < nodes.Count && i < list.Count; i++)
                {
                    string name = bodyBack.GetName(nodes[i]);
                    if (!rigAt.TryGetValue(name, out var standing)) continue;
                    if (bodyBack.FindItem(list[i], "Skin Transform") is not { } st) continue;
                    System.Numerics.Matrix4x4.Invert(bodyBack.GetTransform(st).ToMatrix(), out var bind);
                    var delta = standing.Translation - (bind * overallInv).Translation;
                    deltas.Add(delta);
                    if (deltas.Count <= 5)
                        sb.AppendLine($"    {name,-28} ({delta.X,8:0.##},{delta.Y,8:0.##},{delta.Z,8:0.##})");
                }

                if (deltas.Count > 1)
                {
                    float worst = deltas.Max(d => (d - deltas[0]).Length());
                    sb.AppendLine($"    {deltas.Count} bones; the others differ from the first by at most {worst:0.####}");
                }
            }
            sb.AppendLine();

            sb.AppendLine($"  {shared.Count} bones in both");
            if (shared.Count > 1)
            {
                float worst = 0;
                for (int i = 1; i < shared.Count; i++)
                    worst = Math.Max(worst, (shared[i].Translation - shared[0].Translation).Length());
                sb.AppendLine($"  first says move ({shared[0].Translation.X:0.##},{shared[0].Translation.Y:0.##},{shared[0].Translation.Z:0.##})");
                sb.AppendLine($"  the others differ from it by at most {worst:0.####} units");
            }

            File.WriteAllText(Path.Combine(outDir, "bought-skeleton.txt"), sb.ToString());
        }
    }
}
