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

Sixty-nine tests, five seconds. They build plugins in memory rather than reading
any, so they run anywhere.

The corpus suite sweeps the masters instead, and does not run unless asked:

```
SKASSETS_SKYRIM_DATA="/path/to/Skyrim Special Edition/Data" dotnet test \
    --filter "FullyQualifiedName~MasterCorpus"
```

## Layout

- `Assets/` — what a file is: the media type catalogue, the categories, and the
  path normalising.
- `References/` — what a reference is, and the inventory that turns a sweep of
  references into a list of files.
- `Plugins/` — the sweep: asset links, the untyped fields, the deep scan, and
  the runtime shim that lets any of it run off Windows.

## Licence

**GPL-3.0-or-later.** It belongs to the same family as se-cmd and NIFBX.

## Installing

Published to GitHub Packages. With a `nuget.config` pointing at the feed and
`GITHUB_USERNAME` and `GITHUB_TOKEN` set (the token needs `read:packages`):

```xml
<PackageReference Include="SKAssets" Version="0.1.0" />
```
