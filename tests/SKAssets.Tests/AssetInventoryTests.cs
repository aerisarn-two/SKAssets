using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using SKAssets.Assets;
using SKAssets.References;
using Xunit;

namespace SKAssets.Tests
{
    /// <summary>
    /// Turning references into files.
    /// </summary>
    public class AssetInventoryTests
    {
        private static AssetReference Reference(string given, string? baseFolder = null, string record = "Static") =>
            new()
            {
                Path = AssetPaths.Resolve(given, baseFolder),
                GivenPath = given,
                MediaType = AssetMediaTypes.ForPath(AssetPaths.Resolve(given, baseFolder).Path),
                Origin = AssetReferenceOrigin.Listed,
                Source = ModKey.FromName("Test", ModType.Plugin),
                Record = FormKey.Null,
                RecordType = record,
            };

        /// <summary>
        /// The masters name the same sound as <c>\Data\Sound\FX\X.wav</c> and as
        /// <c>fx\x.wav</c>. One file, two records, two spellings.
        /// </summary>
        [Fact]
        public void CollapsesTheSpellingsOfOneFile()
        {
            var inventory = AssetInventory.Build(
            [
                Reference(@"\Data\Sound\FX\XXX_Placeholder_Silence.wav"),
                Reference(@"fx\xxx_placeholder_silence.wav", "Sound"),
            ]);

            var entry = Assert.Single(inventory.Entries);

            Assert.Equal(1, inventory.Count);
            Assert.Equal(2, entry.ReferenceCount);
            Assert.Equal(2, entry.Spellings.Count);
            Assert.Equal(AssetCategory.Sound, entry.MediaType.Category);
        }

        [Fact]
        public void KeepsDifferentFilesApart()
        {
            var inventory = AssetInventory.Build(
            [
                Reference(@"Clutter\Apple.nif", "Meshes"),
                Reference(@"Clutter\Pear.nif", "Meshes"),
            ]);

            Assert.Equal(2, inventory.Count);
            Assert.All(inventory.Entries, entry => Assert.Equal(1, entry.ReferenceCount));
        }

        [Fact]
        public void FindsAFileByItsNormalisedPath()
        {
            var inventory = AssetInventory.Build([Reference(@"Clutter\Apple.nif", "Meshes")]);

            Assert.NotNull(inventory.Find(new DataRelativePath("Meshes/Clutter/Apple.nif")));
            Assert.Null(inventory.Find(new DataRelativePath("Meshes/Clutter/Pear.nif")));
        }

        [Fact]
        public void CountsFilesByCategoryRatherThanReferences()
        {
            var inventory = AssetInventory.Build(
            [
                Reference(@"Clutter\Apple.nif", "Meshes"),
                Reference(@"Clutter\Apple.nif", "Meshes"),
                Reference(@"Clutter\Apple_d.dds", "Textures"),
            ]);

            var counts = inventory.CountByCategory().ToDictionary(pair => pair.Key, pair => pair.Value);

            Assert.Equal(1, counts[AssetCategory.Model]);
            Assert.Equal(1, counts[AssetCategory.Texture]);
        }

        [Fact]
        public void RemembersHowEachFileWasFound()
        {
            var listed = Reference(@"Clutter\Apple.nif", "Meshes");
            var inferred = listed with { Origin = AssetReferenceOrigin.Inferred };

            var entry = Assert.Single(AssetInventory.Build([listed, inferred]).Entries);

            Assert.Equal(
                [AssetReferenceOrigin.Listed, AssetReferenceOrigin.Inferred],
                entry.Origins.ToArray());
        }

        [Fact]
        public void HasNothingInItWhenTheSweepFoundNothing()
        {
            var inventory = AssetInventory.Build([]);

            Assert.Equal(0, inventory.Count);
            Assert.Empty(inventory.CountByCategory());
        }
    }
}
