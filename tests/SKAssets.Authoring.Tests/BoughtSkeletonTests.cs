using HKFBX.Hkx;
using NIFSharp;
using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// Correcting a bought rig and writing it as both skeletons a creature needs.
    /// </summary>
    public class BoughtSkeletonTests
    {
        private static readonly NifXmlDatabase Db = NifXmlDatabase.LoadEmbedded();

        private static string Bought => Environment.GetEnvironmentVariable("SK_BOUGHT_FBX") ?? "";

        private static bool Available => File.Exists(Bought);

        /// <summary>
        /// The two skeletons have to describe one creature: the Havok rig is what a clip's
        /// tracks bind to, and the NIF is what the engine makes nodes from. Built from the
        /// same corrected rig they agree exactly, which is the only thing that makes the
        /// corrections safe to apply to one of them.
        /// </summary>
        [Fact]
        public void TheNifAndTheHavokRigDescribeOneCreature()
        {
            if (!Available) return;

            CorrectedSkeleton made = BoughtSkeleton.Correct(Bought, Db);

            string folder = Path.Combine(Path.GetTempPath(), $"sk-bought-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                string nif = Path.Combine(folder, "skeleton.nif");
                string hkx = Path.Combine(folder, "skeleton.hkx");

                made.Mesh.Save(nif);
                HkxSkeletonBuilder.Write(
                    Path.Combine(Game.Meshes!, "actors", "draugr", "character assets", "skeletons.hkx"),
                    new HKFBX.Model.SkeletonFile { Rig = made.Rig },
                    hkx);

                NifModel back = NifModel.Load(nif, Db);
                var rig = HkxSkeletonFile.Read(hkx);

                // Not one bone may be missing from the mesh, and none may be anywhere else.
                var findings = CreatureChecks.Skeleton(back, rig).ToList();
                Assert.DoesNotContain(findings, f => f.Severity == FindingSeverity.Error);
                Assert.DoesNotContain(findings, f => f.Rule == "skeleton-bones");

                MeshFinding placed = Assert.Single(findings, f => f.Rule == "skeleton-placement");
                Assert.Equal(FindingSeverity.Note, placed.Severity);

                // And it stands on the floor, which a bought rig does not.
                Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.Ground(back)).Severity);
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// The NIF's root block is named after the file and the rig's own root survives as a
        /// node beneath it. A converter's topmost node becomes the root block and stops being
        /// a bone, so without something above it no node is called NPC Root [Root] -- the name
        /// the engine, the armour and the Havok binding all look a creature's root up by.
        /// </summary>
        [Fact]
        public void TheMeshKeepsTheRigsRootAsANodeUnderAFileNamedRoot()
        {
            if (!Available) return;

            CorrectedSkeleton made = BoughtSkeleton.Correct(Bought, Db, meshName: "skeleton.nif");

            NifItem root = made.Mesh.Blocks[0];
            Assert.Equal("BSFadeNode", root.Name);
            Assert.Equal("skeleton.nif", made.Mesh.GetString(root, "Name"));

            var nodes = made.Mesh.Blocks
                .Where(b => b.Name is "NiNode" or "BSFadeNode")
                .Select(b => made.Mesh.GetString(b, "Name"))
                .ToList();

            Assert.Contains("NPC Root [Root]", nodes);
            Assert.DoesNotContain("Scene", nodes);

            // Every bone of the rig is there, and the file-named root besides.
            Assert.All(made.Rig.Bones, b => Assert.Contains(b.Name, nodes));
            Assert.Equal(made.Rig.Count + 1, nodes.Count);
        }

        /// <summary>A bought rig is corrected in all three ways at once, and reports each.</summary>
        [Fact]
        public void ABoughtRigIsGroundedCentredAndTurned()
        {
            if (!Available) return;

            CorrectedSkeleton made = BoughtSkeleton.Correct(Bought, Db);

            Assert.True(made.Grounded.Lift > 1f, "a rig built round its pelvis hangs below the floor");
            Assert.True(made.Grounded.Shift.Length() > 1f, "this rig stands off to one side of its own root");
            Assert.True(Math.Abs(made.Grounded.Turn) > 90f, "this rig faces away from the way the game faces");

            // Renamed, and the bones nothing could place are named rather than guessed at.
            Assert.Contains("NPC Root [Root]", made.Rig.Bones.Select(b => b.Name));
            Assert.Contains("NPC Spine2 [Spn2]", made.Rig.Bones.Select(b => b.Name));
            Assert.Contains(made.Renamed.Unmapped, u => u.EndsWith("effector", StringComparison.OrdinalIgnoreCase));
        }
    }
}
