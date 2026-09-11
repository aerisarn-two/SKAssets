using SKAssets.Assets;
using SKAssets.Plugins;
using SKAssets.References;
using Xunit;

namespace SKAssets.Tests
{
    /// <summary>
    /// The sweep against the plugins Bethesda shipped.
    /// </summary>
    /// <remarks>
    /// A plugin built in a test says what the code does with the records it was given.
    /// These say what the records are: a hundred and twenty thousand references across
    /// the masters, written by hand over a decade by people who spelled paths four
    /// different ways.
    ///
    /// <b>Nothing is copied out of the folder</b> -- the plugins are read in place.
    ///
    /// <b>It does not run unless asked.</b> Without <c>SKASSETS_SKYRIM_DATA</c> every
    /// test here returns, so an ordinary <c>dotnet test</c> and a checkout with no
    /// Skyrim on it both pass. The sweep takes about a minute:
    ///
    /// <code>
    /// SKASSETS_SKYRIM_DATA="/path/to/Skyrim Special Edition/Data" dotnet test \
    ///     --filter "FullyQualifiedName~MasterCorpus"
    /// </code>
    /// </remarks>
    [Trait("Category", "Corpus")]
    public class MasterCorpusTests
    {
        private static readonly string[] Masters =
        [
            "Skyrim.esm",
            "Update.esm",
            "Dawnguard.esm",
            "HearthFires.esm",
            "Dragonborn.esm",
        ];

        /// <summary>
        /// The Data folder to sweep, or null when nobody asked for one.
        /// </summary>
        /// <remarks>
        /// Named rather than searched for, so that having Skyrim installed is never on
        /// its own enough to add a minute to someone's build.
        /// </remarks>
        private static string? DataFolder()
        {
            string? configured = Environment.GetEnvironmentVariable("SKASSETS_SKYRIM_DATA");

            if (string.IsNullOrWhiteSpace(configured))
                return null;

            // Set but wrong is not the same as not set: somebody asked for this sweep
            // and did not get it, and passing quietly would tell them it had run.
            Assert.True(Directory.Exists(configured), $"SKASSETS_SKYRIM_DATA is not a folder: {configured}");

            string? missing = Masters.FirstOrDefault(master => !File.Exists(Path.Combine(configured, master)));

            Assert.True(missing is null, $"{missing} is not in {configured}");

            return configured;
        }

        private static List<AssetReference>? SweepTheMasters(PluginSweepOptions? options = null)
        {
            string? data = DataFolder();

            if (data is null)
                return null;

            var sweeper = new PluginAssetSweeper(options);

            return Masters.SelectMany(master => sweeper.Sweep(Path.Combine(data, master))).ToList();
        }

        /// <summary>
        /// The headline: the masters refer to some fifty thousand files, and the sweep
        /// gets through them without falling over.
        /// </summary>
        [Fact]
        public void SweepsEveryMaster()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            Assert.True(references.Count > 110_000, $"only {references.Count} references");

            var inventory = AssetInventory.Build(references);

            Assert.True(inventory.Count > 45_000, $"only {inventory.Count} distinct files");
        }

        /// <summary>
        /// Every extension the masters name is in the catalogue -- bar one. Dragonborn
        /// gives a static a model with no extension at all (<c>Thirsk</c>, an unfinished
        /// record), and a sweep that cannot say what that is has classified it correctly.
        /// </summary>
        [Fact]
        public void ClassifiesEverythingTheMastersName()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            var unclassified = references
                .Where(reference => !reference.MediaType.IsKnown)
                .Select(reference => reference.Path.Path)
                .Distinct()
                .ToList();

