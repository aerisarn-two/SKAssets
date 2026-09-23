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

## 4. Into the world

An imported record is usable once something puts it where a player meets it. Vanilla
does that three ways, and each is a call:

| | Vanilla | Call |
| --- | --- | --- |
| placed in a cell | 10,019 statics, 1,735 activators, 181 weapons … | `Place(record, cell, placement)`, `PlaceInWorldspace(record, worldspace, placement)` |
| drawn from a leveled list | 3,023 weapons, 2,730 armours | `AddToLeveledList(record, list, level, count)` |
| in a container | 3,184 weapons, 3,134 armours | `AddToContainer(record, container, count)` |

- **A cell is overridden to hold a reference**, as the Creation Kit does. An interior cell
  is named by editor id; an exterior one is found by position, because most have no editor
  id: the 4,096-unit square at `(floor(x / 4096), floor(y / 4096))` of the worldspace. A
  cell's context has its sub-block and block above it, and its worldspace above those.
- **Rotation is given in degrees**, as the Creation Kit shows it, and written in radians,
  as the record holds it.
- **A leveled list or a container is overridden whole.** Another plugin overriding the
  same list wins or loses the entire list by load order, so two mods adding to one list
  need a patcher that merges them.

## 5. What an armour copies

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

244 of the 1,055 base armours wear more than one addon -- one per race family, or a
separate piece. `AddonsOf(template)` lists them with the races each dresses, and an
import can mesh them one by one:

```csharp
authoring.Import(new AssetImport
{
    Kind = AuthoredKind.Armor, Template = "ArmorIronHelmet", EditorId = "Helmet",
    Fbx = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = "helmet.fbx" },
    Addons = new Dictionary<string, IReadOnlyDictionary<ModelSlot, string>>
    {
        ["IronHelmetArgonianAA"] = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = "helmet_argonian.fbx" },
    },
    MeshFolder = @"MyMod\Armor",
});
```

An addon named takes its slots from its own map and the rest from the import's, and its
meshes are named after its place in the template (`MyMod_Helmet_1.nif`) so they do not
overwrite the import's. An addon not named wears the import's meshes, with a note -- or,
with `DropUnlistedAddons`, is left out, for a piece only some races wear. Naming an addon
the template does not wear is refused.

## 6. A new creature

A creature is a skeleton, a Havok project and a dozen records (`docs/new-race.md`), and
the behaviour graph is the part nobody writes from nothing. So a new creature starts as a
copy of one the game has, under a name of its own, and its parts are replaced from FBX:

```csharp
CreatureResult direwolf = authoring.ImportCreature(new NewCreature
{
    Template = "WolfRace",
    Name = "Direwolf",                     // MyMod_DirewolfProject, MyMod_DirewolfRace
    SourceMeshes = extractedMeshes,        // the three caches and the wolf's Havok files
    Skeleton = "direwolf_skeleton.fbx",    // rig, ragdoll and skeleton.nif
    Body = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = "direwolf.fbx" },
    Animations = ["direwolf_clips.fbx"],   // stacks named for the animations they replace
    Npc = "EncWolf",
});
```

- **The Havok files** -- project, character, behaviours, rig and animations -- are copied
  into a folder beside the template's, at the same depth, so a path the files spell with
  `..` reaches what it did: 66 of the wolf's 72 animations are copied, and the other six,
  paired killmoves under `..\SharedKillMoves`, are reached as the wolf reaches them.
- **The caches** get an entry for the new project with the template's clips and root
  motion, which is in no Havok file, and the set data and speed table are amended from the
  load order with the new plugin in it. They are written to the output's `Meshes`, and
  replace the game's; another creature mod's have to be rebuilt with them (`animgen`).
- **The skeleton** from FBX becomes the skeleton mesh and the rig through
  `SKAssets.Export`, written over a copy of the template's rig so what the rig model does
  not carry comes along. The body part data is copied to name it.
- **The body** is an armour import into a copy of the template's skin; the skin and its
  addons are made to dress the new race, since an addon names the races it fits.
- **The animations** go through `SKAssets.Export`'s clip exchange into the new project:
  written as the new creature's own files, uncompressed, with their root motion in its
  cache entry. No Havok codec runs to write them, so nothing needs Wine off Windows.
- **Movement types of its own**, with `OwnMovementTypes` or `Speeds`. The engine finds a
  creature's movement types by the `iState_<name>` constants of its **root** graph, so
  those are renamed -- in every copied graph, since graphs joined by reference share
  variables by name, and wherever an expression or a transition's condition spells them --
  and the records copied under the new names with the speeds given. Only the root's: a
  shared graph declares every species' (the quadruped graph 17), where the wolf's own root
  declares 2. The race's default movement types follow, and the speed table is written
  from the new records: a direwolf given an 800 run sweeps to 1,600.
- **Sounds of its own**, with `Sounds`: the audio files for each animation event given.
  A creature's voice and feet are its body's footstep set: the wolf's footstep
  `NPCWolfBarkFootstep` is tagged `NPCWolfBark`, the event its animations send, and its
  impact set's 78 entries, one per material, all play one impact whose sound is
  `NPCWolfBark`. So the set is copied onto the creature's body, and for each event given
  the footstep, impact set, impact and sound are copied, the sound naming the new files
  under `Sound\FX\<creature>\<event>`. Every other event keeps the template's sound. An
  event the body's set does not have is refused before anything is written.
- **Named after the creature**, everywhere a name is written down and nothing outside
  the project depends on it: the Havok files and the two places one names another; the
  animations, last of all, because a clip manifest is written against the template's
  names and means the slots; the graph's nodes, its events, its variables and its
  character properties; the records, including the idles. The rule is made from the
  template's name rather than listed, since the sabre cat's records mostly do not spell
  it out -- `SCatRecoil`, `ScatReset`, `CatIdleWarn` -- and a name misspelled by one
  letter still counts, which is how `SabreCastStartSwimming` comes out right. What is
  **not** renamed is what something else answers to: a bone, a ragdoll body and a
  constraint, which are matched across files by the request's bone map and by the skin;
  a paired kill move event, which is how two graphs agree on one animation; and a sound
  event, whose payload is the record to play.
- **Records for the sounds it asks for.** Every `SoundPlay.` in the graphs and the
  animations that names the template is copied under the creature's name -- a marker
  repointed at the descriptor beneath it -- and the reference points at the copy. The
  copies play the template's audio, which is the only audio there is; what they are for
  is that the files can be replaced without touching the game's own records. A shared
  graph asks for every species that uses it, and those are left alone.
- **The extra data a skeleton is read for.** A converted skeleton has the bones, the
  ragdoll and a `BSXFlags`; the game's own hang three more off the root. The actor's box
  is measured from the body rather than copied, since the template's is the template's
  size; the bone LOD list is remapped and left out when nothing survives; the skeleton
  identifier is copied, being an export's run and not an identity.
- **Shared unless asked**: the movement types and the sounds, both found by name.

Against the game, the wolf cloned as a direwolf opens from the caches written as an actor
with every behaviour, clip and root motion the wolf has, beside the game's 429 projects; a
wolf walk taken to FBX and back lands in the direwolf's own animation with its travel.

## 7. Traps

- **Paths in the archives are separated by `/` on Linux** and by `\` in the records.
  Compare the two after normalising, or every lookup misses.
- **A creature's caches replace the game's.** The merged files are one per load order, so
  a second creature mod's must be rebuilt together with these; `animgen` does it.
