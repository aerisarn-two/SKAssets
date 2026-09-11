using SKAssets.Assets;
using Xunit;

namespace SKAssets.Tests
{
    /// <summary>
    /// The catalogue: what an extension means, and what a folder adds to it.
    /// </summary>
    public class AssetMediaTypeTests
    {
        [Theory]
        [InlineData(".nif", "application/vnd.bethesda.nif", AssetCategory.Model)]
        [InlineData(".dds", "image/vnd.ms-dds", AssetCategory.Texture)]
        [InlineData(".hkx", "application/vnd.havok.hkx", AssetCategory.Animation)]
        [InlineData(".pex", "application/vnd.bethesda.papyrus", AssetCategory.Script)]
        [InlineData(".psc", "text/vnd.bethesda.papyrus", AssetCategory.Script)]
        [InlineData(".tri", "application/vnd.bethesda.tri", AssetCategory.Model)]
        [InlineData(".egt", "application/vnd.bethesda.egt", AssetCategory.Model)]
        [InlineData(".seq", "application/vnd.bethesda.seq", AssetCategory.Data)]
        [InlineData(".swf", "application/vnd.adobe.flash.movie", AssetCategory.Interface)]
        [InlineData(".bik", "video/vnd.radgamettools.bink", AssetCategory.Video)]
        public void RecognisesTheFormatsAPluginNames(string extension, string mime, AssetCategory category)
        {
            var type = AssetMediaTypes.ForExtension(extension);

            Assert.Equal(mime, type.Mime);
            Assert.Equal(category, type.Category);
            Assert.True(type.IsKnown);
        }

        /// <summary>
        /// Records are written by hand and by tools, and neither is consistent about
        /// case. <c>.DDS</c> is the same texture as <c>.dds</c>.
        /// </summary>
        [Theory]
        [InlineData(".DDS")]
        [InlineData("DDS")]
        [InlineData("dds")]
        public void IgnoresCaseAndAMissingDot(string extension)
        {
            Assert.Equal("image/vnd.ms-dds", AssetMediaTypes.ForExtension(extension).Mime);
        }

        /// <summary>
        /// An unknown extension still comes back as itself, so that grouping a sweep
        /// by media type does not collapse every stranger into one heap.
        /// </summary>
        [Fact]
        public void KeepsAnUnknownExtension()
        {
            var type = AssetMediaTypes.ForExtension(".wibble");

            Assert.False(type.IsKnown);
            Assert.Equal(AssetCategory.Unknown, type.Category);
            Assert.Equal("application/octet-stream", type.Mime);
            Assert.Equal(".wibble", type.Extension);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void HasNothingToSayAboutNothing(string? extension)
        {
            Assert.Equal(AssetCategory.Unknown, AssetMediaTypes.ForExtension(extension).Category);
        }

        /// <summary>
        /// The same format doing three different jobs, told apart only by where it
        /// sits.
        /// </summary>
        [Theory]
        [InlineData("Sound/FX/Ambient/Wind.wav", AssetCategory.Sound)]
        [InlineData(@"Sound\Voice\Skyrim.esm\MaleNord\Hello_000123.wav", AssetCategory.Voice)]
        [InlineData("Music/Combat/MUSCombat_01.wav", AssetCategory.Music)]
        [InlineData("Music/Special/MUSDragon.xwm", AssetCategory.Music)]
        public void SortsAudioByWhereItLives(string path, AssetCategory category)
        {
            Assert.Equal(category, AssetMediaTypes.ForPath(path).Category);
        }

        /// <summary>
        /// A fuz is voice wherever it is put: it is a dialogue line with its lip sync
        /// attached, and nothing else uses the format.
        /// </summary>
        [Fact]
        public void TreatsFuzAndLipAsVoiceRegardless()
        {
            Assert.Equal(AssetCategory.Voice, AssetMediaTypes.ForPath("elsewhere/line.fuz").Category);
            Assert.Equal(AssetCategory.Voice, AssetMediaTypes.ForPath("elsewhere/line.lip").Category);
        }

        /// <summary>
        /// Folders only ever move audio between audio categories. A model under
        /// <c>Music</c> is still a model.
        /// </summary>
        [Fact]
        public void DoesNotLetAFolderOverruleTheFormat()
        {
            Assert.Equal(AssetCategory.Model, AssetMediaTypes.ForPath("Music/odd/thing.nif").Category);
        }

        [Fact]
        public void CataloguesEachExtensionOnce()
        {
            var duplicates = AssetMediaTypes.Known
                .GroupBy(type => type.Extension, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            Assert.Empty(duplicates);
        }

        [Fact]
        public void SpellsEveryExtensionTheWayItLooksItUp()
        {
            Assert.All(AssetMediaTypes.Known, type =>
            {
                Assert.StartsWith(".", type.Extension);
                Assert.Equal(type.Extension.ToLowerInvariant(), type.Extension);
                Assert.NotEqual(AssetCategory.Unknown, type.Category);
            });
        }
    }
}
