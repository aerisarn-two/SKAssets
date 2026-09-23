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
    
        /// <summary>
        /// The body, converted and made wearable: renamed onto the creature's bones, every
        /// bone given the sphere of what it moves, and every partition told its slot.
        /// </summary>
        [Fact]
        public void TheBodyIsRenamedBoundedAndSlotted()
        {
            if (!Available) return;

            NifModel body = BoughtSkeleton.Body(Bought, Db);

            var bones = body.Blocks
                .Where(b => b.Name is "NiSkinInstance" or "BSDismemberSkinInstance")
                .SelectMany(sk => body.GetRefArray(sk, "Bones"))
                .Select(body.GetName)
                .ToList();

            Assert.NotEmpty(bones);
            Assert.Contains(bones, b => b.StartsWith("NPC ", StringComparison.Ordinal));

            // Fewer than a handful may still be under the exporter's names -- a bone the biped
            // map says nothing about keeps the name it came with -- but not most of them.
            int old = bones.Count(b => b.StartsWith("Bip01", StringComparison.OrdinalIgnoreCase));
            Assert.True(old * 4 < bones.Count, $"{old} of {bones.Count} bones are still Bip01");

            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.SkinBounds(body)).Severity);

            MeshFinding slot = Assert.Single(CreatureChecks.WornSlots(body));
            Assert.Equal(FindingSeverity.Note, slot.Severity);
            Assert.Contains("32", slot.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// A skin bound before its skeleton was corrected is not a fault: one movement carries
        /// the whole bind pose onto the skeleton, so the body goes with it. A skin whose bones
        /// disagree among themselves is a fault, and the two have to be told apart -- by shape,
        /// since a rotation moves two bones by quite different amounts.
        /// </summary>
        [Fact]
        public void ABindPoseCorrectedAsAWholeIsNotedAndOneManglingItsBonesIsNot()
        {
            if (!Available) return;

            CorrectedSkeleton made = BoughtSkeleton.Correct(Bought, Db);
            NifModel body = BoughtSkeleton.Body(Bought, Db);

            string folder = Path.Combine(Path.GetTempPath(), $"sk-body-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                string nif = Path.Combine(folder, "skeleton.nif");
                made.Mesh.Save(nif);
                NifModel skeleton = NifModel.Load(nif, Db);

                // The body was converted from the bought file and the skeleton was corrected,
                // so the two rest poses are 45 units apart -- and the same shape.
                MeshFinding bind = Assert.Single(CreatureChecks.Skin(body, skeleton), f => f.Rule == "skin-bind");
                Assert.Equal(FindingSeverity.Note, bind.Severity);
                Assert.Contains("same shape", bind.Message, StringComparison.Ordinal);

                // Move one bone on its own and it is no longer the same shape.
                NifItem moved = skeleton.Blocks.First(b =>
                    b.Name == "NiNode" && skeleton.GetString(b, "Name") == "NPC L Hand [LHnd]");
                NifTransform was = skeleton.GetTransform(moved);
                skeleton.SetTransform(moved, new NifTransform(
                    new NifVector3(was.Translation.X + 25f, was.Translation.Y, was.Translation.Z),
                    was.Rotation, was.Scale));

                MeshFinding mangled = Assert.Single(CreatureChecks.Skin(body, skeleton), f => f.Rule == "skin-bind");
                Assert.Equal(FindingSeverity.Error, mangled.Severity);
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        }
}
}
