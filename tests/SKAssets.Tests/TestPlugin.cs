using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SKAssets.Tests
{
    /// <summary>
    /// A plugin built in memory, holding one of each kind of reference.
    /// </summary>
    /// <remarks>
    /// The sweep reads records, not files, so a mod assembled here exercises it as
    /// well as one off disk does -- and it can be built on a machine with no Skyrim
    /// on it, which the corpus tests cannot. What it cannot prove is what the vanilla
    /// masters actually contain; that is <see cref="MasterCorpusTests"/>.
    /// </remarks>
    internal static class TestPlugin
    {
        internal const string ModelPath = @"Clutter\Apple.nif";
        internal const string DiffusePath = @"Clutter\Apple_d.dds";
        internal const string NormalPath = @"Clutter\Apple_n.dds";
        internal const string ScriptName = "AppleScript";
        internal const string CubemapPath = @"Data\Textures\Cubemaps\TestCave_e.dds";
        internal const string NoisemapPath = @"Data\Textures\Water\TestWater.dds";
        internal const string SkeletonPath = @"Actors\Test\Character Assets\skeleton.hkx";
        internal const string ResponseScriptName = "TIF__00001234";
        internal const string LegacySoundPath = @"fx\mag\test\mag_test_cast_lp.wav";
        internal const string LegacySoundFolder = @"fx\phy\bottle\l\";
        internal const string LegacyCloudPath = @"Sky\TestCloudUpper01.dds";
        internal const string PlacedScriptName = "PlacedObjectScript";

        internal static ISkyrimModGetter Build()
        {
            var mod = new SkyrimMod(ModKey.FromName("SKAssetsTest", ModType.Plugin), SkyrimRelease.SkyrimSE);

            var statue = mod.Statics.AddNew("TestStatic");
            statue.Model = new Model { File = ModelPath };

            // A record with the field and nothing in it. Two thousand of these are in
            // the masters, and none of them is a reference.
            var empty = mod.Statics.AddNew("EmptyStatic");
            empty.Model = new Model { File = "" };

            var textures = mod.TextureSets.AddNew("TestTextureSet");
            textures.Diffuse = DiffusePath;
            textures.NormalOrGloss = NormalPath;

            // Scripts are never spelled out as paths; the name is all there is.
            var weapon = mod.Weapons.AddNew("TestWeapon");
            weapon.VirtualMachineAdapter = new VirtualMachineAdapter();
            weapon.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = ScriptName });

            // A behaviour graph in a field the format calls a model.
            var race = mod.Races.AddNew("TestRace");
            race.BehaviorGraph = new GenderedItem<Model?>(
                new Model { File = SkeletonPath },
                new Model { File = "" });

            // Named twice, to prove the inventory counts one file and two references.
            var water = mod.Waters.AddNew("TestWater");
            water.UnusedNoisemaps.Add(NoisemapPath);
            water.UnusedNoisemaps.Add(NoisemapPath);

            var cell = new Cell(mod, "TestCell")
            {
                WaterEnvironmentMap = CubemapPath,
            };

            // Legacy fields, held as raw bytes because the format left them
            // undefined once something else replaced them. One sound marker names a
            // file, one names a folder to pick from -- only the first is an asset.
            var sound = mod.SoundMarkers.AddNew("TestSoundMarker");
            sound.FNAM = ZString(LegacySoundPath);

            var folderSound = mod.SoundMarkers.AddNew("TestFolderSoundMarker");
            folderSound.FNAM = ZString(LegacySoundFolder);

            var weather = mod.Weathers.AddNew("TestWeather");
            weather.DNAM = ZString(LegacyCloudPath);

            // A script on a dialogue response. Asking the topic for its asset links
            // hands this back as well, which is why the sweep subtracts what a
            // container's children report.
            var response = new DialogResponses(mod)
            {
                EditorID = "TestResponse",
                VirtualMachineAdapter = new DialogResponsesAdapter(),
            };
            response.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = ResponseScriptName });

            var topic = mod.DialogTopics.AddNew("TestTopic");
            topic.Responses.Add(response);

            // The same, one level down: a cell reports the scripts of what is placed
            // in it.
            var placed = new PlacedObject(mod)
            {
                EditorID = "TestPlacedObject",
                VirtualMachineAdapter = new VirtualMachineAdapter(),
            };
            placed.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = PlacedScriptName });

            cell.Temporary.Add(placed);

            var subBlock = new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock };
            subBlock.Cells.Add(cell);

            var block = new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock };
            block.SubBlocks.Add(subBlock);

            mod.Cells.Records.Add(block);

            return mod;
        }

        /// <summary>
        /// A null-terminated single-byte string, as the format stores one.
        /// </summary>
        private static MemorySlice<byte> ZString(string value) =>
            new(Encoding.Latin1.GetBytes(value + '\0'));
    }
}
