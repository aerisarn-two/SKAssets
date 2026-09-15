# SKAssets

Finds every external file a Skyrim plugin refers to.

```csharp
var sweeper = new PluginAssetSweeper();

foreach (var reference in sweeper.Sweep("Skyrim.esm"))
    Console.WriteLine($"{reference.Path}\t{reference.MediaType.Mime}\t{reference.RecordType}");

// Meshes/Clutter/MiddleClass/Common/Basket01.nif   application/vnd.bethesda.nif   Static
// Textures/Cubemaps/DunCaveRuinGreen_e.dds         image/vnd.ms-dds              Cell
// Scripts/TimedAbilityScript.pex                   application/vnd.bethesda.papyrus   MagicEffect
```

Records are read through [Mutagen](https://github.com/Mutagen-Modding/Mutagen),
the same library se-cmd already uses, so the two projects agree about what a
record says. Nothing but the plugin is opened: the sweep never asks whether a
file exists, which is what lets it run over a folder of plugins with no game
installed.

## What counts as a reference

A plugin names a file in more ways than one, and they are not equally solid.
Every result says which kind it is, because the difference decides what you can
do with it.

| Origin | What it is | In the masters |
| --- | --- | --- |
| **Listed** | The subrecord exists to hold a filename and this is what it holds. | 58,608 |
| **Inferred** | Derived by convention. A script entry names `MyScript`; the file is `Scripts/MyScript.pex`. | 61,497 |
| **Untyped** | A field that holds a path while the schema calls it a string, or an unparsed subrecord that holds one. | 352 |
| **Scanned** | Found by walking every field looking for path-shaped strings. Discovery, off by default. | — |

Inference is most of the sweep and cannot be skipped without losing a category
outright: **every one of the game's Papyrus references is inferred**, because
not one record spells a script path out. It also points at files that were never
shipped — each compiled script implies a `.psc` beside it, and Bethesda shipped
almost no sources — so a missing-asset report wants to filter on origin rather
than trust the lot.

The untyped fields are few and real. A cell's water cubemap and a water type's
noise maps hold textures that no asset link covers, and both spell their paths
with a `Data\` prefix the link fields never carry. Two more are not strings at
all: where the format left a subrecord undefined after something replaced it,
Mutagen hands back its raw bytes, and 161 sound markers still carry the filename
the sound descriptor superseded — as does one weather record, holding its cloud
layers in the pre-Skyrim form.

**34 files in the masters are named this way and no other**: 21 sounds behind the
legacy field and 13 water cubemaps. A link-only sweep reports none of them.

`UntypedPathFields` lists the lot, and says which similar-looking fields are
deliberately left out — node names, Creation Kit filter folders, behaviour graph
events, and the two in five legacy sound values that name a folder to pick from
rather than a file.

## One file, however it was spelled

The masters were written by hand over a decade, and they disagree. All four of
these are the same sound:

```
\Data\Sound\FX\XXX_Placeholder_Silence.wav
Data\Sound\FX\XXX_Placeholder_Silence.wav
fx\xxx_placeholder_silence.wav
Sound/FX/XXX_Placeholder_Silence.wav
```

Every reference carries both forms: `Path`, normalised to what an archive can be
asked for — base folder present, forward slashes, no `Data\` prefix — and
`GivenPath`, the string as the record holds it, which is what you edit if you are
rewriting the record. `AssetInventory` groups by the first and keeps the second:

```csharp
var inventory = AssetInventory.Build(sweeper.Sweep("Skyrim.esm"));

Console.WriteLine(inventory.Count);                      // distinct files
foreach (var (category, files) in inventory.CountByCategory())
    Console.WriteLine($"{category}\t{files}");
```

## What a file is

Two answers, because they disagree often enough to be worth keeping apart.
`MediaType` is what the file is; `DeclaredType` is what the field it came from
was for. A race keeps its skeleton and its FaceGen morphs in *model* fields, so
a `.hkx` and an `.egt` both arrive declared `Model`.

Almost nothing Bethesda ships has a registered media type. Where one exists the
catalogue uses it — `image/vnd.ms-dds`, `image/png`,
`application/vnd.adobe.flash.movie` — and where none does it names a vendor type
rather than flattening the format to `application/octet-stream`:

| | | |
| --- | --- | --- |
| `.nif` `.btr` `.bto` | `application/vnd.bethesda.nif` | Model |
| `.hkx` | `application/vnd.havok.hkx` | Animation |
| `.pex` / `.psc` | `application/vnd.bethesda.papyrus` / `text/…` | Script |
| `.xwm` / `.fuz` | `audio/vnd.bethesda.xwm` / `audio/vnd.bethesda.fuz` | Sound / Voice |

Those are ours, not IANA's: stable identifiers to sort and filter on, and a
claim about which format the path says it is, not about the bytes — which a
plugin sweep never reads.

The coarse `AssetCategory` beside it is what the file is *for*, and the two do
not always follow from each other. One container does several jobs: a `.hkx` is
a skeleton, a behaviour graph or an animation, and a `.wav` is a sound effect, a
line of dialogue or a music track depending only on the folder it sits in. Where
the extension cannot tell them apart, the folder does.

## The vanilla masters

`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`, `HearthFires.esm`, `Dragonborn.esm`
and `_ResourcePack.esl`, swept in about fifty seconds:

| | References | Distinct files |
| --- | --- | --- |
| Script | 58,876 | 26,024 — `.pex` 13,012 · `.psc` 13,012 |
| Model | 41,183 | 18,326 — `.nif` 17,810 · `.tri` 514 · `.egt` 2 |
| Texture | 9,002 | 1,528 — `.dds` 1,462 · `.png` 66 |
| Sound | 6,750 | 5,759 — `.wav` |
| Animation | 3,866 | 98 — `.hkx` |
| Music | 330 | 274 — `.wav` |
| Quest start-up | 449 | 6 — `.seq` |
| | **120,457** | **52,016** |

One reference in all of that is unclassifiable: Dragonborn gives a static the
model `DLC02\Architecture\Thirsk`, with no extension on it.

## Who refers to a file

Asking a record for its asset links asks the records inside it too: a dialogue
topic hands back every script on its responses, a cell every script on the
objects placed in it. Since a sweep visits those records in their own right,
taking the container's answer as well reports each of them twice — in Skyrim.esm
that was 29,320 duplicate script references, one for every INFO and every placed
object carrying a script.

So a reference is attributed to the record that holds it and not to whatever
contains that record: the INFO, not the topic; the placed object, not the cell.
What a container does hold in its own right survives, which is not always
nothing — a worldspace reports its own model and its cells' contents in the same
answer, and only the first is its.

## How the coverage was established

Three passes, and each found something the one before it could not:

1. Every asset-link field in the Skyrim schema — what Mutagen already models.
2. Every plain string field, and every raw subrecord that decodes to something
   path-shaped, read across all six masters — which found the water textures and
   the legacy sound and cloud fields.
3. [UESP's field tables](https://en.uesp.net/wiki/Skyrim_Mod:Mod_File_Format),
   record by record, against what the sweep reports — which confirmed the rest:
   inventory icons, the region canopy shadow, head part morphs and the sound
   descriptors are all covered as links.

What UESP documents and this deliberately does not report: `SOUN`'s folder-valued
filenames (a folder, not a file), `TES4`'s master list (plugins, not assets), and
`CELL`'s `XCGD`, which exists only in the Xbox 360 and PS3 builds of the official
DLC and is not in any PC plugin.

## What a mesh is: SKAssets.Content

The sweep says a plugin needs `Meshes/Clutter/Apple.nif`. It does not say what
that file *is* — and Skyrim's meshes are not one kind of file with optional
parts. `SKAssets.Content` reads the mesh through
[NIFBX](https://github.com/aerisarn-two/NIFBX) and the Havok side through
[HKSK](https://github.com/aerisarn-two/HKSK), and answers that:

```csharp
var reader = new NifProfileReader();

using var stream = File.OpenRead("windmill.nif");
NifProfile profile = reader.Read(stream);

NifRole role = NifRoles.Of(profile);     // NifRole.HavokProp
NifRoles.NeedsCompanionFiles(role);      // true -- the animation is not in the mesh

var projects = HavokProjectIndex.Load(@"Data\meshes");
projects.Resolve(profile.BehaviorGraph); // FarmhouseWindMill, a prop project

MeshDependencies.Of(profile, projects);  // the project file, and every file it declares
MeshRules.Check("Activator", profile);   // what this record requires of this mesh
```

**24% of the meshes the game's records name cannot be read, animated or exported
from the mesh alone.** Three of the eight roles need companion files:

| Role | Meshes | Needs |
| --- | ---: | --- |
| SkinnedAttachment | 3,201 | the skeleton its bones live in, which the RACE record names |
| HavokProp | 987 | the Havok project its behaviour graph names, and that project's clips |
| ActorSkeleton | 52 | the project's rig, which decides what the animations drive |

The other five — static geometry, Gamebryo-animated meshes, self-skinned meshes,
effects and camera paths — are complete in themselves.

### Checking a mesh against the record that names it

Every rule was measured against the shipped game before it was written, and each
carries what it scored, because a rule vanilla breaks in quantity is describing
the checker rather than the format. A head part is a skin over an external
skeleton in 474 of 474 cases; an ADDN mesh carries no geometry in 91 of 92.

The two that take two files are the two that find real bugs:

```csharp
SkeletonRules.CheckSkin(profile.SkinBones, raceSkeleton.Nodes);   // 832/835 in vanilla
SkeletonRules.CheckRig(rigBones, skeletonMesh.Nodes);             // 199/204
```

Of the three armour meshes in the game that fail the first, one is a Falmer
helmet weighted to human bones. It loads, and hangs in the air.

**`docs/asset-kinds.md`** is the evidence: the eight roles and their counts, what
each record type names, how an actor and a prop are assembled across files, and
the traps — the stale split animation cache, the two skeletons inside every
`skeleton.hkx`, and the `x_` bones that belong to Havok and to no mesh.

## One FBX for a creature: SKAssets.Export

An actor's skeleton is stored twice and neither copy is complete.
`skeleton.nif` has the bone tree, the collision shapes, the rigid bodies and the
constraints; `skeleton.hkx` has the animation rig, the ragdoll and the mappers
between them. A DCC can open neither.

`SkeletonExchange` folds both into one FBX and takes them apart again:

```csharp
FbxDocument scene = SkeletonExchange.Export(mesh, havok);   // one file from two
SkeletonFile rebuilt = SkeletonExchange.ImportHavok(scene); // and back again
```

Over the 45 creatures in the shipped game that ship both files with a ragdoll,
that round trip reproduces **all 45 `skeleton.hkx` files byte for byte** — every
bone, body, capsule and joint bit-identical. Not within a tolerance: a skeleton
that comes back with one bit changed is a file the game still loads and the
ragdoll may still behave differently in.

`Export` without a `skeleton.hkx` is the other half of the job. It derives the
ragdoll from the mesh alone — names by a vanilla convention, physics out of the
NIF's rigid bodies, collision filters allocated from the ragdoll's own hierarchy
— which is what authoring new content needs. Both paths exist because exactness
and derivation are different jobs: a field can be worth deriving and still wrong
to derive when the answer is sitting in the other file.

**`docs/skeleton-exchange.md`** is the evidence, and the reference for the
property convention a DCC script reads: what is derived and what is carried, with
the measurement behind each choice, the three shipped files that disagree with
themselves, and how a collision filter is authored from scratch.

The clips go into the same scene, one animation stack each:

```csharp
ClipReport clips = ClipExchange.AddClips(scene, havok.Rig, project);
```

Over 46 of the game's 49 actor projects that is 2,733 stacks, every one of them
driving the whole rig, with none bound to nothing, missing or undecodable. The two
player projects are left to the caller rather than done by default: the cost is
linear and the draugr's 216 clips are already 187 MB, so the player's 1,656 is
about a gigabyte and a half.

Or the whole creature in one call, which is what an animator opening one
actually wants — the bodies beside the skeleton as well as the skeleton, and
every animation the project has:

```csharp
CreatureAssets creature = CreatureExchange.Find(folder, cache)!;
FbxDocument scene = CreatureExchange.Export(creature, database, out CreatureReport report);
```

The chicken is 33 bones, 7 ragdoll bodies, one body mesh and 20 clips in 7.7 MB;
the falmer is ten bodies and 122 clips in 115 MB. A creature's visible body is
neither of its skeleton files — it is a third thing in the same folder — and
`SceneMerge` folds it onto the skeleton's own bones rather than beside a second
copy of them.

And back apart into the files the game reads, however much of a creature the
scene turns out to hold:

```csharp
CreatureImport back = CreatureExchange.Import(scene, database, project);
// back.Skeleton, back.Meshes["chicken.nif"], back.Havok, back.Clips
```

Each part is written only where the scene carries it: no `skeleton.hkx` for a
scene that never had a rig, no `skeleton.nif` for a scene of pure animation. The
bodies come back one file each because the export records which files every node
belongs to — a bone is in the skeleton and in every body skinned to it — and
without that record the only honest answer is one enormous `skeleton.nif` with
every body inside it.

And back out again, once an animator has been at it:

```csharp
ImportReport back = ClipExchange.ImportClips(scene, project);
cache.Save();                 // the packfiles are written; the cache is not
project.SaveCharacter();      // only when a slot was added
```

One packfile per stack, plus the cache entries that address it. A stack is matched
to its slot by what the export wrote onto it rather than by its name, so a stack
this library did not put there is left alone — seven of the game's actors carry
animation in their `skeleton.nif`, and importing that would invent an animation
the creature never had.

**`docs/animation-export.md`** is that half: why root motion comes from the cache
and not from the animation file, why a slot and a clip are not the same thing, and
the one number to check after an export.

## What it does not find yet

- **Voice.** No record names a dialogue file. `.fuz` and `.lip` paths are built
  from the quest, the topic and the FormID of the response, and reproducing that
  is a feature of its own.
- **What the archives actually hold.** A record says `.wav` and the file shipped
  is `.xwm`; a sweep reports what the record says. Reconciling the two means
  reading the BSAs, which is the next thing this component learns to do.
- **What a file refers to in turn.** A NIF names its textures, a behaviour graph
  names its animations. That is a sweep of the asset, not of the plugin.

## Reading a localised plugin off Windows

Parsing a localised record sends Mutagen looking for `.strings` files, which
ends in a lookup for `Plugins.txt` under `LocalAppData` — a variable Linux does
not have, surfacing as `ArgumentNullException: Value cannot be null. (Parameter
'path1')` from somewhere that looks nothing like the cause. `MutagenRuntime`
points the variable at a folder if it is unset, which is all the lookup needs,
and the sweep calls it before opening anything. A real value is left alone, and
on Windows it does nothing.

## Building and testing

```
dotnet build
dotnet test
```

141 tests, a few seconds. They build plugins in memory and describe meshes rather
than reading any, so they run anywhere. Five more need the game and **skip**
without it, rather than passing vacuously — so the count says whether they ran.

`SKAssets.Content` restores NIFBX and HKSK from GitHub Packages, so building it
needs `GITHUB_USERNAME` and `GITHUB_TOKEN` set, with a token carrying
`read:packages`. `SKAssets` itself needs neither.

The corpus suites read the game instead, and do not run unless asked:

```
SKASSETS_SKYRIM_DATA="/path/to/Skyrim Special Edition/Data" dotnet test \
    --filter "FullyQualifiedName~MasterCorpus"     # the plugins, about seven minutes

SKASSETS_SKYRIM_DATA=... SKASSETS_HAVOK_MESHES=/path/to/loose/meshes dotnet test \
    --filter "FullyQualifiedName~MeshCorpus"       # 17,670 meshes, about twenty

SKASSETS_SKYRIM_DATA=... dotnet test \
    --filter "FullyQualifiedName~RoundTripCorpus"  # 45 skeletons, three seconds
```

The second wants the Havok side as loose files, because the animation cache is
text the game ships inside a BSA and HKSK reads a folder. Without
`SKASSETS_HAVOK_MESHES` it checks everything except the project resolution.

## Layout

`src/SKAssets` — the plugin sweep. Depends on Mutagen and nothing else.

- `Assets/` — what a file is: the media type catalogue, the categories, and the
  path normalising.
- `References/` — what a reference is, and the inventory that turns a sweep of
  references into a list of files.
- `Plugins/` — the sweep: asset links, the untyped fields, the deep scan, and
  the runtime shim that lets any of it run off Windows.

`src/SKAssets.Content` — what the files themselves are. Depends on NIFBX and
HKSK, which is why it is a separate package: a tool that only wants to know what
a plugin names should not be made to carry a NIF reader and a Havok library.

- `Nif/` — the census of a mesh, and the role read off it.
- `Assets/` — what a record requires of the mesh it names, and the two checks
  that take a second file.
- `Havok/` — the animation cache indexed by what a mesh can name.

`src/SKAssets.Export` — the files as one scene. The heaviest of the three and the
reason for the split: it carries NIFBX, HKFBX and HKSK together.

- `SkeletonExchange` — a creature's two skeleton files, out to one FBX and back.
- `Fbx/SceneMerge` — folding several documents into one without ending up with
  one skeleton per document.
- `Fbx/HavokBridge`, `Fbx/JointBridge` — a body and a joint said in the ragdoll's
  vocabulary rather than the mesh's.
- `Fbx/ExactPose` — the transforms carried beside the Euler channels a viewer
  reads, because a quaternion does not survive the trip through them.
- `Fbx/BoneUnion` — the nodes only one of the two files has, and which.
- `Fbx/RagdollFilter` — a Havok collision filter, packed, unpacked and allocated.
- `ClipExchange` — the creature's animations, one stack each, over that skeleton,
  and back out into packfiles and cache entries.
- `CreatureExchange` — all of the above for one creature, from its folder, and
  back apart into the files it came from.
- `AssetRecognition` — what a path is, by opening it rather than by its name.

## Licence

**GPL-3.0-or-later.** It belongs to the same family as se-cmd and NIFBX.

## Installing

Published to GitHub Packages. With a `nuget.config` pointing at the feed and
`GITHUB_USERNAME` and `GITHUB_TOKEN` set (the token needs `read:packages`):

```xml
<PackageReference Include="SKAssets" Version="0.1.14" />          <!-- what a plugin names -->
<PackageReference Include="SKAssets.Content" Version="0.1.14" />  <!-- what those files are -->
<PackageReference Include="SKAssets.Export" Version="0.1.14" />   <!-- those files as one scene -->
```

All three carry the same version and are released together, because each is
built against a particular copy of the one below it and nothing else makes that
true at restore time. Taking the first alone brings in Mutagen and nothing more;
the third brings NIFBX, HKFBX and HKSK with it.
