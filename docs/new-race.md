# What a new race needs

Every record a plugin has to carry for a new race, and every file those records
and the Havok project drag behind them. Measured against the five masters
(`Skyrim.esm` to `Dragonborn.esm`, winning overrides) and the extracted meshes,
with the wolf as the worked creature and the Nord as the worked playable race. Read
`asset-kinds.md` first: it establishes that a RACE is the only record that joins a
skeleton to a Havok project.

There are two kinds of new race, and the second is the first plus a great deal:

- a **creature** — a new animal, monster or construct: its own skeleton, behaviour
  graph and animations, worn by NPCs that are not people;
- a **playable race** — a humanoid on the human skeleton, which needs character
  generation, faces, and to be listed on every piece of armour in the game.

The game has 161 races: **10 playable, 151 not**, and every one of the 161 names a
behaviour graph. Variants of an existing race (vampire, DLC recolours) are a third,
cheap case: see §3.3.

## 1. The records of a creature

### 1.1 The RACE record

Which fields vanilla sets, over the 151 non-playable races and the 10 playable ones.
A struct the format always writes (`BodyData`, `MountData`, `Regen`, the clamps,
the skill boosts) counts as set everywhere and says nothing; the rest are choices.

| Field | Creatures | Playable | What it is |
| --- | ---: | ---: | --- |
| `SkeletalModel` | 151 | 10 | the skeleton `.nif`: bones, blend collision, ragdoll (`asset-kinds.md`) |
| `BehaviorGraph` | 151 | 10 | the Havok **project** `.hkx`; its stem is the project's name in the caches |
| `BodyPartData` | 151 | 10 | a BPTD: the body parts for targeting, dismemberment and gore |
| `Skin` | 148 | 10 | an ARMO worn under everything: the body itself (§1.2) |
| `Attacks` | 145 | 10 | attack data: the behaviour event each attack sends — the wolf has 12 |
| `ImpactDataSet` | 141 | 10 | an IPDS: what a hit on the creature looks and sounds like |
| `MaterialType` | 150 | 10 | an MATT: what the creature is made of, for impacts and ragdoll |
| `Keywords` | 147 | 10 | `ActorTypeAnimal`, `ActorTypeCreature`, `ActorTypeNPC` … — conditions test these |
| `OpenLootSound`, `CloseLootSound` | 135 | 10 | SNDRs for searching the corpse |
| `ActorEffect` | 109 | 10 | abilities and diseases the race always has — the wolf's `DiseaseRockjoint` |
| `Voices` | 151 | 10 | the voice type per sex |
| `UnarmedDamage`, `UnarmedReach` | 149–150 | 10 | its bite or claw |
| `Starting`, `BaseCarryWeight`, `BaseMass`, acceleration, turning | 151 | 10 | the physical and stat numbers |
| `Name` | 66 | 0 | shown in the UI; creatures mostly go without |
| `BaseMovementDefaultWalk`/`Run` | 74 / 72 | 0 | a movement type per role — **half the creatures set none** (§1.4) |
| `…Swim`, `…Fly`, `…Sneak`, `…Sprint` | 15, 6, 9, 2 | 0 | only where the creature does it |
| `MovementTypes` | 10 | 0 | per-race overrides of a movement type's speeds |
| `HeadData` | 28 | 10 | faces: head parts, presets, tints (§2) — what makes a humanoid |
| `DecapitateArmors`, `DecapitationFX` | 21 | 10 | the severed head and its blood spray |
| `MorphRace`, `ArmorRace` | 23, 8 | 0 | a variant borrowing another race's faces or armour (§3.3) |
| `FlightRadius`, `AimAngleTolerance` | 6, 47 | 0 | flyers; creatures that aim |

Flags matter as much as fields: `Playable`, `Child`, `Swims`, `Flies`,
`Walks`, `AllowPickpocket`, `NoKnockdowns`, `AllowPCDialogue` and the rest decide
what the engine lets the race do.

### 1.2 The body: skin, armour addon, footsteps

A creature's body is armour it can never take off:

```
RACE ── Skin ──▶ ARMO SkinWolf             (biped slot Body, Race = WolfRace)
                   └─ Armature ──▶ ARMA NakedWolfAA
                                     ├─ Race = WolfRace (+ AdditionalRaces)
                                     ├─ WorldModel ──▶ Actors/Canine/Character Assets Wolf/wolf.nif
                                     └─ FootstepSound ──▶ FSTS NPCWolfFootWalkFootstepSet
                                                            └─▶ FSTP × n  (tag → IPDS → sound)
```

