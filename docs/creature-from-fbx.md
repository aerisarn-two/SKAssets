# A creature from FBX to the game, with its behaviour assembled

    Status:   GUIDE over measured ground. The copy route (§1) is built and tested
              against the game; the assembly route (§2) is designed in HKSK and
              not yet built. Every step says which.
    Reads:    docs/new-race.md (the records), docs/authoring.md §6 (the copy
              route), docs/skeleton-exchange.md and docs/animation-export.md (the
              FBX conventions); in HKSK, docs/behavior-assembly.md (the assembly
              API and what a graph is made of), docs/creature-patterns-research.md
              (the census behind it), docs/animation-data.md, docs/speed-data.md,
              docs/animation-set-data.md (the caches), docs/paired-animations.md
              (kill-moves and mounts)

## 0. Two routes, and when each applies

A creature is a skeleton, a Havok project and a dozen records. The game holds 49
projects that carry a behaviour, and the behaviour is the part nobody writes from
nothing -- so there are two ways to get one:

| | **copy a creature** (`ImportCreature`, built) | **assemble a behaviour** (`HKSK.Assembly`, designed) |
| --- | --- | --- |
| start from | a template race the game has | the animations you have, with roles |
| the graph | the template's, copied and renamed | written from templates per plan and module |
| the animations | must replace the template's by name, one for one | any set at or above the floor (seven) |
| the skeleton | any, if the rig is exchanged whole | any |
| fits when | the new creature *is* a wolf, a draugr, a chicken with a new skin and re-animated clips | the creature's animation set matches no shipped creature: fewer clips, more attacks, a different plan |
| costs | nothing the template did not already pay | the plugin side has to be authored against the names the assembly reports |

The first route is `docs/authoring.md` §6 and is not repeated here beyond where
the two share a step. The rest of this document is the second route, end to end,
and it is written so that the same call sequence works for the first with the
graph step swapped.

## 1. What you bring

**Files.**

- **the skeleton**, one FBX as `SKAssets.Export` writes it (`docs/skeleton-exchange.md`):
  the rig, the ragdoll bodies and constraints with the `sk_` properties, and the
  skeleton mesh. Bone names are the contract for everything below;
- **the body**, one FBX per model slot, skinned to those bones;
- **the animations**, FBX files with one stack per clip over the same rig. A stack's
  name is the animation's file stem (`WalkForward`, `Attack1`). A clip that should
  move the creature has its travel on the root bone, which the import takes off the
  root and into the cache (`docs/animation-export.md`, "Root motion");
- **sounds**, `.wav` or `.xwm`, one per animation event that should play one.

**Decisions.** The creature's name; what each animation *is* (§4); the eight speeds
per movement type; the attack list; which shipped creature to borrow the non-graph
records from (§6).

## 2. The skeleton and the ragdoll

Built. `SKAssets.Export`'s skeleton exchange turns the FBX into three things:

| written | from | used by |
| --- | --- | --- |
| `Character Assets/skeleton.nif` | the mesh and the bone hierarchy | the RACE's `SkeletalModel`, the BPTD's model |
| `Character Assets/skeleton.hkx` | the rig | the character file's `rigName`; every animation is bound to it |
| the ragdoll in the same `.hkx` | the `sk_`-tagged bodies and constraints | the character file's `ragdollName`; the shell's ragdoll modifiers |

Check with `SkeletonRules.CheckRig` (the `.nif` carries the rig's bones bar Havok's
`x_` ones) and, after the body, `SkeletonRules.CheckSkin`.

What the assembly will ask of the skeleton, all by bone name and all optional
(HKSK `docs/behavior-assembly.md` §2.2):

| for | bones | shipped values, which are the defaults |
| --- | --- | --- |
| the get-up pose matcher | two bones beside the root; the shipped ones are template leftovers, any symmetric pair serves | root and pelvis are bone 0 |
| head tracking | the spine-to-head chain, three to five bones | limit 65°, gains 0.075 / 0.05 |
| foot IK | hip, knee, ankle per leg, and the knee axis | ankle heights read from the rest pose; four legs set `isQuadrupedNarrow` |
| partial-body casting | the bones the upper body starts at | a bone mask, inline; no character property |

## 3. The body

Built. The body FBX goes through the armour import into a skin: an ARMO with slot
Body, its ARMA naming the race, the `WorldModel` the mesh, textures to DDS beside
it. The BPTD is copied from the kin race (§6) because its model names bones, and
must name bones this skeleton has. The footstep set hangs off the addon, and it is
where the creature's sounds will live.

