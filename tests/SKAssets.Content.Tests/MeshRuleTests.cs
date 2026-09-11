using SKAssets.Content.Assets;
using SKAssets.Content.Nif;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// What a record requires of the mesh it names.
    /// </summary>
    public class MeshRuleTests
    {
        private static string[] Rules(string? record, NifProfile profile) =>
            MeshRules.Check(record, profile).Select(finding => finding.Rule).ToArray();

        /// <summary>
        /// BSXFlags is derived from the graph, so a stored value the graph cannot
        /// produce is the file misdescribing itself to the engine.
        /// </summary>
        [Fact]
        public void ReportsBsxFlagsThatTheGraphDoesNotSupport()
        {
            var wrong = Profiles.Static() with { StoredBsxFlags = 0x83, CalculatedBsxFlags = 0x82 };

            Assert.Contains("bsx-flags", Rules("Static", wrong));
        }

        [Fact]
        public void AcceptsBsxFlagsThatAgreeOrAreAbsent()
        {
            Assert.Empty(Rules("Static", Profiles.Static() with { StoredBsxFlags = 0x82, CalculatedBsxFlags = 0x82 }));
            Assert.Empty(Rules("Static", Profiles.Static() with { StoredBsxFlags = null, CalculatedBsxFlags = 0x82 }));
        }

        [Fact]
        public void AHeadPartHasToBeASkinOverTheActorsSkeleton()
        {
            Assert.Contains("headpart-skinned", Rules("HeadPart", Profiles.Static()));
            Assert.Contains("headpart-external-skeleton",
                Rules("HeadPart", Profiles.Attachment() with { HasExternalSkeleton = false }));
            Assert.Empty(Rules("HeadPart", Profiles.Attachment()));
        }

        /// <summary>
        /// Armour is only required to be an external skin once it is a skin at all:
        /// 31 of the meshes ARMA records name are props and static pieces.
        /// </summary>
        [Fact]
        public void ArmourMayBeUnskinnedButNotSkinnedToItself()
        {
            Assert.Empty(Rules("ArmorAddon", Profiles.Static()));
            Assert.Contains("armor-external-skeleton",
                Rules("ArmorAddon", Profiles.Attachment() with { HasExternalSkeleton = false }));
            Assert.Empty(Rules("ArmorAddon", Profiles.Attachment()));
        }

        [Theory]
        [InlineData("Race")]
        [InlineData("BodyPartData")]
        public void ARaceAndARagdollNameASkeleton(string record)
        {
            Assert.Contains("skeleton-expected", Rules(record, Profiles.Static()));
            Assert.Empty(Rules(record, Profiles.Skeleton()));
            Assert.Contains("skeleton-carries-geometry", Rules(record, Profiles.Skeleton() with { Shapes = 4 }));
        }

        [Fact]
        public void AnAddonNodeAndACameraPathCarryNoGeometry()
        {
            var effect = new NifProfile { RootType = "NiNode", ParticleSystems = 1 };

            Assert.Empty(Rules("AddonNode", effect));
            Assert.Contains("addon-carries-geometry", Rules("AddonNode", effect with { Shapes = 1 }));
            Assert.Contains("camera-carries-geometry", Rules("CameraShot", effect with { Shapes = 1 }));
        }

        [Fact]
        public void SaysNothingAboutARecordItHasNoRulesFor() =>
            Assert.Empty(Rules("Ingredient", Profiles.Static()));

        /// <summary>
        /// A mesh that hands its animation to a graph is incomplete without it.
        /// </summary>
        [Fact]
        public void AMissingBehaviourGraphIsAnError()
        {
            var finding = MeshRules.CheckBehaviorGraph(Profiles.Prop(), graphExists: false, isRegisteredProject: false);

            Assert.Equal("behavior-graph-missing", finding?.Rule);
            Assert.Equal(FindingSeverity.Error, finding?.Severity);
        }

        /// <summary>
        /// The graph is there and the cache does not list it, so the game loads a
        /// project with no clips in it.
        /// </summary>
        [Fact]
        public void AGraphOutsideTheCacheIsAWarning()
        {
            var finding = MeshRules.CheckBehaviorGraph(Profiles.Prop(), graphExists: true, isRegisteredProject: false);

            Assert.Equal("behavior-graph-unregistered", finding?.Rule);
        }

        [Fact]
        public void SaysNothingWhenTheGraphResolves() =>
            Assert.Null(MeshRules.CheckBehaviorGraph(Profiles.Prop(), graphExists: true, isRegisteredProject: true));

        [Fact]
        public void SaysNothingAboutAMeshWithNoGraph() =>
            Assert.Null(MeshRules.CheckBehaviorGraph(Profiles.Static(), graphExists: false, isRegisteredProject: false));
    }
}
