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

                Assert.DoesNotContain(weapon.Findings.Concat(armour.Findings), f => f.Finding.Severity != FindingSeverity.Note);
                Assert.Equal(["RecipeWeaponMyMod_Sword", "TemperWeaponMyMod_Sword"],
                    weapon.Records.Where(r => r.Type == nameof(ConstructibleObject)).Select(r => r.EditorId).Order());

                foreach (string mesh in weapon.Meshes.Concat(armour.Meshes))
                    Assert.NotNull(NifModel.Load(Path.Combine(_out, mesh), schema));

                authoring.Save();
            }

            using var written = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            Assert.Contains(written.ModHeader.MasterReferences, m => m.Master.FileName == "Skyrim.esm");
            Assert.Equal("MyMod_Sword", Assert.Single(written.Weapons).EditorID);
            Assert.Equal("MyMod_CuirassAA", Assert.Single(written.ArmorAddons).EditorID);
        }

        /// <summary>A mesh out of the game's archives, converted to FBX in the output folder.</summary>
        private string Fbx(string data, NifXmlDatabase schema, string archived)
        {
            foreach (string archive in Directory.GetFiles(data, "*.bsa"))
                foreach (var entry in Archive.CreateReader(GameRelease.SkyrimSE, archive).Files)
                {
                    if (!string.Equals(entry.Path.Replace('\\', '/'), archived.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;

                    using var stream = new MemoryStream(entry.GetBytes());
                    string fbx = Path.Combine(_out, "source", Path.GetFileNameWithoutExtension(archived) + ".fbx");
                    Directory.CreateDirectory(Path.GetDirectoryName(fbx)!);
                    new NifToFbx(NifModel.Load(stream, schema)).Convert().Save(fbx);
                    return fbx;
                }

            throw new FileNotFoundException($"no archive in {data} holds {archived}");
        }
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