## 4. The animations, with roles

Built for the import; the roles are the assembly's input.

`SKAssets.Export`'s clip exchange writes each stack as an uncompressed animation in
the project's folder and puts its travel into the cache as root motion. Then each
animation is given **roles** -- what it is, not what it is called, because the
shipped names agree on nothing (`MTForward`, `WalkForward`, `Forward_Walk`, `RunF`):

```csharp
var animations = new List<RoledAnimation>
{
    new("Animations/Idle.hkx",          [new(RoleKind.Idle)]),
    new("Animations/WalkForward.hkx",   [new(RoleKind.Walk, Heading.Forward)]),
    new("Animations/RunForward.hkx",    [new(RoleKind.Run,  Heading.Forward)]),
    new("Animations/TurnLeft.hkx",      [new(RoleKind.TurnInPlace, Side.Left),
                                         new(RoleKind.TurnInPlace, Side.Right, Mirror: true)]),
    new("Animations/Attack1.hkx",       [new(RoleKind.Attack, Name: "attackStart_Bite")],
                                        Events: [new("HitFrame", 0.40f), new("preHitFrame", 0.25f)]),
    new("Animations/Recoil.hkx",        [new(RoleKind.Recoil)]),
    new("Animations/StaggerSmall.hkx",  [new(RoleKind.Stagger, Magnitude: Magnitude.Small)]),
    new("Animations/StaggerLarge.hkx",  [new(RoleKind.Stagger, Magnitude: Magnitude.Large)]),
    new("Animations/Death.hkx",         [new(RoleKind.Death)]),
    new("Animations/GetUp.hkx",         [new(RoleKind.GetUp), new(RoleKind.Reanimate)]),
};
```

`BehaviorAssembler.GuessRoles(paths)` proposes this from the file names with the
census's token rules (`idle`, `forward`, `left`, `walk`, `run`, `attack`, `power`,
`stagger`, `recoil`, `getup`, `death` ...) for a front end to show and correct; it is
never used unreviewed.

**Which animations need travel** is decided by how the engine moves an actor (HKSK
`docs/animation-data.md` §4.3, `docs/behavior-assembly.md` §4.2): by the cache's
movement block, per frame, in animation-driven mode, and by the controller's
velocity otherwise. So:

| role | root track |
| --- | --- |
| walk, run, trot, sprint, swim, turn loops | **required** -- not to move (that is the controller) but because the speed table and the ladder rungs are computed from it |
| attack, power attack, canned turn, bash, get-up, death, stagger, recoil, aggro, kill-move victim | **required where the clip should carry the actor**; combat measures an attack's reach from it |
| idles, combat idle, equip, unequip, block, feed, lay | none |
| cruise, hover idle (flyers) | none; flight is motion-driven |

A locomotion clip authored without travel records a creature that cannot move.
`Plan` reports each such slot.

## 5. The behaviour, assembled

Designed (HKSK `docs/behavior-assembly.md` §5). Two calls, the first pure:

```csharp
var spec = new CreatureSpec
{
    Name = "Direwolf",
    SkeletonPath = "…/Character Assets/skeleton.hkx",
    RagdollPath  = "…/Character Assets/skeleton.hkx",
    Animations = animations,
    Bones = new SkeletonRoles
    {
        LookAtChain = ["Spine2", "Neck1", "Neck2", "Head"],
        Legs = [new("LFrontLeg1", "LFrontLeg2", "LFrontLegToe", KneeAxis: new(1, 0, 0)), /* … */],
    },
    Movements = new Dictionary<MovementRole, MovementType>
    {
        [MovementRole.Walk] = walk,   // eight speeds; becomes MOVT "DirewolfDefault" and iState_DirewolfDefault
        [MovementRole.Run]  = run,
    },
    AttackEvents = ["attackStart_Bite"],
};

AssemblyPlan plan = BehaviorAssembler.Plan(spec);       // nothing written
// plan.Plan      -> LocomotionPlan.Quadruped (forward walk with left/right turn variants)
// plan.Modules   -> { Attacks, Recoil, Stagger, AnimatedDeath, GetUp, Reanimate, Idles }
// plan.Slots     -> every role, filled, reused or empty
// plan.Warnings  -> "WalkForward carries no root track", "no canned turns: cannedTurn* events will be ignored"
// plan.IStates   -> { iState_DirewolfDefault: 0 }
// plan.Events    -> the core, plus attackStart_Bite, plus the modules'

AssembledProject built = BehaviorAssembler.Assemble(cache, spec, group: "Canine");
```

