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

            string outDir = Environment.GetEnvironmentVariable("SK_OUT") ?? Path.GetTempPath();
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

            File.WriteAllText(Path.Combine(outDir, "bought-skeleton.txt"), sb.ToString());
        }
    }
}
