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

## Where the animation actually lives

The role says what a mesh *is*. It does not say where its motion comes from, and
the two do not line up the way the names suggest.

These counts come from a different sweep from the rest of this document and have a
different denominator: **every NIF in the base-game archives, 22,240 of them**,
rather than the 17,670 a record names. No DLC archive was present on the install
they were taken from. They are not comparable with the tables above and are not a
correction to them — a mesh nothing names is still counted here.

### A Gamebryo animation is usually not a clip

Of the 1,234 meshes that classify as `AnimatedMesh`:

| Sequences carried | Meshes |
| ---: | ---: |
| none | **921** |
| one | 52 |
| two or three | 256 |
| four to ten | 5 |

The largest group carries **no named sequence at all**. They qualify as animated
because they hold time controllers, not a controller manager: a mill wheel that
turns, a texture that flips, an alpha that pulses. There is nothing to select and
no clip to name, so an FBX gets one stack or none.

At the other end, 261 carry more than one and the most on any single mesh is
seven, in `meshes/magic/invisfxhand01.nif`. Doors are prominent among them —
`volunruudrightaxedoor`, `seruinsdoortemple01` and `riftenrwdoorspecial01` each
carry four — but so does `dwesoulgemcontainer01`, and so does a user-interface
dome. **Whether the multi-sequence case belongs to doors specifically has not been
measured**; the naming is suggestive and that is all.

A controller manager and controller sequences always travel together: 1,288 meshes
have a manager and exactly 1,288 have sequences.

### A behaviour graph is often a trigger, not a source

**839 meshes carry a behaviour graph *and* their own controller sequences** — 69%
of the 1,210 that name a graph at all. Havok and Gamebryo are not alternatives per
asset; they routinely run in the same file.

What the graphs are is the point:

```
1stpersonelderscrollhandattach.nif  [1 seq] -> GenericBehaviors\StagesNoLoops\StagesNoLoops.hkx
dlc1protoswingingbridge.nif         [2 seq] -> GenericBehaviors\StagesNoLoops\StagesNoLoops.hkx
magicanomalyspawner.nif             [2 seq] -> GenericBehaviors\BlendBetweenStatesVariable\...
fxgreybeardshoutfaas.nif            [4 seq] -> GenericBehaviors\waitPlayIdleAway\...
werebear_transformation.nif         [1 seq] -> Magic\IdleOnLoad.hkx
fxsteamsphereskin.nif                       -> GenericBehaviors\Autoplay.hkx
```

`StagesNoLoops`, `waitPlayIdleAway`, `Autoplay`, `IdleOnLoad`,
`BlendBetweenStatesVariable` name *control patterns*, not animations. The graph is
a small state machine deciding **when** to play the sequences the mesh already
holds; the animation data never leaves the NIF. That is consistent with what a
prop project's cache entry looks like — a file list, no clips.

So "the animation is in the project" is true of some props and false of most.
Which it is has to be read off the mesh, not assumed from the role.

### A prop can be skinned, and then stops looking like a prop

119 meshes are skinned to an external skeleton **and** name a behaviour graph, and
they are not props in any ordinary sense:

```
meshes/actors/dlc01/sabrecat/dlc1sabrecat.nif       -> DLC01\SharedBehaviors\BlackreachCreatures\...
meshes/actors/dlc01/dragon/dragonpurplebloodwingl.nif -> UniqueBehaviors\DragonBloodWingL\...
meshes/actors/dwarvenspherecenturion/character assets/fxsteamsphereskin.nif -> GenericBehaviors\Autoplay.hkx
```

`NifRoles.Of` tests `HasExternalSkeleton` before `BehaviorGraph`, so every one of
them classifies as `SkinnedAttachment` and the role never admits it is
Havok-driven. That is 119 assets an exporter keyed on `NifRole` would fetch no
project for.

