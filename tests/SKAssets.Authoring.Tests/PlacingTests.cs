using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>Getting an imported asset into the game: placed in a cell, dropped as loot, sold.</summary>
    public sealed class PlacingTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-placing-" + Guid.NewGuid().ToString("N"));
        private readonly ISkyrimModGetter _master = Masters.Build();

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        private (PluginAuthoring Authoring, FormKey Sword) Imported()
        {
            var authoring = new PluginAuthoring([_master], "MyMod.esp", _out, new StubImporter());
            ImportResult sword = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "MySword",
                Fbx = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = "sword.fbx" }, MeshFolder = "MyMod", Recipes = false,
            });
            return (authoring, sword.Record.FormKey);
        }

        /// <summary>An interior cell is overridden to hold the reference, placed and turned as asked.</summary>
        [Fact]
        public void AReferenceIsPlacedInAnInteriorCell()
        {
            var (authoring, sword) = Imported();
            using var _ = authoring;

            AuthoredRecord placed = authoring.Place(sword, "WhiterunBanneredMare", new Placement(new P3Float(10, 20, 30), new P3Float(0, 0, 90), 1.5f));

            ICellGetter cell = authoring.Plugin.EnumerateMajorRecords<ICellGetter>().Single();
            Assert.Equal("WhiterunBanneredMare", cell.EditorID);
            Assert.Equal(_master.ModKey, cell.FormKey.ModKey);

            var reference = Assert.IsAssignableFrom<IPlacedObjectGetter>(Assert.Single(cell.Temporary));
            Assert.Equal(placed.FormKey, reference.FormKey);
            Assert.Equal(sword, reference.Base.FormKey);
            Assert.Equal(new P3Float(10, 20, 30), reference.Placement!.Position);
            Assert.Equal(MathF.PI / 2, reference.Placement.Rotation.Z, 5);
            Assert.Equal(1.5f, reference.Scale);
        }

        /// <summary>An exterior cell is found by the position, in the worldspace named.</summary>
        [Fact]
        public void AReferenceIsPlacedInTheExteriorCellHoldingIt()
        {
            var (authoring, sword) = Imported();
            using var _ = authoring;

            authoring.PlaceInWorldspace(sword, "Tamriel", new Placement(new P3Float(PluginAuthoring.CellSize + 100, 2 * PluginAuthoring.CellSize + 5, 0)));

            ICellGetter cell = authoring.Plugin.EnumerateMajorRecords<ICellGetter>().Single();
            Assert.Equal(new P2Int(1, 2), cell.Grid!.Point);
            Assert.Single(authoring.Plugin.Worldspaces);

            Assert.Throws<ArgumentException>(() => authoring.PlaceInWorldspace(sword, "Tamriel", new Placement(new P3Float(-50000, 0, 0))));
        }

        /// <summary>A leveled list and a container are overridden with the item added.</summary>
        [Fact]
        public void AnItemIsAddedToLootAndToAMerchant()
        {
            var (authoring, sword) = Imported();
            using var _ = authoring;

            authoring.AddToLeveledList(sword, "LItemWeaponSword", level: 5);
            authoring.AddToContainer(sword, "MerchantWhiterunBlacksmithChest", count: 2);

            var entry = Assert.Single(authoring.Plugin.LeveledItems.Single().Entries!);
            Assert.Equal((sword, (short)5), (entry.Data!.Reference.FormKey, entry.Data.Level));
            var item = Assert.Single(authoring.Plugin.Containers.Single().Items!);
            Assert.Equal((sword, 2), (item.Item.Item.FormKey, item.Item.Count));
        }

        /// <summary>A plugin holding placed references and overridden cells writes and reads back.</summary>
        [Fact]
        public void APluginWithPlacedReferencesIsWritten()
        {
            var (authoring, sword) = Imported();
            using (authoring)
            {
                authoring.Place(sword, "WhiterunBanneredMare", new Placement(new P3Float(1, 2, 3)));
                authoring.PlaceInWorldspace(sword, "Tamriel", new Placement(new P3Float(PluginAuthoring.CellSize + 1, 2 * PluginAuthoring.CellSize + 1, 0)));
                authoring.Save();
            }

            using var written = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            var placed = written.EnumerateMajorRecords<IPlacedObjectGetter>().ToList();
            Assert.Equal(2, placed.Count);
            Assert.All(placed, p => Assert.Equal(sword, p.Base.FormKey));
        }

        [Fact]
        public void ACellOrListTheLoadOrderLacksIsRefused()
        {
            var (authoring, sword) = Imported();
            using var _ = authoring;

            Assert.Throws<ArgumentException>(() => authoring.Place(sword, "NoSuchCell", new Placement(default)));
            Assert.Throws<ArgumentException>(() => authoring.AddToLeveledList(sword, "NoSuchList"));
        }
    }
}