Every colour variant is a skin of its own: the wolf's race is named by 4 ARMOs and 5
ARMAs (`SkinWolf`, `…Black`, `…Fire`, `…Summon`, and the vampire dog's), and an NPC
picks one through `WornArmor` — 18 of the 24 wolf NPCs override the race's skin.

**The footstep set is where a creature's sounds live.** Despite its name it maps
any animation event tag to a footstep record, and the wolf's lists its bark, breath,
howl and every attack. The addon carries it, not the race.

### 1.3 Sounds named from the animations

The other half of a creature's audio runs **backwards, from an asset to a record**.
14 of the wolf's 50 animation event names — `NPCWolfBark`, `NPCWolfAttackA`,
`NPCWolfBreatheRun` … — are sound descriptor (SNDR) editor ids, and across the game
**3,419 of the 3,941 clip events that carry a payload** (`SoundPlay.NPCChickenScratch`)
name one. So the plugin must hold SNDRs whose editor ids the animations already use;
no FormLink records the dependency, and a sweep of the plugin does not see it.

### 1.4 Movement types are named, not linked

Nothing links a creature to its movement types: the wolf's race names none, and 77 of
the 151 creatures name no walk. The behaviour graph declares `iState_<name>`
constants, and the engine finds the MOVT whose **name** (`MNAM`, not the editor id)
matches. A new creature needs a MOVT per `iState_` constant, named exactly so, with
speeds that match what its locomotion clips travel. How to choose them is HKSK's
`docs/speed-data.md` §7.

### 1.5 The rest of the creature's records

| Record | For | Wolf |
| --- | --- | --- |
| BPTD | body parts; its model is a skeleton `.nif` | shares the **dog's** `DogBodyPartData` |
| IPDS → IPCT, MATT | impacts on the creature | `FXMeleeBiteMediumImpactSet`: 9 impacts over 54 materials |
| VTYP | the voice type, for dialogue and shouts | `CrWolfVoice` |
| SPEL, MGEF | racial abilities and diseases | `DiseaseRockjoint` |
| MOVT | one per `iState_` constant (§1.4) | named by the graph |
| IDLE | idle trees conditioned on the race: killmoves, idles | `KillMoveWolfRoot`, `WolfNonCombatIdle` |
| CPTH | killmove camera paths conditioned on the race | one, `BasicCams` |

The idle trees also decide which of the creature's attacks the set data marks as
moving attacks (HKSK's `docs/animation-set-data.md` §6), and an attack in the RACE's
attack data has to send an event the graph handles.

### 1.6 The NPCs that wear it

A race is only ever seen through an NPC. The 24 wolf NPCs:

| Field | Set on | |
| --- | ---: | --- |
| Class (CLAS) | 24 | `EncClassAnimalPredator` |
| Voice (VTYP) | 23 | |
| Combat style (CSTY) | 22 | `csWolf` |
| Factions (FACT) | 21 | `CreatureFaction`, `PredatorFaction`, `WolfFaction` … |
| Death item (LVLI) | 21 | `DeathItemWolf` |
| Worn armour (ARMO) | 18 | the skin variant |
| Template (NPC_) | 17 | a base NPC the rest inherit from |
| Packages (PACK, or a FLST of them) | 12 | `DefaultPredatorPackageList` |

An audio template NPC (`AudioTemplateWolf`) carries the shared sound settings, and
**19 leveled lists** (LVLN) put wolves into encounter zones — which is how a creature
appears in the world without being placed by hand.

## 2. What a playable race adds

Measured on the Nord, whose race record links 128 records where the wolf's links 12.

- **`HeadData`, per sex** — the default head parts (HDPT: head, mouth, eyes and
  hair, 4 for the Nord man, and brows as well for the woman), 10 chargen presets (NPC_ records), 34 tint masks, 15 hair colours (CLFM),
  6 face detail texture sets (TXST). The game has 805 head parts and 796 of them
  name a **valid-race form list** (`HeadPartsNord`, `HeadPartsHumansOrcsandVampires`
  …): a new race has to be added to each list whose parts it should offer, or get its
  own.
- **Armour.** Every vanilla playable race is named by **561–579 of the armour
  addons**, in their `Race` or `AdditionalRaces`. A race missing from an addon shows
  nothing where that armour is worn. A new playable race either joins every addon —
  every addon in every armour mod too — or borrows an existing race's with
  `ArmorRace` (§3.3).
- Racial SPELs (`RaceNord`, `PowerNordBattleCry`), a voice type per sex, the
  decapitated head ARMO and its `BloodSprayDecap01` art object, the `SkinNaked`
  body with its body, hands and feet addons, and `DefaultBodyPartData`.
- **Everything conditioned on the race.** 1,325 NPCs, but also 87 dialogue responses
  and 45 topics, 7 quests, 9 form lists (`RacesHuman` …), 2 packages and a magic
  effect test for the Nord by name. None of it breaks for a new race; it simply does
  not include it.

## 3. The files

### 3.1 A creature's

For the wolf, following every record above and the Havok project:

| File | Named by | Wolf |
| --- | --- | --- |
| skeleton `.nif` | RACE `SkeletalModel`, BPTD model | `Character Assets Wolf/skeleton.nif` |
| body `.nif`, one per skin | ARMA `WorldModel` | `wolf.nif`, `wolfblack.nif`, `wolffire.nif`, `wolfred.nif` |
| textures | the body `.nif` | `Wolf.dds`, `Wolf_n.dds`, `Wolf_sk.dds` |
| project `.hkx` | RACE `BehaviorGraph` | `WolfProject.hkx` |
| character `.hkx` | the project | `Characters Wolf/Wolf.hkx` — lists **72 animations** |
| behaviour `.hkx` | the character, and each other | 4, two of them the shared quadruped graphs |
| rig `skeleton.hkx` | the character | the animation rig **and** the ragdoll (`asset-kinds.md`) |
| animation `.hkx` | the character | 72 |
| sounds | the SNDRs of §1.2–1.3 | `.wav`/`.xwm` under `Sound/FX` |
| scripts | NPCs, where they have them | 5 of the wolf NPCs carry one |

The body mesh must be weighted to bones the skeleton has — `SkeletonRules.CheckSkin`
tests it, and holds for 832 of vanilla's 835 skins — and the skeleton `.nif` must carry
the rig's bones, bar Havok's own `x_` bones (`SkeletonRules.CheckRig`).

### 3.2 The three caches

A creature's project has an entry in each of the merged caches beside the meshes, and
the game reads the merged files, falling back to the split form under
`animationdata/` only without them (HKSK's `docs/animation-data.md` §4.1), so every
creature a load order adds has to be in the same three files:

| Cache | The wolf's entry |
| --- | --- |
| `animationdatasinglefile.txt` | the 6 files, 110 clips, 72 root motions |
| `animationsetdatasinglefile.txt` | 1 set |
| `speeddatasinglefile.txt` | 1 speed key |

The root motion in the first exists in no Havok file — a Skyrim animation's root does
not move — and has to come from the animation import (HKSK's `docs/animation-data.md`).
`animgen` writes all three for a load order, adding a new creature as an actor
because its race names its project:

```
animgen <meshes> <Data> --plugin MyCreature.esp --project MyCreatureProject -o <out>
```

### 3.3 A playable race's

On top of the creature's list, the human skeleton and graph are reused, and what is
new is the head and body:

- head meshes and their `.tri` morph files, per sex, from the HDPTs;
- body, hands and feet meshes in `_0`/`_1` weight pairs, and their first-person
  counterparts (`1stpersonmalebody_0.nif` …);
- the face tint masks and detail textures of `HeadData`;
- **FaceGen for every NPC of the race**: `Meshes/Actors/Character/FaceGenData/
  FaceGeom/<plugin>/<form id>.nif` and the matching `FaceTint` texture, baked by the
  Creation Kit (the layout is the extracted game's; what an NPC without them looks
  like is not measured here);
- the decapitated head mesh.

**A variant is cheap.** Eight vanilla races borrow another's armour with `ArmorRace`,
and 23 another's faces with `MorphRace`: Miraak's race wears the Nord's 577 addons
while being named by 19 of its own, and `DA13AfflictedRace` wears the Breton's. A vampire or recoloured variant needs only the RACE and what differs.

## 4. Order of work

1. Skeleton `.nif`, rig and ragdoll `skeleton.hkx`, body mesh weighted to it.
2. The Havok project: character, behaviours, animations with their root motion.
3. The plugin: MOVTs named for the graph's `iState_` constants; SNDRs named for the
   animations' events; the footstep set; skin ARMO and ARMA; BPTD; the RACE; the NPCs,
   leveled lists and death items.
4. `animgen` over the load order, to put the project into the three caches.

`SKAssets.Authoring`'s `ImportCreature` does 1 to 4 from a template creature and FBX
files for whatever is replaced (`docs/authoring.md` §6).
5. Check: `SkeletonRules.CheckSkin` and `CheckRig`, HKSK's `ConsistencyReport`.

## 5. Traps

- **Two dependencies no FormLink records**: movement types by `MNAM` from the graph's
  constants, and sound descriptors by editor id from the animations' events. Renaming
  either record silently breaks the creature.
- **Sounds hang off the armour addon**, through its footstep set, not off the race.
- **A body part data can be borrowed** — the wolf uses the dog's — so one per
  creature is not required, only one whose model names bones the skeleton has.
- **The caches are merged files.** Two creature mods each shipping their own
  `animationdatasinglefile.txt` overwrite each other; the files have to be written for
  the whole load order.
- **The race's graph path is the project's name.** `Actors\Canine\WolfProject.hkx`
  makes `WolfProject` the actor whose entries the caches hold, and it is what tells an
  actor from a prop: 247 props have animations as well.