It also puts a question against the invariant above. *A behaviour graph resolves to
a registered project* was measured over meshes a record names, and *every one of
those is a prop*; a sabrecat naming `BlackreachCreatures` may still satisfy that —
"prop" there means how the cache registers the project, not what the mesh depicts —
but **it has not been checked**, and an exporter that decides where to fetch
animation from should not rely on it until it has been.

### And some meshes animate with no animation in them at all

The root block type is its own signal, and there are only three besides the usual
two. Over the same 22,240 archive meshes:

| Root | Meshes | Carries | Classifies as |
| --- | ---: | --- | --- |
| `BSFadeNode` | 18,432 | — | everything |
| `NiNode` | 3,357 | — | everything |
| `BSLeafAnimNode` | 287 | shapes 287, collision 120, skinned 4, sequences 4 | `StaticGeometry` 281 |
| `BSMasterParticleSystem` | 93 | shapes 1 | `Effect` **93 / 93** |
| `BSTreeNode` | 71 | shapes 71, collision 71, skinned 71 | `SkinnedMesh` **71 / 71** |

Two of the three decide the role outright, and the third gets 281 of 287. A root
type is a stronger predictor of what a mesh is than the record that names it.

The interesting pair is `BSLeafAnimNode` and `BSTreeNode` — pines and gum trees,
and cloth too: the Nightingale banners are `BSLeafAnimNode`. **Between them, 358
meshes animate in the engine and carry nothing that says so.** Not one names a
behaviour graph; four of the 358 hold a sequence. The motion is leaf sway, trunk
bend and cloth ripple, driven by the shader from the wind, and the only thing in
the file that asks for it is the class of the root block.

So the classifier calls 281 of them `StaticGeometry` and 71 `SkinnedMesh`, both
listed above as *complete on its own*. That is true of a round trip and false of the
question "does this move". The distinction matters for export in one narrow,
unforgiving way: such a mesh needs no animation exported, and **its root block type
has to come back exactly**, or a pine comes home as a `BSFadeNode` and the forest
stops moving. `FbxNodeType` in NIFBX carries the class for this reason, and keeps a
per-root `BSXFlags` table in which `BSTreeNode` is `0x8080E` where everything else
is `0x8000E`.

It holds. Four samples of each of the five root types were taken through
`NifToFbx` and back through `FbxToNif`, and **all twenty came back as the class
they went in as** — `BSLeafAnimNode`, `BSTreeNode` and `BSMasterParticleSystem`
included. The concern is real and the code already answers it.

Counting these, motion in this game comes from five places, and only two of them put
animation data in a file that can be exported:

| | Where the motion is |
| --- | --- |
| an actor's clips | the Havok project, named by RACE |
| a prop's clips | the Havok project, named by the mesh's BGED |
| a named sequence | the NIF, played by the engine or triggered by a generic graph |
| an always-on controller | the NIF, running forever |
| leaf and tree sway | nowhere — the shader, keyed on the root block type |

### What follows for a classifier

`NifRole` is one axis collapsing two independent facts. For classification that is
fine. For deciding what to export it is not, because the two questions have
different answers:

| | |
| --- | --- |
| **where the motion comes from** | a Havok project via RACE, a Havok project via BGED, the NIF's own sequences, an always-on controller, the shader via the root block type, or several of these at once |
| **what the geometry needs** | rigid nodes, a skin to bones the file carries, or a skin to a skeleton it does not |

Both are readable from `NifProfile` directly. Neither is recoverable from the role.

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
- **HavokProp** — the mesh names a project, and what the project contributes
  varies. For 839 of the 1,210 meshes that name one, the graph is a trigger and
  the animation is the mesh's own sequences; for the rest the motion is in the
  project and exporting the mesh alone silently drops it. A complete FBX is the
  mesh plus whatever the project actually holds, which means reading the project
  rather than assuming — see *Where the animation actually lives*.

The direction this leaves open is the reverse lookup: what a mesh refers to *in
turn* — its textures, and the meshes an addon node pulls in. That is a sweep of
the file rather than of the plugin, and it belongs beside this one.