What `Assemble` writes, and what each block is (HKSK
`docs/creature-patterns-research.md` for the measurements behind every one):

| block | what it is | from |
| --- | --- | --- |
| **root machine and ragdoll shell** | one live state; animate-to-ragdoll if a death clip was given, else `Ragdoll` straight in; fully-ragdoll; get-up with the pose matchers over the get-up clips, selected by `iGetUpType` | the same nodes in all 46 shipped creatures |
| **root modifier list** | keyframe bones and ragdoll drive; the speed sampler bound `iState`, `Direction`, `Speed`, `SpeedSampled`; look-at over the chain; foot-IK controls; get-up; the draw/sheathe expressions if there is a stance | same |
| **situation machine** | `DefaultState` first; stagger, recoil, attacking, and each module as a state; wildcards `staggerStart`, `recoilStart`, `bleedOutStart`, `aggroWarningStart` … | the census's situation table |
| **locomotion plan** | chosen from the roles: compass biped (8 headings), four-arm (4), quadruped (forward gaits with left/right turn blends and a separate backward), forward only, or swimmer; a standing machine (idle + turns), the moving generator, canned turns if given | the five plans |
| **combat stance** | if a combat idle was given: the standing and moving parts repeated under a readied state, entered by `weapEquip` / `combatStanceStart`, with equip and unequip transition clips if given | 33 creatures |
| **attacks** | one state per attack, entered by its event, `bAllowRotation` raised, exit on `attackStop`, `recoilStart` to the recoil state; triggers at the times given | 42 creatures |
| **modules** | swim, aggro warning, bleed-out, bash, block, idles, lay-down, feed, kill-move victim, flight, perch, upper-body cast -- each present iff its roles are | measured each |
| **declarations** | the engine's variables with the shipped initial values; `blendDefault` 0.2, `blendFast` 0.1, `blendSlow` 0.5; `iState_<MNAM>` per movement type; the base machine's sync variable; the event core plus one per module and per attack | `docs/animation-variables.md` |
| **the character file** | `rigName`, `ragdollName`, `behaviorFilename`, the animation list **in the order the roles were given** -- that order is every clip's cache index | `README.md` (HKSK) |
| **the cache rows** | the project's block in the animation data with every clip, speed, crops, events and root motion; its one set in the set data; its speed block from the ladders and the movement type | the three generators HKSK already has |

