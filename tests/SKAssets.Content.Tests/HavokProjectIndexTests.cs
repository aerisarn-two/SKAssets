using SKAssets.Content.Havok;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// Getting from the graph a mesh names to the project that owns it.
    /// </summary>
    public class HavokProjectIndexTests
    {
        private static readonly HavokProjectIndex Index = HavokProjectIndex.From(
        [
            new HavokProjectInfo("FarmhouseWindMill", ProjectKind.Prop,
                [@"Behaviors\Behavior00.hkx", @"Characters\Character00.hkx", @"CharacterAssets\SingleBoneSkeleton.hkx"]),
            new HavokProjectInfo("ChickenProject", ProjectKind.Actor, [@"Characters\Chicken.hkx"]),
        ]);

        /// <summary>
        /// The project's name is the stem of the file the mesh names, which is what
        /// ties the two together for 1,086 of the 1,102 meshes that name a graph.
        /// </summary>
        [Theory]
        [InlineData(@"Architecture\Farmhouse\FarmhouseWindMill\FarmhouseWindMill.hkx")]
        [InlineData("Architecture/Farmhouse/FarmhouseWindMill/FarmhouseWindMill.hkx")]
        [InlineData("farmhousewindmill.hkx")]
        public void ResolvesAGraphByItsStem(string graph)
        {
            var project = Index.Resolve(graph);

            Assert.Equal("FarmhouseWindMill", project?.Name);
            Assert.Equal(ProjectKind.Prop, project?.Kind);
            Assert.Equal(3, project?.Files.Count);
        }

        [Fact]
        public void ResolvesNothingForAGraphTheCacheDoesNotList() =>
            Assert.Null(Index.Resolve(@"CreationClub\_Shared\Plants\PlantActivatorBehavior.hkx"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ResolvesNothingForNoGraph(string? graph) => Assert.Null(Index.Resolve(graph));

        /// <summary>
        /// A mesh spells a graph the way a record spells a model: relative to
        /// <c>Meshes</c>.
        /// </summary>
        [Fact]
        public void PutsAGraphUnderMeshes() =>
            Assert.Equal(
                "Meshes/Architecture/Farmhouse/FarmhouseWindMill/FarmhouseWindMill.hkx",
                HavokProjectIndex.GraphPath(@"Architecture\Farmhouse\FarmhouseWindMill\FarmhouseWindMill.hkx").Path);

        [Fact]
        public void KeepsBothKindsApart()
        {
            Assert.Equal(ProjectKind.Actor, Index.Resolve("ChickenProject.hkx")?.Kind);
            Assert.Equal(2, Index.Projects.Count);
        }
    }
}
