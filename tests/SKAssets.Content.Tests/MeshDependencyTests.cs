using SKAssets.Content.Assets;
using SKAssets.Content.Havok;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// What a mesh needs beyond itself.
    /// </summary>
    public class MeshDependencyTests
    {
        private static readonly HavokProjectIndex Projects = HavokProjectIndex.From(
        [
            new HavokProjectInfo("FarmhouseWindMill", ProjectKind.Prop,
                [@"Behaviors\Behavior00.hkx", @"Characters\Character00.hkx", @"CharacterAssets\SingleBoneSkeleton.hkx"]),
        ]);

        private const string Graph = @"Architecture\Farmhouse\FarmhouseWindMill\FarmhouseWindMill.hkx";

        [Fact]
        public void AMeshWithNoGraphNeedsNothingElse() =>
            Assert.Empty(MeshDependencies.Of(Profiles.Static(), Projects));

        /// <summary>
        /// The project's files are written relative to the project, so they only make
        /// sense once the folder the graph sits in is put back on the front.
        /// </summary>
        [Fact]
        public void APropPullsInItsWholeProject()
        {
            var dependencies = MeshDependencies.Of(Profiles.Prop(Graph), Projects);

            Assert.Equal(4, dependencies.Count);

            Assert.Equal(
                "Meshes/Architecture/Farmhouse/FarmhouseWindMill/FarmhouseWindMill.hkx",
                dependencies[0].Path.Path);
            Assert.Equal(DependencyKind.BehaviorProject, dependencies[0].Kind);

            Assert.Equal(
                [
                    "Meshes/Architecture/Farmhouse/FarmhouseWindMill/Behaviors/Behavior00.hkx",
                    "Meshes/Architecture/Farmhouse/FarmhouseWindMill/Characters/Character00.hkx",
                    "Meshes/Architecture/Farmhouse/FarmhouseWindMill/CharacterAssets/SingleBoneSkeleton.hkx",
                ],
                dependencies.Skip(1).Select(dependency => dependency.Path.Path));
        }

        /// <summary>
        /// Without the cache the graph is still a dependency; what the project
        /// declares simply is not known.
        /// </summary>
        [Fact]
        public void ReportsTheGraphWithoutACacheToResolveIt()
        {
            var dependency = Assert.Single(MeshDependencies.Of(Profiles.Prop(Graph), projects: null));

            Assert.Equal(DependencyKind.BehaviorProject, dependency.Kind);
        }

        [Fact]
        public void ReportsOnlyTheGraphWhenTheCacheDoesNotListIt()
        {
            var profile = Profiles.Prop(@"CreationClub\_Shared\Plants\PlantActivatorBehavior.hkx");

            Assert.Single(MeshDependencies.Of(profile, Projects));
        }
    }
}
