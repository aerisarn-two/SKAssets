using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// The whole way against the game: a vanilla mesh taken to FBX, imported back through NIFBX,
    /// and written as a new record copied from the one that names the original.
    /// </summary>
    public sealed class AuthoringCorpusTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-authoring-corpus-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        /// <summary>
        /// The iron sword and the iron cuirass, re-imported: their meshes convert and load, the mesh
        /// rules find nothing wrong with them for the records that name them, and the plugin is
        /// written against the masters with the sword's two recipes copied.
        /// </summary>
        [MastersFact]
        public void AVanillaMeshComesBackAsANewRecord()
        {
            string data = Game.Data!;
            NifXmlDatabase schema = NifXmlDatabase.LoadEmbedded();

            string sword = Fbx(data, schema, @"meshes\weapons\iron\longsword.nif");

            // The sword's diffuse beside its FBX, as an author's own texture would be; its normal
            // map is not, and stays the game's.
            string beside = Path.Combine(Path.GetDirectoryName(sword)!, "textures", "ironlongsword.dds");
            Directory.CreateDirectory(Path.GetDirectoryName(beside)!);
            File.WriteAllBytes(beside, Archived(data, @"textures\weapons\iron\ironlongsword.dds"));
            string cuirass = Fbx(data, schema, @"meshes\armor\iron\male\cuirasslight_1.nif");

            using (var authoring = PluginAuthoring.Open(data, "MyMod.esp", _out))
            {
                authoring.Prefix = "MyMod_";

                ImportResult weapon = authoring.Import(new AssetImport
                {
                    Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "Sword", Name = "My Sword",
                    Fbx = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = sword }, MeshFolder = @"MyMod\Weapons",
                });
                ImportResult armour = authoring.Import(new AssetImport
                {
                    Kind = AuthoredKind.Armor, Template = "ArmorIronCuirass", EditorId = "Cuirass",
                    Fbx = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = cuirass }, MeshFolder = @"MyMod\Armor",
                });

                Assert.Equal(["Textures/MyMod/Weapons/MyMod_ironlongsword.dds"], weapon.Textures);
                Assert.True(File.Exists(Path.Combine(_out, "Textures", "MyMod", "Weapons", "MyMod_ironlongsword.dds")));

                var named = TexturesOf(NifModel.Load(Path.Combine(_out, weapon.Meshes[0]), schema));
                Assert.Contains(@"Textures\MyMod\Weapons\MyMod_ironlongsword.dds", named);
                Assert.Contains(named, t => t.Equals(@"textures\weapons\iron\ironlongsword_n.dds", StringComparison.OrdinalIgnoreCase));

                Assert.DoesNotContain(weapon.Findings.Concat(armour.Findings), f => f.Finding.Severity != FindingSeverity.Note);
                Assert.Equal(["RecipeWeaponMyMod_Sword", "TemperWeaponMyMod_Sword"],
                    weapon.Records.Where(r => r.Type == nameof(ConstructibleObject)).Select(r => r.EditorId).Order());

                foreach (string mesh in weapon.Meshes.Concat(armour.Meshes))
                    Assert.NotNull(NifModel.Load(Path.Combine(_out, mesh), schema));

                // Into the world: on the Bannered Mare's floor, and outside on Tamriel's grid.
                authoring.Place(weapon.Record.FormKey, "WhiterunBanneredMare", new Placement(new Noggog.P3Float(0, 0, 0)));
                authoring.PlaceInWorldspace(weapon.Record.FormKey, "Tamriel", new Placement(new Noggog.P3Float(20000, -10000, 0)));
                authoring.AddToLeveledList(weapon.Record.FormKey, "LItemWeaponSword");

                authoring.Save();
            }

            using var written = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            Assert.Contains(written.ModHeader.MasterReferences, m => m.Master.FileName == "Skyrim.esm");
            Assert.Equal("MyMod_Sword", Assert.Single(written.Weapons).EditorID);
            Assert.Equal("MyMod_CuirassAA", Assert.Single(written.ArmorAddons).EditorID);
            Assert.Equal(2, written.EnumerateMajorRecords<IPlacedObjectGetter>().Count());
            Assert.Equal("Tamriel", Assert.Single(written.Worldspaces).EditorID);
        }

        /// <summary>A mesh out of the game's archives, converted to FBX in a folder of its own.</summary>
        private string Fbx(string data, NifXmlDatabase schema, string archived)
        {
            using var stream = new MemoryStream(Archived(data, archived));
            string fbx = Path.Combine(_out, "source", Path.GetFileNameWithoutExtension(archived), Path.GetFileNameWithoutExtension(archived) + ".fbx");
            Directory.CreateDirectory(Path.GetDirectoryName(fbx)!);
            new NifToFbx(NifModel.Load(stream, schema)).Convert().Save(fbx);
            return fbx;
        }

        /// <summary>A file out of the game's archives. Their paths are separated by '/' here, and records' by '\\'.</summary>
        private static byte[] Archived(string data, string archived)
        {
            foreach (string archive in Directory.GetFiles(data, "*.bsa"))
                foreach (var entry in Archive.CreateReader(GameRelease.SkyrimSE, archive).Files)
                    if (string.Equals(entry.Path.Replace('\\', '/'), archived.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        return entry.GetBytes();

            throw new FileNotFoundException($"no archive in {data} holds {archived}");
        }

        /// <summary>Every texture path a mesh's texture sets name.</summary>
        private static List<string> TexturesOf(NifModel model) =>
            [.. model.Blocks.Where(b => b.Name == "BSShaderTextureSet")
                .SelectMany(b => b.Children.First(c => c.Name == "Textures").Children)
                .Select(t => t.Value.Get<string>()).OfType<string>().Where(t => t.Length > 0)];
    }

    /// <summary>The game's Data folder, when one is to hand.</summary>
    internal static class Game
    {
        public const string DataVar = "SKASSETS_SKYRIM_DATA";

        public static string? Data
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(DataVar);
                if (string.IsNullOrWhiteSpace(configured)) return null;

                Assert.True(Directory.Exists(configured), $"{DataVar} is not a folder: {configured}");
                return configured;
            }
        }
    }

    /// <summary>A fact about the shipped game, which skips rather than passes without it.</summary>
    public sealed class MastersFactAttribute : FactAttribute
    {
        public MastersFactAttribute()
        {
            if (Game.Data is null) Skip = $"set {Game.DataVar} to the game's Data folder to run this";
        }
    }
}
