using HKFBX.Hkx;
using NIFSharp;
using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// The checks, against the game's own creatures and against files broken on purpose.
    /// </summary>
    public class CreatureChecksTests
    {
        private static readonly NifXmlDatabase Db = NifXmlDatabase.LoadEmbedded();

        private static string Wolf => Path.Combine(Game.Meshes!, "actors", "canine", "character assets wolf");

        private static NifModel Skeleton() => NifModel.Load(Path.Combine(Wolf, "skeleton.nif"), Db);

        /// <summary>
        /// A root scaled by 100 is the thing a Blender export does by default, and is what the
        /// check exists for: every bone the same factor out, named as a scale rather than as a
        /// bone in the wrong place.
        /// </summary>
        [HavokMastersFact]
        public void AScaledRootIsReportedAsAScale()
        {
            NifModel skeleton = Skeleton();
            var havok = HkxSkeletonFile.Read(Path.Combine(Wolf, "skeleton.hkx"));

            NifItem root = skeleton.Blocks[0];
            NifTransform was = skeleton.GetTransform(root);
            skeleton.SetTransform(root, new NifTransform(was.Translation, was.Rotation, was.Scale * 100f));

            MeshFinding finding = Assert.Single(
                CreatureChecks.Skeleton(skeleton, havok).Where(f => f.Rule == "skeleton-placement"));

            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("100", finding.Message, StringComparison.Ordinal);
            Assert.Contains("times the size", finding.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// The wolf against its own rig: a handful of bones apart, which the game itself has --
        /// a warning, not an error, or every creature made from a shipped one reports a failure
        /// it inherited.
        /// </summary>
        [HavokMastersFact]
        public void AShippedCreatureAgreesWithItsOwnRigOrIsOnlyWarnedAbout()
        {
            var findings = CreatureChecks.Skeleton(Skeleton(), HkxSkeletonFile.Read(Path.Combine(Wolf, "skeleton.hkx")));

            Assert.DoesNotContain(findings, f => f.Severity == FindingSeverity.Error);
            Assert.Contains(findings, f => f.Rule == "skeleton-placement");
        }

        /// <summary>
        /// A creature is authored standing on the origin. All 52 of the game's skeletons have
        /// their lowest bone at or below one unit and not one above it.
        /// </summary>
        [HavokMastersFact]
        public void ARigThatDoesNotStandOnTheGroundIsReported()
        {
            NifModel skeleton = Skeleton();
            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.Ground(skeleton)).Severity);

            // Lifted: the creature floats.
            NifItem root = skeleton.Blocks[0];
            NifTransform was = skeleton.GetTransform(root);
            skeleton.SetTransform(root, new NifTransform(
                new NifVector3(was.Translation.X, was.Translation.Y, was.Translation.Z + 40f), was.Rotation, was.Scale));

            MeshFinding floating = Assert.Single(CreatureChecks.Ground(skeleton));
            Assert.Equal(FindingSeverity.Error, floating.Severity);
            Assert.Contains("floats", floating.Message, StringComparison.Ordinal);

            // Dropped: a rig built around its pelvis rather than its feet.
            skeleton.SetTransform(root, new NifTransform(
                new NifVector3(was.Translation.X, was.Translation.Y, was.Translation.Z - 40f), was.Rotation, was.Scale));

            MeshFinding buried = Assert.Single(CreatureChecks.Ground(skeleton));
            Assert.Equal(FindingSeverity.Error, buried.Severity);
            Assert.Contains("below the ground", buried.Message, StringComparison.Ordinal);
        }

        /// <summary>The wolf's own skin: bound where its skeleton stands, and fully weighted.</summary>
        [HavokMastersFact]
        public void AShippedSkinIsBoundToItsSkeletonAndFullyWeighted()
        {
            NifModel body = NifModel.Load(Path.Combine(Wolf, "wolf.nif"), Db);

            Assert.DoesNotContain(CreatureChecks.Skin(body, Skeleton()), f => f.Severity == FindingSeverity.Error);
            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.Weights(body)).Severity);
        }

        /// <summary>
        /// A worn mesh names the slot it is worn in, in every partition; a converter that does
        /// not know the slot writes 0, which the game reads as a biped object out of range.
        /// </summary>
        [HavokMastersFact]
        public void APartitionNamingNoSlotIsReported()
        {
            NifModel body = NifModel.Load(
                Path.Combine(Game.Meshes!, "actors", "character", "character assets", "malebody_1.nif"), Db);

            MeshFinding named = Assert.Single(CreatureChecks.WornSlots(body));
            Assert.Equal(FindingSeverity.Note, named.Severity);
            Assert.Contains("32", named.Message, StringComparison.Ordinal);

            NifItem partition = body.Blocks.Where(b => b.Name == "BSDismemberSkinInstance")
                .SelectMany(b => body.FindItem(b, "Partitions")?.Children ?? []).First();
            body.FindItem(partition, "Body Part")!.Value.SetCount(0);

            MeshFinding finding = Assert.Single(CreatureChecks.WornSlots(body));
            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("out of range", finding.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// A shipped body carries a sphere on every bone, and a converted one carries none until
        /// they are measured: the bound is merged from them and there is nothing to merge.
        /// </summary>
        [HavokMastersFact]
        public void ASkinWhoseBonesCarryNoSphereIsReportedAndCanBeMeasured()
        {
            NifModel body = NifModel.Load(Path.Combine(Wolf, "wolf.nif"), Db);

            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.SkinBounds(body)).Severity);

            // Emptied, as NIFBX leaves them.
            var spheres = body.Blocks.Where(b => b.Name == "NiSkinData")
                .SelectMany(d => body.FindItem(d, "Bone List")?.Children ?? [])
                .Select(bone => body.FindItem(bone, "Bounding Sphere")!).ToList();
            var was = spheres.Select(s => float.Parse(body.FindItem(s, "Radius")!.Value.ToString()!)).ToList();
            foreach (NifItem sphere in spheres)
            {
                body.FindItem(sphere, "Center")!.Value.Set(new NifVector3(0, 0, 0));
                body.FindItem(sphere, "Radius")!.Value.SetFloat(0f);
            }

            MeshFinding finding = Assert.Single(CreatureChecks.SkinBounds(body));
            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("no number at all", finding.Message, StringComparison.Ordinal);

            // Measured again from the mesh they come back the size the game shipped them, within
            // a tenth or so either way: a sphere is fixed by which vertices it has to hold and
            // how its centre is chosen, and neither is written down anywhere.
            Assert.NotEmpty(WornMesh.Bounds(body));
            var now = spheres.Select(s => float.Parse(body.FindItem(s, "Radius")!.Value.ToString()!)).ToList();
            var both = was.Zip(now).Where(p => p.First > 0 && p.Second > 0).ToList();
            Assert.NotEmpty(both);
            Assert.All(both, p => Assert.InRange(p.Second, p.First * 0.85f, p.First * 1.25f));
        }

        /// <summary>A creature's own body is skinned without partitions at all, which is fine.</summary>
        [HavokMastersFact]
        public void ASkinWithNoPartitionsIsNotFaulted()
        {
            NifModel body = NifModel.Load(Path.Combine(Wolf, "wolf.nif"), Db);

            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.WornSlots(body)).Severity);
        }

        /// <summary>
        /// A vertex moved by nothing is drawn at the origin, which is what a converter that gives
        /// a split control point's weights to one copy leaves behind.
        /// </summary>
        [HavokMastersFact]
        public void AnUnweightedVertexIsReported()
        {
            NifModel body = NifModel.Load(Path.Combine(Wolf, "wolf.nif"), Db);

            // Strip one bone's weights, which leaves the vertices only it moved with none.
            NifItem shape = body.Blocks.First(b => b.Name == "BSTriShape" && body.GetRef(b, "Skin") is not null);
            NifItem data = body.GetRef(body.GetRef(shape, "Skin")!, "Data")!;
            foreach (NifItem bone in body.FindItem(data, "Bone List")!.Children)
            {
                if (body.FindItem(bone, "Vertex Weights") is not { Children.Count: > 0 } weights) continue;
                foreach (NifItem weight in weights.Children) body.FindItem(weight, "Weight")!.Value.SetFloat(0f);
                break;
            }

            MeshFinding finding = Assert.Single(CreatureChecks.Weights(body).Where(f => f.Rule == "skin-unweighted"));
            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("collapse to the origin", finding.Message, StringComparison.Ordinal);
        }
    }
}