Names the plugin has to agree with come back on the plan: `plan.IStates` (one
`MOVT` per key, its `MNAM` the suffix after `iState_`), `plan.Events` filtered to the
`attackStart_*` (the race's attack data) and `idle*Start` names (idle records), and
the sound events the triggers carry.

## 6. The plugin

Built for the copy route; the assembly route needs the same records, and
`SKAssets.Authoring` writes them the same way from a **kin** race -- the shipped
creature whose non-graph records are borrowed. Every record `docs/new-race.md` §1
lists, and where each comes from:

| record | from the kin | from the assembly | from you |
| --- | --- | --- | --- |
| `RACE` | keywords, material, impact set, voice, loot sounds, abilities, unarmed reach and damage, the physical numbers, flags (`Walks`, `Swims`, `Flies`) | `BehaviorGraph` = the project path; `Attacks` = one entry per `plan.Events` attack, its event name the entry's; `BaseMovementDefault*` = the movement types by role | `Name`, `SkeletalModel` (§2), `Skin` (§3) |
| `MOVT` | -- | one per `plan.IStates` key, `MNAM` exactly the suffix, the eight speeds from `spec.Movements` | the speeds |
| `BPTD` | copied, its model the new skeleton `.nif` | -- | -- |
| `ARMO` / `ARMA` | the skin's shape | -- | the body (§3) |
| `FSTS` / `FSTP` / `IPDS` / `SNDR` | the kin's set copied onto the body | the events the triggers carry | the audio files per event, as `Sounds` |
| `IDLE` | the kin's non-combat idle root and its conditions, re-conditioned on the new race | one per `idle*Start` event the assembly declared, its `ENAM` that event; for each kill-move victim state, the paired idle's `pa_` event is the killer's and needs no record on this side | which idles the AI may pick |
| `CSTY`, `CLAS`, `FACT`, `LVLI`, `PACK` | copied | -- | -- |
| `NPC_` | a copy of the kin's `Npc` with the new race, skin, class, style, factions, death item | -- | which |
| `LVLN` | -- | -- | the leveled lists that put it in the world (`AddToLeveledList`) |

Two dependencies no FormLink records, and the assembly makes both explicit: the
movement types by `MNAM` from the graph's constants, and the sound descriptors by
editor id from the animations' events. Both come off the plan.

The proposed call, a sibling of `ImportCreature` with the graph step swapped:

```csharp
CreatureResult built = authoring.AssembleCreature(new NewCreatureFromRoles
{
    Kin = "WolfRace",                      // records borrowed; no Havok file of it is used
    Name = "Direwolf",
    SourceMeshes = extractedMeshes,        // the three caches; the assembly adds a project
    Skeleton = "direwolf_skeleton.fbx",
    Body = new() { [ModelSlot.Main] = "direwolf.fbx" },
    Animations = animations,               // RoledAnimation, as §4; FBX paths import first
    Bones = bones,
    Speeds = new() { ["DirewolfDefault"] = MovementSpeeds.Uniform(150, 450) },
    Sounds = new() { ["NPCDirewolfBark"] = ["bark.wav"] },
    Npc = "EncWolf",
});
```

## 7. The caches and the load order

Built. The three merged files under `Meshes/` are one per load order: the assembly
added the project's rows to the copies in the output, and a second creature mod's
have to be rebuilt together with them. `animgen` does that over a `Data` folder:

    animgen <meshes> <Data> --plugin MyCreature.esp --project MyMod_DirewolfProject -o <out>

The set data is rebuilt from the graph and the plugin's idle and attack events, and
the speed table from the graph's ladders and the `MOVT`s -- which is why the `MNAM`
names have to be right before this step, not after.

## 8. Checks, before the game and in it

Before:

- `SkeletonRules.CheckRig`, `SkeletonRules.CheckSkin` (SKAssets);
- HKSK `ConsistencyReport` over the new project: every clip's cache index is its
  animation's position, speed and crops agree, every generator is cached;
- the census reading of the assembled project (`ZzCreatureCensus` in HKSK's tests)
  classifies it as the plan it was given with the modules it was given;
- HKSK's engine evaluator, driven with `moveStart` and a `Direction`, lands in the
  locomotion state and samples the movement type's speeds; driven with each
  `attackStart_*`, enters that attack and leaves on `attackStop`;
- the plan's warnings are empty, or each one is a choice.

In the game, with the NPC placed or spawned by console, in this order, because
each failure hides the next:

1. it stands and idles (the shell, the character file, the caches were found);
2. it walks and runs toward a target at plausible speeds (the sampler, the `MOVT`
   names, the speed block);
3. it turns in place and takes canned turns (the standing machine);
4. it attacks and the hit lands (`attackStart_*` from the race's attack data, the
   attacking state, `HitFrame` in the clip);
5. it staggers and recoils when hit (the wildcards);
6. it dies and ragdolls; a reanimated one gets up (the shell, `GetUpStart`);
7. it barks, or whatever it does (the footstep set by event name);
8. a humanoid kill-moves it, if victim states were given (`docs/paired-animations.md`).

## 9. Traps, collected

- **Names match by spelling, not by case**, both events and variables: the engine
  interns them through a case-insensitive pool (HKSK `docs/animation-events.md`
  §1). A typo is not caught by case.
- **The movement type's `MNAM` is the suffix of `iState_<MNAM>`**, not its editor id,
  and the speed table is generated from the record, so the record comes first.
- **A sound is found by the SNDR's editor id**, which is the animation event's
  payload. Renaming either side silently mutes the creature.
- **Root motion lives in the cache and nowhere else.** An animation re-imported
  without its travel walks on the spot; a cache regenerated without the animation's
  new travel moves the creature one way and measures it another.
- **The character file's list is the cache index.** Append, never insert; the
  assembly writes it in role order and every clip's number is its position.
- **A kill-move victim listens for the bare name**, `KillMoveBearA`, while the
  killer's graph listens for `pa_KillMoveBearA`: the engine toggles the prefix and
  starts the pair only if both graphs declare their form.
- **Two creature mods overwrite each other's caches** unless rebuilt together.
- **The paths in the archives use `/`** and the records `\`; compare normalised.
