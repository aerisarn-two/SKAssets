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

        /// <summary>The wolf's own skin: bound where its skeleton stands, and fully weighted.</summary>
        [HavokMastersFact]
        public void AShippedSkinIsBoundToItsSkeletonAndFullyWeighted()
        {
            NifModel body = NifModel.Load(Path.Combine(Wolf, "wolf.nif"), Db);

            Assert.DoesNotContain(CreatureChecks.Skin(body, Skeleton()), f => f.Severity == FindingSeverity.Error);
            Assert.Equal(FindingSeverity.Note, Assert.Single(CreatureChecks.Weights(body)).Severity);
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