            Assert.Equal(["Meshes/DLC02/Architecture/Thirsk"], unclassified);
        }

        /// <summary>
        /// The fields no asset link covers: two holding water textures, and two the
        /// format stopped defining once something replaced them.
        /// </summary>
        [Fact]
        public void FindsThePathsNoAssetLinkCovers()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            var untyped = references
                .Where(reference => reference.Origin == AssetReferenceOrigin.Untyped)
                .ToList();

            Assert.Equal(352, untyped.Count);

            var byField = untyped
                .GroupBy(reference => $"{reference.RecordType}.{reference.Field}")
                .ToDictionary(group => group.Key, group => group.Count());

            Assert.Equal(161, byField["SoundMarker.FNAM"]);
            Assert.Equal(94, byField["Cell.WaterEnvironmentMap"]);
            Assert.Equal(93, byField["Water.UnusedNoisemaps"]);
            Assert.Equal(4, byField.Where(pair => pair.Key.StartsWith("Weather.")).Sum(pair => pair.Value));

            // The prefix the link fields never carry, gone by the time it is reported.
            Assert.Contains(untyped, reference =>
                reference.GivenPath.StartsWith(@"Data\", StringComparison.OrdinalIgnoreCase)
                && reference.Path.Path.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// What the untyped pass is worth: the files nothing else in the masters
        /// names. Without it these are simply missing from a sweep.
        /// </summary>
        [Fact]
        public void FindsFilesNoOtherFieldNames()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            var onlyUntyped = AssetInventory.Build(references).Entries
                .Where(entry => entry.Origins.All(origin => origin == AssetReferenceOrigin.Untyped))
                .ToList();

            Assert.Equal(34, onlyUntyped.Count);

            Assert.Equal(
                21,
                onlyUntyped.Count(entry => entry.MediaType.Category == AssetCategory.Sound));
        }

        /// <summary>
        /// A folder is not a file. Two in five legacy sound values name one, and the
        /// marker plays whatever is inside it.
        /// </summary>
        [Fact]
        public void ReportsNoFolders()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            Assert.DoesNotContain(references, reference =>
                reference.Path.Path.EndsWith('/') || reference.GivenPath.EndsWith('\\'));
        }

        /// <summary>
        /// Every Papyrus reference in the game is inferred from a script name; not one
        /// record spells a script path out.
        /// </summary>
        [Fact]
        public void FindsScriptsOnlyByInference()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            var scripts = references
                .Where(reference => reference.MediaType.Category == AssetCategory.Script)
                .ToList();

            Assert.NotEmpty(scripts);
            Assert.All(scripts, script => Assert.Equal(AssetReferenceOrigin.Inferred, script.Origin));
        }

        /// <summary>
        /// Four spellings of one sound, normalised to one path. This is what the sweep
        /// is for: the records disagree, and the file does not care.
        /// </summary>
        [Fact]
        public void NormalisesTheWaysARecordCanNameOneFile()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            var entry = AssetInventory.Build(references)
                .Find(new Mutagen.Bethesda.Assets.DataRelativePath("Sound/FX/XXX_Placeholder_Silence.wav"));

            Assert.NotNull(entry);
            Assert.True(entry.Spellings.Count > 1, "expected the masters to disagree about this path");
        }

        /// <summary>
        /// A script on a dialogue response belongs to the response, not to the topic
        /// above it. Before that was true, every INFO in Skyrim.esm was counted twice.
        /// </summary>
        [Fact]
        public void AttributesAReferenceToTheRecordThatHoldsIt()
        {
            var references = SweepTheMasters();

            if (references is null)
                return;

            Assert.DoesNotContain(references, reference => reference.RecordType == "DialogTopic");
            Assert.Contains(references, reference => reference.RecordType == "DialogResponses");

            // A cell reports the scripts of everything placed in it. What is left of
            // its own are the water textures, which are not links at all.
            Assert.All(
                references.Where(reference => reference.RecordType == "Cell"),
                reference => Assert.Equal(AssetReferenceOrigin.Untyped, reference.Origin));
        }

        /// <summary>
        /// Turning inference off costs the scripts and nothing else. Worth knowing,
        /// since it is most of the work a sweep does.
        /// </summary>
        [Fact]
        public void ListedReferencesAreTheSameWithoutInference()
        {
            var all = SweepTheMasters();

            if (all is null)
                return;

            var listedOnly = SweepTheMasters(new PluginSweepOptions { IncludeInferred = false })!;

            Assert.Equal(
                all.Count(reference => reference.Origin != AssetReferenceOrigin.Inferred),
                listedOnly.Count);
        }
    }
}
