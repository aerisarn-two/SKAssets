using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using SKAssets.Content.Nif;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// A master built in memory, holding one template of each shape an import copies.
    /// </summary>
    /// <remarks>
    /// Each is laid out the way the measured vanilla ones are: the sword owns a first-person
    /// static and is made and tempered at a bench, and has an enchanted variant built on it; the
    /// cuirass owns an addon whose body meshes come in two weights; the arrow shares its
    /// projectile; the book shares its inventory art.
    /// </remarks>
    internal static class Masters
    {
        public static ISkyrimModGetter Build()
        {
            var mod = new SkyrimMod(ModKey.FromName("Skyrim", ModType.Master), SkyrimRelease.SkyrimSE);

            var sword = mod.Keywords.AddNew("WeapTypeSword");
            var forge = mod.Keywords.AddNew("CraftingSmithingForge");
            var wheel = mod.Keywords.AddNew("CraftingSmithingSharpeningWheel");
            var ingot = mod.MiscItems.AddNew("IngotIron");

            var shown = mod.Statics.AddNew("1stPersonIronSword");
            shown.Model = new Model { File = @"Weapons\Iron\1stPersonLongSword.nif" };

            var iron = mod.Weapons.AddNew("IronSword");
            iron.Name = "Iron Sword";
            iron.Model = new Model { File = @"Weapons\Iron\LongSword.nif" };
            iron.FirstPersonModel.SetTo(shown);
            iron.Keywords = [sword.ToLink<IKeywordGetter>()];

            var enchanted = mod.Weapons.AddNew("EnchIronSwordFire01");
            enchanted.Template.SetTo(iron);

            foreach ((string id, Keyword bench) in new[] { ("RecipeWeaponIronSword", forge), ("TemperWeaponIronSword", wheel) })
            {
                var recipe = mod.ConstructibleObjects.AddNew(id);
                recipe.CreatedObject.SetTo(iron);
                recipe.WorkbenchKeyword.SetTo(bench);
                recipe.Items = [new ContainerEntry { Item = new ContainerItem { Item = ingot.ToLink<IItemGetter>(), Count = 2 } }];
            }

            var nord = mod.Races.AddNew("NordRace");
            var addon = mod.ArmorAddons.AddNew("IronCuirassAA");
            addon.Race.SetTo(nord);
            addon.WorldModel = new GenderedItem<Model?>(
                new Model { File = @"Armor\Iron\M\Cuirass_1.nif" },
                new Model { File = @"Armor\Iron\F\Cuirass_1.nif" });

            var cuirass = mod.Armors.AddNew("ArmorIronCuirass");
            cuirass.Name = "Iron Armor";
            cuirass.Armature.Add(addon.ToLink<IArmorAddonGetter>());
            cuirass.WorldModel = new GenderedItem<ArmorModel?>(new ArmorModel { Model = new Model { File = @"Armor\Iron\CuirassGND.nif" } }, null);

            // A helmet with an addon per race family, as 244 of vanilla's base armours have.
            var argonian = mod.Races.AddNew("ArgonianRace");
            var helmet = mod.Armors.AddNew("ArmorIronHelmet");
            foreach ((string id, Race race, string mesh) in new[] { ("IronHelmetAA", nord, @"Armor\Iron\Helmet.nif"), ("IronHelmetArgonianAA", argonian, @"Armor\Iron\HelmetArgonian.nif") })
            {
                var aa = mod.ArmorAddons.AddNew(id);
                aa.Race.SetTo(race);
                aa.WorldModel = new GenderedItem<Model?>(new Model { File = mesh }, null);
                helmet.Armature.Add(aa.ToLink<IArmorAddonGetter>());
            }

            var flies = mod.Projectiles.AddNew("ArrowIronProjectile");
            flies.Model = new Model { File = @"Weapons\Iron\IronArrowProjectile.nif" };
            var arrow = mod.Ammunitions.AddNew("IronArrow");
            arrow.Model = new Model { File = @"Weapons\Iron\IronArrow.nif" };
            arrow.Projectile.SetTo(flies);

            var art = mod.Statics.AddNew("BookInventoryArt");
            var book = mod.Books.AddNew("BookGeneric");
            book.Model = new Model { File = @"Clutter\Books\Book01.nif" };
            book.InventoryArt.SetTo(art);

            var rock = mod.Statics.AddNew("RockCliff01");
            rock.Model = new Model { File = @"Landscape\Rocks\RockCliff01.nif" };

            // Somewhere to put things: an inn, a patch of wilderness, a loot list, a merchant's chest.
            var inn = new Cell(mod) { EditorID = "WhiterunBanneredMare", Flags = Cell.Flag.IsInteriorCell };
            mod.Cells.Records.Add(new CellBlock
            {
                BlockNumber = 0,
                GroupType = GroupTypeEnum.InteriorCellBlock,
                SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [inn] }],
            });

            var tamriel = new Worldspace(mod) { EditorID = "Tamriel" };
            var wild = new Cell(mod) { Grid = new CellGrid { Point = new P2Int(1, 2) } };
            tamriel.SubCells.Add(new WorldspaceBlock
            {
                BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellBlock,
                Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [wild] }],
            });
            mod.Worldspaces.Add(tamriel);

            var loot = mod.LeveledItems.AddNew("LItemWeaponSword");
            loot.Entries = [];
            var chest = mod.Containers.AddNew("MerchantWhiterunBlacksmithChest");
            chest.Items = [];

            return mod;
        }
    }

    /// <summary>An importer that writes a placeholder and describes it, so no FBX is needed.</summary>
    internal sealed class StubImporter(NifProfile? profile = null) : IMeshImporter
    {
        public List<(string Fbx, string Nif)> Calls { get; } = [];

        public ImportedMesh Import(MeshTarget target)
        {
            Calls.Add((target.Fbx, target.Nif));
            Directory.CreateDirectory(Path.GetDirectoryName(target.Nif)!);
            File.WriteAllText(target.Nif, target.Fbx);
            return new ImportedMesh(profile ?? new NifProfile { RootType = "BSFadeNode", Shapes = 1, Collisions = 1 }, [], []);
        }
    }
}
