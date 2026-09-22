# Importing an FBX as a record

What a plugin needs for an imported mesh to be usable in the game, measured over the
five masters (`Skyrim.esm` to `Dragonborn.esm`, 1,168,582 winning records), and how
`SKAssets.Authoring` writes it.

```csharp
using var authoring = PluginAuthoring.Open(dataFolder, "MyMod.esp", outputFolder);
authoring.Prefix = "MyMod_";

ImportResult sword = authoring.Import(new AssetImport
{
    Kind = AuthoredKind.Weapon,
    Template = "IronSword",               // an editor id, or 012EB7:Skyrim.esm
    EditorId = "Sword",                   // written as MyMod_Sword
    Name = "My Sword",
    Fbx = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = "sword.fbx" },
    MeshFolder = @"MyMod\Weapons",
});

authoring.Save();                         // MyMod.esp beside Meshes\MyMod\Weapons\MyMod_Sword.nif
```

The result lists every record written, every mesh, what the mesh rules found wrong with
each mesh for the record that names it, and the choices made on the caller's behalf.

## 0. The method: copy a record, and what it owns

A mesh is usable once a record names it, and a record is usable once the records it
depends on exist. A new record written from nothing would have to get every one of
those right; a copy of a similar vanilla record -- the **template** -- already has them.
So an import copies the template, points it at the new mesh, and handles each record the
template links to in one of two ways:

- **owned** -- nothing else refers to it, and it carries a mesh of its own: an armour's
  addons, a weapon's first-person static. It is copied, and the copy takes the new mesh;
- **shared** -- keywords, sounds, equip slots, impact sets, materials, enchantments,
  magic effects, races. The copy refers to it exactly as the template does.

The copies are new records in the new plugin, never overrides, so the masters are
unchanged. Nothing is placed in the world.

## 1. What each record type owns, shares, and is made by

For each record type that names a mesh: how many the masters hold and how many of those
are placed in a cell; which record types they link to, with how many link one they
**own** (a record nothing else refers to); and what refers to them.

| Type | Records | Placed | Owns, when it owns | Shares | Made or handed out by |
| --- | ---: | ---: | --- | --- | --- |
| **WEAP** | 3,262 | 181 | STAT, its first-person model (3,234 link one, 33 own it) | keyword, equip type, impact set, material, sounds, enchantment | COBJ 253, LVLI 3,023, CONT 3,184 |
| **ARMO** | 3,675 | 306 | ARMA, its body meshes (3,675 link one, 562 own it) | race, keyword, enchantment, sounds | COBJ 339, OTFT 579, LVLI 2,730 |
| ARMA | 1,076 | 0 | -- | race, footstep set, texture set | ARMO 1,048 |
| **AMMO** | 54 | 30 | PROJ, what it flies as (54 link one, 24 own it) | keyword, sounds | COBJ 20, LVLI 23 |
| **BOOK** | 1,042 | 670 | STAT, its inventory art (1,040 link one, 7 own it) | keyword, sounds, the spell it teaches | CONT, LVLI |
| **SCRL** | 99 | 43 | STAT, its menu display object (99, 0 own it) | equip type, magic effect, keyword | CONT 94, LVLI 62 |
| STAT | 12,105 | 10,019 | -- | material object, texture set | placed |
| MSTT | 806 | 668 | -- | sounds, texture set | placed |
| FURN | 515 | 438 | -- | keyword, idles | placed; IDLE 37 |
| DOOR | 320 | 282 | -- | open, close and loop sounds | placed |
| ACTI | 2,495 | 1,735 | -- | quest, message, sound marker, form list | placed |
| CONT | 591 | 534 | LVLI, its contents (400 link one, 73 own it) | sounds | placed |
| FLOR | 106 | 93 | -- | the ingredient it yields, sounds | placed |
| TREE | 263 | 212 | -- | the ingredient it yields, sounds | placed |
| LIGH | 494 | 374 | -- | sounds | placed; PROJ, MGEF |
| MISC | 1,015 | 307 | -- | keyword | COBJ 558 (made, or a component), CONT 432 |
| KEYM | 377 | 310 | -- | keyword, sounds | CONT 373, NPC_ 211 |
| SLGM | 17 | 13 | -- | keyword | CONT 17, LVLI 12 |
| ALCH | 407 | 248 | -- | magic effect, keyword, sounds | COBJ 61, CONT 385, LVLI 330 |
| INGR | 115 | 106 | -- | magic effect, keyword, sounds | LVLI 102, CONT 87 |
| ARTO | 318 | 0 | -- | -- | MGEF 155, VFX 159 |

