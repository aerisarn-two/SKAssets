using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>An armour with an addon per race family: one set of meshes, or a set per addon.</summary>
    public sealed class AddonTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-addons-" + Guid.NewGuid().ToString("N"));
        private readonly StubImporter _meshes = new();

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        private PluginAuthoring Authoring() => new([Masters.Build()], "MyMod.esp", _out, _meshes);

        private static Dictionary<ModelSlot, string> Main(string fbx) => new() { [ModelSlot.Main] = fbx };

        [Fact]
        public void TheTemplatesAddonsAreListedWithTheRacesTheyDress()
        {
            using var authoring = Authoring();

            var addons = authoring.AddonsOf("ArmorIronHelmet");

            Assert.Equal(["IronHelmetAA", "IronHelmetArgonianAA"], addons.Select(a => a.EditorId));
            Assert.Equal(["ArgonianRace"], addons[1].Races);
            Assert.Equal(@"Armor\Iron\HelmetArgonian.nif", addons[1].MaleModel);
        }

        /// <summary>Without addons named, every copy wears the import's meshes, and the result says so.</summary>
        [Fact]
        public void EveryAddonWearsTheImportsMeshesUnlessTold()
        {
            using var authoring = Authoring();
            ImportResult result = authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronHelmet", EditorId = "MyHelmet", Fbx = Main("helmet.fbx"), MeshFolder = "MyMod",
            });

            Assert.Equal(2, authoring.Plugin.ArmorAddons.Count);
            Assert.All(authoring.Plugin.ArmorAddons, aa => Assert.Equal(@"MyMod\MyHelmet.nif", aa.WorldModel!.Male!.File.GivenPath));
            Assert.Contains(result.Notes, n => n.Contains("each copy wears the same meshes"));
        }

        /// <summary>An addon named gets meshes of its own, named so they do not overwrite the import's.</summary>
        [Fact]
        public void ANamedAddonGetsItsOwnMeshes()
        {
            using var authoring = Authoring();
            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronHelmet", EditorId = "MyHelmet", Fbx = Main("helmet.fbx"), MeshFolder = "MyMod",
                Addons = new Dictionary<string, IReadOnlyDictionary<ModelSlot, string>> { ["IronHelmetArgonianAA"] = Main("helmet_argonian.fbx") },
            });

            var armour = authoring.Plugin.Armors.Single();
            var worn = armour.Armature.Select(l => authoring.Plugin.ArmorAddons[l.FormKey]).ToList();
            Assert.Equal(@"MyMod\MyHelmet.nif", worn[0].WorldModel!.Male!.File.GivenPath);
            Assert.Equal(@"MyMod\MyHelmet_1.nif", worn[1].WorldModel!.Male!.File.GivenPath);
            Assert.Contains(_meshes.Calls, c => c.Fbx == "helmet_argonian.fbx" && c.Nif.EndsWith("MyHelmet_1.nif"));
        }

        /// <summary>The addons not named can be left out, for a piece only some races wear.</summary>
        [Fact]
        public void TheAddonsNotNamedCanBeLeftOut()
        {
            using var authoring = Authoring();
            authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronHelmet", EditorId = "MyHelmet", Fbx = Main("helmet.fbx"), MeshFolder = "MyMod",
                Addons = new Dictionary<string, IReadOnlyDictionary<ModelSlot, string>> { ["IronHelmetAA"] = Main("helmet.fbx") },
                DropUnlistedAddons = true,
            });

            var addon = Assert.Single(authoring.Plugin.ArmorAddons);
            Assert.Equal(addon.FormKey, Assert.Single(authoring.Plugin.Armors.Single().Armature).FormKey);
        }

        [Fact]
        public void AnAddonTheTemplateDoesNotWearIsRefused()
        {
            using var authoring = Authoring();
            Assert.Throws<ArgumentException>(() => authoring.Import(new AssetImport
            {
                Kind = AuthoredKind.Armor, Template = "ArmorIronHelmet", EditorId = "MyHelmet", Fbx = Main("helmet.fbx"), MeshFolder = "MyMod",
                Addons = new Dictionary<string, IReadOnlyDictionary<ModelSlot, string>> { ["NoSuchAA"] = Main("x.fbx") },
            }));
        }
    }
}
