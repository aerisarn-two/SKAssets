# How a plugin refers to a file

What the record formats do, what Mutagen models of it, and what it leaves for
this component to do. Written while auditing the vanilla masters; the numbers
are from that sweep and are worth re-measuring rather than trusting if the
Mutagen version moves.

The format reference is UESP's
[Skyrim Mod:Mod File Format](https://en.uesp.net/wiki/Skyrim_Mod:Mod_File_Format).
What follows is the part of it that names files, checked against the masters
rather than restated from the wiki.

## Mutagen already models most of it

Mutagen types path-bearing fields as **asset links** rather than strings, each
carrying an asset type that knows its base folder and its expected extensions.
Skyrim has twelve:

| Asset type | Base folder | Extensions |
| --- | --- | --- |
| Model | `Meshes` | `.nif` |
| DeformedModel | `Meshes` | `.tri` |
| BodyTexture | `Meshes` | `.egt` |
| Behavior | `Meshes` | `.hkx` |
| Texture | `Textures` | `.dds` `.png` |
| Sound | `Sound` | `.wav` `.xwm` |
| Music | `Music` | `.wav` `.xwm` |
| ScriptCompiled | `Scripts` | `.pex` |
| ScriptSource | `Scripts/Source` | `.psc` |
| Interface | `Interface` | `.swf` `.gfx` |
| Translation | `Strings` | `.strings` `.dlstrings` `.ilstrings` |
| Seq | `Seq` | `.seq` |

A link's asset type is the type of the **field**, not of the file. A RACE keeps
its behaviour graph and its FaceGen morphs in model fields, so across the masters
408 `.hkx` and 393 `.egt` references arrive declared `Model`. Both claims are
kept: `DeclaredType` for the field, `MediaType` for the file.

`EnumerateAssetLinks` takes a query, and the three answers are different
questions:

- **Listed** — the paths records hold. 58,608 across the masters.
- **Inferred** — paths derived from something that is not one. 61,497, almost
  all of them scripts: a VMAD script entry names `MyScript` and the convention
  makes that `Scripts/MyScript.pex` plus `Scripts/Source/MyScript.psc`. Quest
  fragment names resolve the same way (1,109 of 1,111 in the masters, the other
  two truncated by the format's name limit).
- **Resolved** — needs a link cache and a load order, and is about what a
  reference resolves *to*. Not used here: this component reads one plugin at a
  time and asks what it names, not what wins.

### Asking a record includes what is inside it

`EnumerateAssetLinks` traverses contained records as well: a DialogTopic hands
back every script on its responses, a cell every script on the objects placed in
it, a worldspace everything in its cells. `EnumerateMajorRecords` visits those
records in their own right, so sweeping every record and taking every answer
counts the contained ones twice -- 29,320 duplicate references in Skyrim.esm,
42,760 across the masters, every one of them a script.

Each is attributed to the record that holds it, by subtracting what a record's
direct children report from what the record reports. The remainder is not always
empty, which is why the subtraction has to be a subtraction and not a rule about
record types:

| Container | Reports | Its own |
| --- | --- | --- |
| DialogTopic | its responses' scripts | none, ever |
| Cell | its placed objects' scripts | its own scripts, and the water textures below |
| Worldspace | its cells' contents | its own model and textures |

Skyrim's containment is all of it: a worldspace holds cells, a cell holds what is
placed in it and its navigation meshes, a topic holds its responses. Quests do
not hold their topics -- those are records of their own -- and nothing else
nests.

## What it does not model

Three passes established this: every asset-link field in the schema, every plain
string field and raw subrecord read across all six masters, and UESP's field
tables record by record. Out of 869,687 records in Skyrim.esm the difference the
second pass found is small, and most of it is not an asset at all.

**Real, and missed by a link-only sweep:**

| Field | In the masters | Note |
| --- | --- | --- |
| `SoundMarker.FNAM` | 161 | Legacy sound filename, raw bytes |
| `Cell.WaterEnvironmentMap` | 94 | Cubemap reflected in the cell's water |
| `Water.UnusedNoisemaps` | 93 | Named unused, filled anyway |
| `Weather.DNAM` `CNAM` `ANAM` `BNAM` | 4 | Cloud layers in the pre-Skyrim form, raw bytes |

The water fields spell their paths `Data\Textures\...`, a prefix no asset link
carries. `Cell.WaterNoiseTexture`, `BodyPartData.Parts.LimbReplacementModel`,
`Class.Icon` and `Faction.Ranks.Insignia` are the same kind of field and are read
too, though the masters leave all four empty.

**34 files in the masters are named by one of these and by nothing else** -- 21
sounds behind `FNAM`, 13 cubemaps behind `WaterEnvironmentMap`. That is the size
of the hole a link-only sweep leaves.

### The legacy fields are not strings at all

Where the format left a subrecord undefined -- `SOUN`'s `FNAM` after the sound
descriptor replaced it, `WTHR`'s cloud layers after the texture fields did --
Mutagen parses nothing and hands back the raw bytes. A path in one is invisible
to anything that reads properties, including the deep scan, which only ever sees
strings. UESP documents both as zstrings, and the masters still fill both.

They are decoded as Latin-1, which reads the format's single-byte characters
without throwing on any of them. That matters because the subrecords are
undefined: the same four letters in another record may hold a struct, so reading
one has to come back empty-handed rather than fail. Requiring a file extension is
what tells a filename from a struct -- and from a folder, which is what two in
five of the sound values hold, the marker playing whatever is inside it.

### Cross-checked against UESP

[The wiki's field tables](https://en.uesp.net/wiki/Skyrim_Mod:Mod_File_Format)
were read record by record and diffed against what the sweep reports. Everything
else it documents as a filename is covered as an asset link: the inventory icons
(`ICON`/`MICO`) on thirteen record types, `REGN`'s canopy shadow, `HDPT`'s morph
files, `CLMT`'s sun textures, `EYES`, `MUST`, `SNDR`, `LSCR`'s camera path.

Documented, and deliberately not reported:

- `SOUN`'s folder-valued `FNAM` -- 391 of them, each a folder the marker picks a
  sound from. A folder is not an asset and has no media type.
- `TES4`'s master list. Those are plugins, and the load order's business.
- `CELL`'s `XCGD`, a `.nif` filename found only in the Xbox 360 and PS3 builds of
  the official DLC. No PC plugin has one, and Mutagen does not model it.
- `LAND`'s fallback to `dirt02.dds`, which is the engine's behaviour when a
  reference is absent rather than a reference.

**Path-shaped and not a reference**, which is why the deep scan is opt-in:

- `BodyPartData.Parts.PartNode`, `IkStartNode`, `VatsTarget`, `GoreTargetBone` —
  node names inside a skeleton. Some are spelled
  `BASE Meshes\Actors\Dragon\Character Assets\Skeleton.nif`, which is a node
  named after the file it belongs to.
- `Quest.Filter` — the Creation Kit folder the quest is filed under. 114 of them
  in Skyrim.esm, all ending in a separator, none with a file at the end.
- `IdleAnimation.AnimationEvent`, `Npc.Attacks.AttackEvent`,
  `Race.MovementTypeNames` — events inside a behaviour graph. The graph is the
  reference; these reach an animation through it.

## What the records get wrong, and what to do about it

**Empty fields.** Around 2,200 links in the masters have the subrecord and
nothing in it — 1,494 models, 685 textures. A record with a model field it does
not use refers to nothing, and they are dropped.

**Four spellings of one path.** The same sound appears as
`\Data\Sound\FX\XXX_Placeholder_Silence.wav`, as `Data\Sound\FX\...`, and as
`fx\xxx_placeholder_silence.wav` relying on its field's base folder. Fourteen
files in the masters are named more than one way. Normalising to the archive's
form — base folder present, forward slashes, no `Data\`, compared without
regard to case — makes them one file, and `AssetInventoryEntry.Spellings` keeps
what they were.

**No extension at all.** `Meshes/DLC02/Architecture/Thirsk`, one static in
Dragonborn. The catalogue reports it unknown, which is the honest answer.

## Reading a localised plugin off Windows

Parsing a localised record — a PERK's effects, an ARMO's name — makes Mutagen
resolve a `.strings` lookup, which asks for the applicable archives, which asks
for the load order, which asks for `Plugins.txt` under `LocalAppData`. There is
no such variable on Linux and the null comes back as

```
ArgumentNullException: Value cannot be null. (Parameter 'path1')
    at Mutagen.Bethesda.Plugins.Order.DI.PluginListingsPathProvider.Get(GameRelease)
```

from a stack that mentions neither strings nor localisation. The variable is
read by its Windows spelling, `LocalAppData`, which on a case-sensitive system
is not the same as `LOCALAPPDATA` — setting the latter changes nothing. Pointing
`LocalAppData` at any folder is enough; the file behind it need not exist.
`MutagenRuntime.Prepare` does that when the variable is unset, and the sweep
calls it before opening a plugin.

## Cost

Binary overlays, one pass, no link cache. On a 2026 desktop:

| | Records | References | Time |
| --- | --- | --- | --- |
| Skyrim.esm | 869,687 | 83,898 | 38 s |
| Dragonborn.esm | 178,654 | 14,311 | 9 s |
| Dawnguard.esm | 95,133 | 12,063 | 4 s |
| Update.esm | 16,044 | 4,380 | 2 s |

Subtracting what contained records report costs almost nothing: it is paid only
by records that both have links and contain something, which is cells, topics
and worldspaces rather than the eight hundred thousand records that contain
nothing.

Inference is roughly half of it. The deep scan is another order of magnitude —
Skyrim.esm goes from half a minute to the better part of an hour — because it
reflects over every property of every record, and it is a discovery tool rather
than a better sweep.

## Still to do

- **Voice.** No record names a dialogue file. The path is built from the quest,
  the topic, the response and the voice type; `Sound/Voice/<plugin>/<voicetype>/
  <editorid>_<formid>.fuz`, with a `.lip` beside it. Deriving those is a feature
  of its own, and the only one of these gaps that hides tens of thousands of
  files.
- **`.wav` against `.xwm`.** Records name `.wav`; Skyrim SE ships `.xwm`. The
  asset type knows both extensions, so reconciling them belongs to the pass that
  reads the archives.
- **FaceGen and LOD.** `Meshes/Actors/Character/FaceGenData/...` and the
  `.btr`/`.bto` LOD meshes are named after a record's FormID, not by any field.
