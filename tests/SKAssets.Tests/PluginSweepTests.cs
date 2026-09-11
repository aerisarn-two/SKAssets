using SKAssets.Assets;
using SKAssets.Plugins;
using SKAssets.References;
using Xunit;

namespace SKAssets.Tests
{
    /// <summary>
    /// The sweep itself, over a plugin built for the purpose.
    /// </summary>
    public class PluginSweepTests
    {
        private static List<AssetReference> Sweep(PluginSweepOptions? options = null) =>
            new PluginAssetSweeper(options).Sweep(TestPlugin.Build()).ToList();

        private static AssetReference Single(IEnumerable<AssetReference> references, string path) =>
            Assert.Single(references, reference => reference.Path.Path == path);

        [Fact]
        public void FindsAModelAndNormalisesItUnderMeshes()
        {
            var model = Single(Sweep(), "Meshes/Clutter/Apple.nif");

            Assert.Equal(TestPlugin.ModelPath, model.GivenPath);
            Assert.Equal(AssetReferenceOrigin.Listed, model.Origin);
            Assert.Equal(AssetCategory.Model, model.MediaType.Category);
            Assert.Equal("application/vnd.bethesda.nif", model.MediaType.Mime);
            Assert.Equal("Static", model.RecordType);
            Assert.Equal("TestStatic", model.EditorId);
            Assert.Equal("Model", model.DeclaredType);
        }

        [Fact]
        public void FindsBothTexturesOfATextureSet()
        {
            var references = Sweep();

            Assert.Equal(AssetCategory.Texture, Single(references, "Textures/Clutter/Apple_d.dds").MediaType.Category);
            Assert.Equal("Texture", Single(references, "Textures/Clutter/Apple_n.dds").DeclaredType);
        }

        /// <summary>
        /// A field that exists and holds nothing refers to nothing. The masters have
        /// some two thousand of these.
        /// </summary>
        [Fact]
        public void IgnoresAnEmptyField()
        {
            Assert.DoesNotContain(Sweep(), reference => reference.EditorId == "EmptyStatic");
        }

        /// <summary>
        /// The reason inferred links are on by default: a script is named, never
        /// pathed, so without them a sweep of Skyrim.esm reports no scripts at all.
        /// </summary>
        [Fact]
        public void DerivesTheScriptFileFromTheScriptName()
        {
            var references = Sweep();

            var compiled = Single(references, $"Scripts/{TestPlugin.ScriptName}.pex");

            Assert.Equal(AssetReferenceOrigin.Inferred, compiled.Origin);
            Assert.Equal(AssetCategory.Script, compiled.MediaType.Category);
            Assert.Equal("application/vnd.bethesda.papyrus", compiled.MediaType.Mime);

            // And the source beside it, which the game does not ship.
            Assert.Equal(
                "text/vnd.bethesda.papyrus",
                Single(references, $"Scripts/Source/{TestPlugin.ScriptName}.psc").MediaType.Mime);
        }

        [Fact]
        public void LeavesInferredPathsOutWhenAskedTo()
        {
            var references = Sweep(new PluginSweepOptions { IncludeInferred = false });

            Assert.DoesNotContain(references, reference => reference.Origin == AssetReferenceOrigin.Inferred);
            Assert.Contains(references, reference => reference.Origin == AssetReferenceOrigin.Listed);
        }

        /// <summary>
        /// What the field is for and what the file is are two different questions. A
        /// race keeps its behaviour graph in a model field, and the answers differ.
        /// </summary>
        [Fact]
        public void SeparatesWhatTheFieldSaysFromWhatTheFileIs()
        {
            var behaviour = Single(Sweep(), "Meshes/Actors/Test/Character Assets/skeleton.hkx");

            Assert.Equal("Model", behaviour.DeclaredType);
            Assert.Equal(AssetCategory.Animation, behaviour.MediaType.Category);
            Assert.Equal("application/vnd.havok.hkx", behaviour.MediaType.Mime);
        }

        /// <summary>
        /// The cell water cubemap: a texture in a field typed as a string, spelled
        /// with the <c>Data\</c> prefix that no asset link ever carries.
        /// </summary>
        [Fact]
        public void FindsAPathInAFieldThatIsNotAnAssetLink()
        {
            var cubemap = Single(Sweep(), "Textures/Cubemaps/TestCave_e.dds");

            Assert.Equal(AssetReferenceOrigin.Untyped, cubemap.Origin);
            Assert.Equal("Cell", cubemap.RecordType);
            Assert.Equal("WaterEnvironmentMap", cubemap.Field);
            Assert.Equal(TestPlugin.CubemapPath, cubemap.GivenPath);
            Assert.Equal(AssetCategory.Texture, cubemap.MediaType.Category);
        }

