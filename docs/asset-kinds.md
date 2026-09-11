# What a mesh is, and what it drags behind it

Skyrim's meshes are not one kind of file with optional parts. They fall into a
handful of shapes that are built differently, animate differently, and need
different companion files — and **24% of the meshes the game's records name
cannot be read, animated or exported from the mesh alone**.

This is the evidence for that, and the model built on it. Everything here was
measured against the shipped game: 17,670 meshes, every one named by a record in
the five masters or the resource pack, read out of the archives and joined to the
records that name them and to the Havok projects they belong to.

## The eight shapes

| Role | Meshes | What it is | Complete on its own |
| --- | ---: | --- | :---: |
| **StaticGeometry** | 11,519 | Geometry, with collision or without | yes |
| **SkinnedAttachment** | 3,201 | A skin over a skeleton in another file | **no** |
| **AnimatedMesh** | 1,137 | Gamebryo controllers, animation inside the file | yes |
| **HavokProp** | 987 | Hands its animation to a Havok project | **no** |
| **Effect** | 393 | Particles, usually with nothing drawn | yes |
| **SkinnedMesh** | 262 | Skinned to bones it carries itself | yes |
| **CameraPath** | 98 | Nodes and controllers, nothing drawn | yes |
| **ActorSkeleton** | 52 | A tree of bones with blend collision and ragdoll | **no** |

21 meshes classify as nothing at all: markers and muzzle-flash placeholders with
no geometry, no particles and no controller. They are named by IPCT, MSTT and
PROJ records, and they are as empty as they look.

The order in which the tests are applied is itself a claim, because the shapes
overlap — 100 armour meshes are both a skin and a Havok prop. A skeleton is a
skeleton whatever else it holds; a skin over bones it does not carry cannot be
read at all without them, which matters more than what animates it; only then
does the animation source decide. `NifRoles.Of` runs in that order and says why.

## What each record names

The join is the point: a record type is a strong predictor of a mesh's shape, and
where it is not, the exceptions are worth knowing.

| Record | Meshes | Overwhelmingly |
| --- | ---: | --- |
| STAT | 9,814 | StaticGeometry 94%, and 395 animated ones |
| ARMA | 2,761 | SkinnedAttachment 99% |
| ACTI | 891 | HavokProp 55%, StaticGeometry 35% |
| MSTT | 683 | AnimatedMesh 41%, StaticGeometry 22%, Effect 19% |
| HDPT | 474 | SkinnedAttachment **100%** |
| ARTO | 297 | HavokProp 85% |
| DOOR | 222 | AnimatedMesh 83% — doors are Gamebryo, not Havok |
| CAMS | 76 | CameraPath **100%** |
| ADDN | 92 | Effect **100%** |
| RACE | 54 | ActorSkeleton 52 of 54 |
| BPTD | 43 | ActorSkeleton **100%** |

DOOR is the one that surprises: 185 of 222 doors animate through a controller
manager in the mesh, and only 6 hand the job to Havok.

## The two multi-file assets

### An actor

A RACE record is the only place the two halves meet, and **all 204 races in the
game name both**:

```
RACE ──── SkeletalModel ────▶ skeleton.nif      (ActorSkeleton: bones, blend collision, ragdoll)
     └─── BehaviorGraph ────▶ <name>project.hkx (an actor project in the cache)
                                    │
                                    ├── characters/<name>.hkx   the rig, the graph, the animation list
                                    ├── behaviors/*.hkx         clip generators
                                    ├── characterassets/skeleton.hkx   rig + ragdoll skeletons
                                    └── animationdatasinglefile.txt    clips, root motion, events

ARMA / HDPT ─ WorldModel ───▶ armour.nif        (SkinnedAttachment, weighted to the skeleton's bones by name)
```

Every one of the 204 resolves to a project the animation cache lists. A mesh
never names an actor project — only a RACE does.

**A skeleton.hkx holds two skeletons, not one.** The first is the animation rig
(`NPC Root [Root]`); the second is the ragdoll (`Ragdoll_*`), whose bones are
named for the ragdoll and map onto the mesh through collision objects rather than
by name. Comparing a skeleton mesh against both at once makes every actor in the
game look broken.

### A prop

Here the mesh names the project itself, through `BSBehaviorGraphExtraData`:

```
ACTI / STAT / ARTO / FURN ──▶ windmill.nif
                                    │  BSBehaviorGraphExtraData
                                    ▼
                              Architecture/Farmhouse/FarmhouseWindMill/FarmhouseWindMill.hkx
                                    ├── behaviors/behavior00.hkx
                                    ├── characters/character00.hkx
                                    ├── characterassets/singleboneskeleton.hkx
                                    └── animationdatasinglefile.txt entry (file list, no clips)
```

