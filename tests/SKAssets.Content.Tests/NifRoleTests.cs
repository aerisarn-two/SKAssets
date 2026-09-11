using SKAssets.Content.Nif;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// Reading what a mesh is off what it contains.
    /// </summary>
    public class NifRoleTests
    {
        [Fact]
        public void GeometryAndCollisionIsStatic() =>
            Assert.Equal(NifRole.StaticGeometry, NifRoles.Of(Profiles.Static()));

        /// <summary>
        /// A skeleton is a skeleton whatever else is in the file -- that is why it is
        /// the first test rather than one among several.
        /// </summary>
        [Fact]
        public void ASkeletonOutranksEverythingElse()
        {
            var loaded = Profiles.Skeleton() with
            {
                Shapes = 2,
                IsSkinned = true,
                BehaviorGraph = "something.hkx",
                ParticleSystems = 1,
            };

            Assert.Equal(NifRole.ActorSkeleton, NifRoles.Of(loaded));
        }

        /// <summary>
        /// 100 armour meshes in the game are both a skin and a Havok prop. The skin
        /// wins, because bones that are not in the file are what stops it being
        /// readable on its own -- and the graph is still on the profile.
        /// </summary>
        [Fact]
        public void ASkinOverSomebodyElsesSkeletonOutranksItsBehaviourGraph()
        {
            var both = Profiles.Attachment() with { BehaviorGraph = @"DLC02\SharedBehaviors\MiraakFade\MiraakFade.hkx" };

            Assert.Equal(NifRole.SkinnedAttachment, NifRoles.Of(both));
            Assert.NotNull(both.BehaviorGraph);
        }

        [Fact]
        public void ABehaviourGraphMakesItAProp() =>
            Assert.Equal(NifRole.HavokProp, NifRoles.Of(Profiles.Prop()));

        [Fact]
        public void SkinnedToItsOwnBonesIsNotAnAttachment()
        {
            var tree = Profiles.Static() with { IsSkinned = true, HasExternalSkeleton = false };

            Assert.Equal(NifRole.SkinnedMesh, NifRoles.Of(tree));
        }

        [Fact]
        public void ControllersWithGeometryAreAnAnimatedMesh()
        {
            var door = Profiles.Static() with { ControllerManagers = 1, ControllerSequences = 2 };

            Assert.Equal(NifRole.AnimatedMesh, NifRoles.Of(door));
        }

        /// <summary>
        /// What an ADDN record names: an effect with nothing to draw of its own.
        /// </summary>
        [Fact]
        public void ParticlesWithoutGeometryAreAnEffect()
        {
            var addon = new NifProfile { RootType = "NiNode", ParticleSystems = 2, TimeControllers = 4 };

            Assert.Equal(NifRole.Effect, NifRoles.Of(addon));
        }

        [Fact]
        public void ControllersWithoutGeometryOrParticlesAreACameraPath()
        {
            var camera = new NifProfile { RootType = "NiNode", TimeControllers = 3 };

            Assert.Equal(NifRole.CameraPath, NifRoles.Of(camera));
        }

        [Fact]
        public void AnEmptyFileSaysNothing() =>
            Assert.Equal(NifRole.Unknown, NifRoles.Of(new NifProfile { RootType = "NiNode" }));

        /// <summary>
        /// The three roles a converter cannot finish on its own, because the rest of
        /// the asset is in another file.
        /// </summary>
        [Theory]
        [InlineData(NifRole.SkinnedAttachment, true)]
        [InlineData(NifRole.ActorSkeleton, true)]
        [InlineData(NifRole.HavokProp, true)]
        [InlineData(NifRole.StaticGeometry, false)]
        [InlineData(NifRole.AnimatedMesh, false)]
        [InlineData(NifRole.SkinnedMesh, false)]
        [InlineData(NifRole.Effect, false)]
        public void SaysWhichRolesNeedMoreThanTheMesh(NifRole role, bool needs) =>
            Assert.Equal(needs, NifRoles.NeedsCompanionFiles(role));
    }
}