        /// <summary>
        /// A path in a subrecord the format stopped defining. Mutagen hands these
        /// back as raw bytes, so nothing that reads properties can see them -- and
        /// the masters still carry 161 sound filenames and a set of cloud layers in
        /// exactly this form.
        /// </summary>
        [Theory]
        [InlineData("Sound/fx/mag/test/mag_test_cast_lp.wav", "SoundMarker", "FNAM", AssetCategory.Sound)]
        [InlineData("Textures/Sky/TestCloudUpper01.dds", "Weather", "DNAM", AssetCategory.Texture)]
        public void ReadsAPathOutOfALegacySubrecord(string path, string recordType, string field, AssetCategory category)
        {
            var reference = Single(Sweep(), path);

            Assert.Equal(AssetReferenceOrigin.Untyped, reference.Origin);
            Assert.Equal(recordType, reference.RecordType);
            Assert.Equal(field, reference.Field);
            Assert.Equal(category, reference.MediaType.Category);
        }

        /// <summary>
        /// Two in five legacy sound filenames are a folder: the marker plays whatever
        /// is in it, so no one file is being referred to.
        /// </summary>
        [Fact]
        public void DoesNotReportAFolderAsAFile()
        {
            Assert.DoesNotContain(
                Sweep(),
                reference => reference.EditorId == "TestFolderSoundMarker");
        }

        [Fact]
        public void LeavesUntypedFieldsOutWhenAskedTo()
        {
            Assert.DoesNotContain(
                Sweep(new PluginSweepOptions { IncludeUntyped = false }),
                reference => reference.Origin == AssetReferenceOrigin.Untyped);
        }

        [Fact]
        public void ReportsEachReferenceSeparatelyEvenToTheSameFile()
        {
            var noisemaps = Sweep()
                .Where(reference => reference.Path.Path == "Textures/Water/TestWater.dds")
                .ToList();

            Assert.Equal(2, noisemaps.Count);
            Assert.All(noisemaps, reference => Assert.Equal("UnusedNoisemaps", reference.Field));
        }

        [Fact]
        public void NamesThePluginEveryReferenceCameFrom()
        {
            Assert.All(Sweep(), reference => Assert.Equal("SKAssetsTest.esp", reference.Source.FileName.String));
        }

        /// <summary>
        /// Nothing in the sweep should arrive unclassified: every extension a plugin
        /// can name is in the catalogue.
        /// </summary>
        [Fact]
        public void ClassifiesEverythingItFinds()
        {
            Assert.All(Sweep(), reference => Assert.True(reference.MediaType.IsKnown, reference.Path.Path));
        }

        /// <summary>
        /// A record's links include the records inside it -- a topic hands back its
        /// responses' scripts, a cell its placed objects'. Since the sweep visits
        /// those too, counting the container as well would report each one twice.
        /// </summary>
        [Theory]
        [InlineData(TestPlugin.ResponseScriptName, "DialogResponses", "TestResponse")]
        [InlineData(TestPlugin.PlacedScriptName, "PlacedObject", "TestPlacedObject")]
        public void AttributesAReferenceToTheRecordThatHoldsIt(string script, string recordType, string editorId)
        {
            var compiled = Single(Sweep(), $"Scripts/{script}.pex");

            Assert.Equal(recordType, compiled.RecordType);
            Assert.Equal(editorId, compiled.EditorId);
        }

        /// <summary>
        /// What a container holds in its own right is still its own: the cell's water
        /// cubemap survives the subtraction that drops its placed objects' scripts.
        /// </summary>
        [Fact]
        public void KeepsWhatTheContainerItselfRefersTo()
        {
            var references = Sweep();

            Assert.Equal("Cell", Single(references, "Textures/Cubemaps/TestCave_e.dds").RecordType);
            Assert.DoesNotContain(references, reference => reference.RecordType == "DialogTopic");
        }

        /// <summary>
        /// The deep scan is a superset of the ordinary sweep, not a different one: it
        /// adds fields nobody catalogued, and repeats nothing already found.
        /// </summary>
        [Fact]
        public void DeepScanAddsWithoutRepeating()
        {
            var ordinary = Sweep();
            var deep = Sweep(new PluginSweepOptions { DeepScan = true });

            Assert.Superset(
                ordinary.Select(reference => reference.Path.Path).ToHashSet(),
                deep.Select(reference => reference.Path.Path).ToHashSet());

            foreach (var group in deep.GroupBy(reference => (reference.Record, reference.Path.Path)))
                Assert.True(
                    group.Count(reference => reference.Origin == AssetReferenceOrigin.Scanned) <= 1,
                    $"{group.Key} was scanned more than once");
        }
    }
}