**A mesh's behaviour graph always names a prop project.** 1,086 of the 1,102
meshes that name one resolve to a project in the cache, and every one of those is
a prop. The other 16 point at Creation Club content this install does not carry,
which is what a missing project usually means rather than a broken mesh.

The path is spelled the way a record spells a model: relative to `Meshes`. The
project's name is the stem of its own file, which is what ties the two together.

## The invariants

Each was measured before it was written, and each carries what it scored. A rule
the game breaks in quantity describes the checker rather than the format.

| Rule | Holds in vanilla |
| --- | --- |
| `BSXFlags` matches what the block graph says | 12,491 / 12,509 |
| A head part is a skin over an external skeleton | 474 / 474 |
| An armour mesh, when skinned, skins externally | 2,722 / 2,722 |
| A RACE or BPTD mesh is a skeleton | 95 / 97 |
| A CAMS mesh carries no geometry | 76 / 76 |
| An ADDN mesh carries no geometry | 91 / 92 |
| A behaviour graph resolves to a registered project | 1,086 / 1,102 |
| **A skin's bones exist in its race's skeleton** | **832 / 835** |
| **A rig's bones exist in the skeleton mesh** | **199 / 204** |

The last two are the ones that find real bugs, because they are the only checks
that compare two files. Of the three armour meshes that fail, one is a Falmer
helmet weighted to human bones — it loads, and hangs in the air.

The two RACE meshes that are not skeletons are worth knowing by name, because
both are traps:

- `Actors/Draugr/Character Assets/DLC01/SkeletonWarrior.nif` is a *skinned
  attachment* — a draugr body, named for the undead rather than for a rig. Its
  name is the only thing about it that suggests a skeleton.
- `Effects/FXEmptyObject.nif` is the game's universal placeholder: no geometry,
  no anything, named by ACTI, ARMA, ARTO, DOOR, EXPL, HAZD, PROJ **and** RACE.
  Any rule keyed on the record type meets it eventually.

Two exceptions are rules of their own rather than failures:

- **`x_`-prefixed bones are Havok's.** Every actor rig declares `x_NPC LookNode`,
  `x_NPC Translate` and `x_NPC Rotate`, and no skeleton mesh carries any of them.
  Excluding the prefix takes the rig check from 131/204 to 199/204.
- **A skeleton mesh carries nodes the rig never mentions** — weapon and shield
  mounts, magic nodes, camera attachments: a median of four, and up to 67. The
  containment only runs one way.

## Traps

**The split animation cache is stale.** `animationdata/` lists 324 projects where
`animationdatasinglefile.txt` has 429, and every Dragonborn prop is missing from
it. Building a project index from the folder listing reports perfectly good
Dragonborn meshes as pointing at nothing — which is exactly what happened here
before HKSK's own warning was taken seriously. Read the merged file.

**A prop project is not under `actors/`.** It sits beside the mesh it animates:
`meshes/architecture/farmhouse/farmhousewindmill/farmhousewindmill.hkx`, with
`behaviors/`, `characters/` and `characterassets/` around it. Only the 49 actor
projects live under `meshes/actors/`.

**A file list is relative to its project.** `character assets/skeleton.hkx` names
188 different files across the 429 projects; the folder is what disambiguates.

## What this means for FBX

The point of classifying a mesh is knowing what a converter has to be given, and
what it has to be checked against. By role:

- **StaticGeometry, AnimatedMesh, SkinnedMesh, Effect, CameraPath** — the mesh is
  the asset. A round trip through FBX is complete when the mesh comes back.
- **SkinnedAttachment** — the bind pose references bones that are not in the
  file. An export without the skeleton produces an armature of orphan bones, and
  an import that invents bone names produces a mesh that loads and does not
  deform. The skeleton comes from the race, through the record, and the names
  must match exactly. This is what `SkeletonRules.CheckSkin` checks before the
  conversion rather than after it.
- **ActorSkeleton** — the mesh is half of a pair. The rig in the project's
  `skeleton.hkx` decides which bones animations drive, the mesh decides what the
  skin binds to, and they have to agree by name. A round trip that renames or
  re-cases a bone breaks every skin and every animation that referenced it.
- **HavokProp** — the animation is not in the mesh at all, so exporting the mesh
  alone silently drops the motion. The clips are in the project, which HKSK reads
  and HKFBX converts; a complete FBX for a windmill is the mesh plus the
  project's clips, and an import has to put both back.

The direction this leaves open is the reverse lookup: what a mesh refers to *in
turn* — its textures, and the meshes an addon node pulls in. That is a sweep of
the file rather than of the plugin, and it belongs beside this one.