Three things follow.

- **Few types own anything, and bar a container's contents what they own is a second
  mesh.** An armour's addon
  holds its body meshes; a weapon's first-person static, an ammunition's projectile, a
  book's inventory art are each a model of the same thing seen another way. That is what
  an import copies; everything else is shared.
- **Ownership is rarer than linking** because variants share: 2,866 weapons and 2,620
  armours are built on another of their type -- the enchanted copies -- and use its
  meshes. A variant named as a template is traced to its base, which is what gets copied.
- **Being made is a record of its own.** A weapon or armour a player can craft or temper
  has a constructible object (COBJ) whose `CreatedObject` is it: the iron sword has
  `RecipeWeaponIronSword` at the forge and `TemperWeaponIronSword` at the wheel. Those
  are copied to create the new record -- same bench, components and conditions -- unless
  the import asks not to.

## 2. The model slots

Where each FBX goes. `Main` is required; the rest default as vanilla does.

| Slot | Written to | Without it |
| --- | --- | --- |
| `Main` | the record's model; an armour addon's male body | -- |
| `FirstPerson` | a weapon's first-person static; an addon's male first-person body | a weapon uses `Main` (120 of 368 base weapons do); an armour keeps the template's |
| `Female`, `FemaleFirstPerson` | an addon's female bodies | the male mesh, where the template has a female one (796 of 1,071 addons do) |
| `Ground`, `FemaleGround` | an armour lying in the world | the template's |
| `LightWeight` | an addon's `_0` body, beside `Main` as `_1` | both weights are `Main` (614 of 1,071 addons come in weights) |
| `Inventory` | a copied book's or scroll's inventory static | the template's shared one |
| `Projectile` | a copied ammunition's projectile | the template's shared one |

Every mesh is named from the prefixed editor id -- `MyMod_Sword.nif`,
`MyMod_Cuirass_1.nif`, `MyMod_Arrow_projectile.nif` -- under the import's mesh folder,
and converted by NIFBX (`FbxToNif`). Each is then checked with
`SKAssets.Content.Assets.MeshRules` against the record type that names it: an armour mesh
skinned to bones of its own, not the actor's, is reported.

## 3. Textures

A DCC tool names a texture where it found it, `C:\work\sword_d.png`, and NIFBX keeps what
it can of that: from `textures\` on when the path has it, and otherwise the path as it
came with its extension made `.dds`. That names a file the game will never find, and the
texture itself is written nowhere. So each mesh's textures are handled as it is imported:

- every texture the FBX references is looked for beside the FBX -- where its relative path
  points, where its absolute one does, and by name in the FBX's folder or a `textures`
  folder beside it, **whatever the case of the name**: the game's own files spell the
  same texture `IronLongsword.dds` in a record and `ironlongsword.dds` in an archive;
- one that is found is written to `Textures\<TextureFolder>\<prefix><name>.dds` and the
  mesh pointed at it -- a DDS copied as it is, a PNG, TGA, JPEG or BMP encoded as BC7 with
  its mipmaps (StbImageSharp to decode, BCnEncoder.Net to encode);
- one that is not found keeps its path when that is a game path -- a mesh reusing a
  vanilla texture, which is ordinary -- and is reported as `texture-not-found` when it is
  an absolute path on somebody's disc.

`TextureFolder` defaults to the mesh folder. The textures written are in the result.

## 4. What an armour copies

Armour is the one type whose meshes live on another record, and the one with the most
choices:

```
ARMO MyMod_Cuirass            copy of ArmorIronCuirass
  ├─ WorldModel               the template's ground model, or the Ground slot
  └─ Armature ──▶ ARMA MyMod_CuirassAA      copy of IronCuirassAA: race, footsteps kept
                    ├─ WorldModel male    MyMod_Cuirass_1.nif  (+ _0)
                    ├─ WorldModel female  MyMod_Cuirass_1.nif, or the Female slot
                    └─ FirstPersonModel   the template's, or the FirstPerson slot
```

244 of the 1,055 base armours wear more than one addon -- one per race family, a
separate piece. Each is copied and given the same meshes, which is right for a single
piece and a starting point for the rest; the result notes it.

## 5. Traps

- **Paths in the archives are separated by `/` on Linux** and by `\` in the records.
  Compare the two after normalising, or every lookup misses.
- **Actors are not an import.** A creature is a skeleton, a Havok project, three caches and
  a dozen records (`docs/new-race.md`); the kinds here are the records that name one mesh
  or a few.
