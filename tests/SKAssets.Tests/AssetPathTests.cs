using SKAssets.Assets;
using Xunit;

namespace SKAssets.Tests
{
    /// <summary>
    /// Getting from what a record says to what an archive can be asked for.
    /// </summary>
    public class AssetPathTests
    {
        /// <summary>
        /// The ordinary case: a field that implies its folder, and a value that
        /// leaves it out.
        /// </summary>
        [Fact]
        public void PutsAPathUnderTheFolderItsFieldImplies()
        {
            Assert.Equal(
                "Textures/Cubemaps/Cave_e.dds",
                AssetPaths.Resolve(@"Cubemaps\Cave_e.dds", "Textures").Path);
        }

        /// <summary>
        /// The cell water fields in the masters spell the folder out, and prefix it
        /// with <c>Data</c> besides. Neither may be doubled.
        /// </summary>
        [Theory]
        [InlineData(@"Data\Textures\Cubemaps\Cave_e.dds")]
        [InlineData(@"Textures\Cubemaps\Cave_e.dds")]
        [InlineData("Textures/Cubemaps/Cave_e.dds")]
        [InlineData(@"\Textures\Cubemaps\Cave_e.dds")]
        [InlineData(@"  Textures\Cubemaps\Cave_e.dds  ")]
        public void LeavesAFolderAloneWhenItIsAlreadyThere(string given)
        {
            Assert.Equal(
                "Textures/Cubemaps/Cave_e.dds",
                AssetPaths.Resolve(given, "Textures").Path);
        }

        /// <summary>
        /// Case is the game's business, not ours: the base folder is present whether
        /// or not it is spelled the way the field declares it.
        /// </summary>
        [Fact]
        public void RecognisesTheFolderWhateverItsCase()
        {
            Assert.Equal(
                "textures/Cubemaps/Cave_e.dds",
                AssetPaths.Resolve(@"textures\Cubemaps\Cave_e.dds", "Textures").Path);
        }

        [Fact]
        public void LeavesThePathWhereItIsWithoutAFolder()
        {
            Assert.Equal("Cubemaps/Cave_e.dds", AssetPaths.Resolve(@"Cubemaps\Cave_e.dds", null).Path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void HasNoPathForAnEmptyField(string? given)
        {
            Assert.True(AssetPaths.Resolve(given, "Textures").IsNull);
        }
    }
}
