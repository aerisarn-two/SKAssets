using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// The checks that take two files.
    /// </summary>
    public class SkeletonRuleTests
    {
        private static readonly string[] Skeleton =
            ["NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]", "NPC L Hand [LHnd]", "WeaponBack"];

        [Fact]
        public void ASkinWeightedToBonesTheSkeletonHasIsFine() =>
            Assert.Empty(SkeletonRules.CheckSkin(["NPC Spine [Spn0]", "NPC L Hand [LHnd]"], Skeleton));

        /// <summary>
        /// The Falmer helmet case: weighted to human bones, mounted on a Falmer.
        /// </summary>
        [Fact]
        public void ASkinWeightedToBonesTheSkeletonLacksIsAnError()
        {
            var findings = SkeletonRules.CheckSkin(["NPC Spine [Spn0]", "NPC Head [Head]"], Skeleton);

            var finding = Assert.Single(findings);
            Assert.Equal("skin-bone-absent", finding.Rule);
            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("NPC Head [Head]", finding.Message);
        }

        [Fact]
        public void BonesAreMatchedWithoutRegardToCase() =>
            Assert.Empty(SkeletonRules.CheckSkin(["npc spine [spn0]"], Skeleton));

        /// <summary>
        /// With no skeleton to compare against there is nothing to say -- which is
        /// not the same as saying the skin is right.
        /// </summary>
        [Fact]
        public void SaysNothingWithoutASkeleton() =>
            Assert.Empty(SkeletonRules.CheckSkin(["NPC Head [Head]"], []));

        /// <summary>
        /// Every actor rig declares three handles no mesh carries. They are Havok's,
        /// and the prefix is the rule.
        /// </summary>
        [Fact]
        public void HavokOnlyHandlesAreNotMissingBones()
        {
            Assert.Empty(SkeletonRules.CheckRig(
                ["NPC Root [Root]", "x_NPC LookNode [Look]", "x_NPC Translate [Pos ]", "x_NPC Rotate [Rot ]"],
                Skeleton));
        }

        [Fact]
        public void ARigBoneTheMeshLacksIsAWarning()
        {
            var findings = SkeletonRules.CheckRig(["NPC Root [Root]", "NPC L UpperarmTwistHelper"], Skeleton);

            var finding = Assert.Single(findings);
            Assert.Equal("rig-bone-absent", finding.Rule);
            Assert.Contains("NPC L UpperarmTwistHelper", finding.Message);
        }

        /// <summary>
        /// The other direction is not a rule: a skeleton mesh carries weapon mounts,
        /// magic nodes and camera attachments the rig never mentions.
        /// </summary>
        [Fact]
        public void ExtraNodesInTheMeshAreNotAFinding() =>
            Assert.Empty(SkeletonRules.CheckRig(["NPC Root [Root]"], Skeleton));
    }
}
