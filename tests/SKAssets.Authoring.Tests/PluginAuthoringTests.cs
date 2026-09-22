using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Content.Nif;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    public sealed class PluginAuthoringTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-authoring-" + Guid.NewGuid().ToString("N"));
        private readonly ISkyrimModGetter _master = Masters.Build();
        private readonly StubImporter _meshes = new();

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        private PluginAuthoring Authoring(string prefix = "") =>
            new([_master], "MyMod.esp", _out, _meshes) { Prefix = prefix };

        private static Dictionary<ModelSlot, string> Fbx(params (ModelSlot, string)[] slots) => slots.ToDictionary(s => s.Item1, s => s.Item2);

        private T Master<T>(string editorId) where T : class, IMajorRecordGetter =>
            _master.EnumerateMajorRecords<T>().Single(r => r.EditorID == editorId);

        /// <summary>
        /// A weapon is a copy of the template with the new mesh, a first-person static of its own,
        /// and the template's crafting and tempering recipes made to create it instead.
        /// </summary>
        [Fact]
        public void AWeaponIsTheTemplateWithTheNewMeshItsOwnStaticAndItsRecipes()
        {
            using var authoring = Authoring();
            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "MySword", Name = "My Sword",
                Fbx = Fbx((ModelSlot.Main, "sword.fbx")), MeshFolder = @"MyMod\Weapons",
            });

            var sword = authoring.Plugin.Weapons.Single();
            Assert.Equal("MySword", sword.EditorID);
            Assert.Equal("My Sword", sword.Name?.String);
            Assert.Equal(@"MyMod\Weapons\MySword.nif", sword.Model!.File.GivenPath);
            Assert.Equal(Master<IWeaponGetter>("IronSword").Keywords, sword.Keywords);

            var shown = authoring.Plugin.Statics.Single();
            Assert.Equal(shown.FormKey, sword.FirstPersonModel.FormKey);
            Assert.Equal(@"MyMod\Weapons\MySword.nif", shown.Model!.File.GivenPath);

            Assert.Equal(2, authoring.Plugin.ConstructibleObjects.Count);
            Assert.All(authoring.Plugin.ConstructibleObjects, r => Assert.Equal(sword.FormKey, r.CreatedObject.FormKey));
            Assert.Equal(["RecipeWeaponMySword", "TemperWeaponMySword"], authoring.Plugin.ConstructibleObjects.Select(r => r.EditorID).Order());

            Assert.Equal(sword.FormKey, result.Record.FormKey);
            Assert.Equal(4, result.Records.Count);
            Assert.Equal(["Meshes/MyMod/Weapons/MySword.nif"], result.Meshes);
            Assert.Single(_meshes.Calls);
            Assert.Contains(result.Notes, n => n.Contains("first-person static uses the main mesh"));
        }

        /// <summary>The template is copied, never overridden: the master's records are untouched and none is in the plugin.</summary>
        [Fact]
        public void TheMastersRecordsAreNotOverridden()
        {
            using var authoring = Authoring();
            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "MySword",
                Fbx = Fbx((ModelSlot.Main, "sword.fbx")), MeshFolder = "MyMod",
            });

            Assert.All(authoring.Plugin.EnumerateMajorRecords(), r => Assert.Equal(authoring.Plugin.ModKey, r.FormKey.ModKey));
            Assert.Equal(@"Weapons\Iron\LongSword.nif", Master<IWeaponGetter>("IronSword").Model!.File.GivenPath);
        }

        /// <summary>An enchanted variant inherits its mesh from its base, so the base is what is copied.</summary>
        [Fact]
        public void AVariantIsCopiedFromItsBase()
        {
            using var authoring = Authoring();
            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "EnchIronSwordFire01", EditorId = "MySword",
                Fbx = Fbx((ModelSlot.Main, "sword.fbx")), MeshFolder = "MyMod", Recipes = false,
            });

            Assert.Equal(Master<IWeaponGetter>("IronSword").FormKey, result.Record.CopiedFrom);
            Assert.True(authoring.Plugin.Weapons.Single().Template.IsNull);
            Assert.Contains(result.Notes, n => n.Contains("is a variant"));
        }

        /// <summary>
        /// An armour owns its addon: the addon is copied and given the body mesh in both weights,
        /// and the armour wears the copy.
        /// </summary>
        [Fact]
        public void AnArmourWearsACopyOfItsAddonInBothWeights()
        {
            using var authoring = Authoring();
            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronCuirass", EditorId = "MyCuirass",
                Fbx = Fbx((ModelSlot.Main, "heavy.fbx"), (ModelSlot.LightWeight, "light.fbx")), MeshFolder = @"MyMod\Armor",
            });

            var armour = authoring.Plugin.Armors.Single();
            var addon = authoring.Plugin.ArmorAddons.Single();
            Assert.Equal(addon.FormKey, Assert.Single(armour.Armature).FormKey);
            Assert.Equal(Master<IArmorAddonGetter>("IronCuirassAA").Race.FormKey, addon.Race.FormKey);
            Assert.Equal(@"MyMod\Armor\MyCuirass_1.nif", addon.WorldModel!.Male!.File.GivenPath);
            Assert.Equal(@"MyMod\Armor\MyCuirass_1.nif", addon.WorldModel.Female!.File.GivenPath);

            Assert.Equal(["Meshes/MyMod/Armor/MyCuirass_0.nif", "Meshes/MyMod/Armor/MyCuirass_1.nif"], result.Meshes.Order());
            Assert.Contains(_meshes.Calls, c => c.Fbx == "light.fbx" && c.Nif.EndsWith("MyCuirass_0.nif"));
            Assert.Contains(result.Notes, n => n.Contains("female body wears the male mesh"));
            Assert.Contains(result.Notes, n => n.Contains("lies on the ground as the template does"));
        }

        /// <summary>The prefix goes on every editor id and mesh an import writes; the plugin's is the default.</summary>
        [Fact]
        public void ThePrefixNamesEverythingAnImportWrites()
        {
            using var authoring = Authoring(prefix: "MyMod_");
            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "Sword",
                Fbx = Fbx((ModelSlot.Main, "sword.fbx")), MeshFolder = "MyMod",
            });

            Assert.All(result.Records, r => Assert.Contains("MyMod_Sword", r.EditorId));
            Assert.Equal(["Meshes/MyMod/MyMod_Sword.nif"], result.Meshes);

            ImportResult own = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Static, Template = "RockCliff01", EditorId = "Rock", Prefix = "XY",
                Fbx = Fbx((ModelSlot.Main, "rock.fbx")), MeshFolder = "MyMod",
            });
            Assert.Equal("XYRock", own.Record.EditorId);
        }

        /// <summary>Ammunition shares its projectile until it is given a mesh to fly as; then it owns a copy.</summary>
        [Fact]
        public void AmmunitionCopiesItsProjectileOnlyForAMeshOfItsOwn()
        {
            using var authoring = Authoring();
            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Ammunition, Template = "IronArrow", EditorId = "MyArrow",
                Fbx = Fbx((ModelSlot.Main, "arrow.fbx")), MeshFolder = "MyMod",
            });
            Assert.Equal(Master<IProjectileGetter>("ArrowIronProjectile").FormKey, authoring.Plugin.Ammunitions.Single().Projectile.FormKey);

            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Ammunition, Template = "IronArrow", EditorId = "MyFlyingArrow",
                Fbx = Fbx((ModelSlot.Main, "arrow.fbx"), (ModelSlot.Projectile, "flying.fbx")), MeshFolder = "MyMod",
            });
            var flies = authoring.Plugin.Projectiles.Single();
            Assert.Equal(flies.FormKey, authoring.Plugin.Ammunitions.Single(a => a.EditorID == "MyFlyingArrow").Projectile.FormKey);
            Assert.Equal(@"MyMod\MyFlyingArrow_projectile.nif", flies.Model!.File.GivenPath);
        }

        /// <summary>A book keeps the shared inventory art unless it is given its own.</summary>
        [Fact]
        public void ABookKeepsItsSharedInventoryArt()
        {
            using var authoring = Authoring();
            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Book, Template = "BookGeneric", EditorId = "MyBook",
                Fbx = Fbx((ModelSlot.Main, "book.fbx")), MeshFolder = "MyMod",
            });

            Assert.Equal(Master<IStaticGetter>("BookInventoryArt").FormKey, authoring.Plugin.Books.Single().InventoryArt.FormKey);
            Assert.Empty(authoring.Plugin.Statics);
        }

        /// <summary>Every mesh is checked against the record that names it.</summary>
        [Fact]
        public void AMeshIsCheckedAgainstTheRecordThatNamesIt()
        {
            var inside = new StubImporter(new NifProfile { RootType = "NiNode", Shapes = 1, IsSkinned = true, HasExternalSkeleton = false });
            using var authoring = new PluginAuthoring([_master], "MyMod.esp", _out, inside);

            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronCuirass", EditorId = "MyCuirass",
                Fbx = Fbx((ModelSlot.Main, "cuirass.fbx")), MeshFolder = "MyMod",
            });

            Assert.Contains(result.Findings, f => f.Finding.Rule == "armor-external-skeleton");
        }

        /// <summary>The plugin is written against its masters and reads back with what was imported.</summary>
        [Fact]
        public void ThePluginIsWrittenAgainstItsMasters()
        {
            using (var authoring = Authoring())
            {
                authoring.Import(new AssetImport
                {
                    Kind = AuthoredKind.Weapon, Template = "IronSword", EditorId = "MySword",
                    Fbx = Fbx((ModelSlot.Main, "sword.fbx")), MeshFolder = "MyMod",
                });
                authoring.Save();
            }

            using var written = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            Assert.Equal([_master.ModKey], written.ModHeader.MasterReferences.Select(m => m.Master));
            Assert.Equal("MySword", Assert.Single(written.Weapons).EditorID);
            Assert.True(File.Exists(Path.Combine(_out, "Meshes", "MyMod", "MySword.nif")));
        }

        [Fact]
        public void AnImportNeedsATemplateOfItsKindAndAMainMesh()
        {
            using var authoring = Authoring();

            Assert.Throws<ArgumentException>(() => authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Weapon, Template = "RockCliff01", EditorId = "X", Fbx = Fbx((ModelSlot.Main, "x.fbx")), MeshFolder = "M",
            }));
            Assert.Throws<ArgumentException>(() => authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Static, Template = "RockCliff01", EditorId = "X", Fbx = Fbx((ModelSlot.Ground, "x.fbx")), MeshFolder = "M",
            }));
        }
    }
}
