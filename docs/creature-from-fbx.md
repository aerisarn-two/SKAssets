# A creature from FBX to the game, with its behaviour assembled

    Status:   GUIDE over measured ground. The copy route (§0, docs/authoring.md
              §6) is built and checked against the game's files, not yet in a
              running game; the assembly route (§4–§6) is designed in HKSK and
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
actor projects -- the ones a race wears and the caches carry -- and the behaviour
graph is the part nobody writes from nothing. So there are two ways to get one:

| | **copy a creature** (`ImportCreature`, built) | **assemble a behaviour** (`HKSK.Assembly`, designed) |
| --- | --- | --- |
| start from | a template race the game has | the animations you have, with roles |
| the graph | the template's, copied and renamed | written from templates per plan and module |
| the animations | replace the template's by name; one that matches none is listed but no clip plays it until the graph is amended, since the graph is the template's | any set at or above the floor (seven) |
| the skeleton | any: a rig with the template's bones is written over it, a rig of its own is built from it and every bone the copied graph named by index is found again by name (`RigRemap`) | any |
| fits when | the new creature *is* a wolf, a draugr, a chicken with a new skin and re-animated clips | the creature's animation set matches no shipped creature: fewer clips, more attacks, a different plan |
| costs | nothing the template did not already pay | the graph template writer, and three record fields written from the plan instead of copied (§6); everything else -- the skeleton, the mesh to NIF with its textures, the records, the sounds, the caches -- is `ImportCreature`'s code as it stands |

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

**Decisions.** The creature's name; what each animation *is* (§4); the names of its
movement types and which roles share one -- their speeds are read off the clips,
not decided (§5); the attack list; which shipped creature to borrow the non-graph
records from (§6).

## 2. The skeleton and the ragdoll

Built. `SKAssets.Export`'s skeleton exchange turns the FBX into three things:

| written | from | used by |
| --- | --- | --- |
| `Character Assets/skeleton.nif` | the mesh and the bone hierarchy | the RACE's `SkeletalModel`, the BPTD's model |
| `Character Assets/skeleton.hkx` | the rig | the character file's `rigName`; every animation is bound to it |
| the ragdoll in the same `.hkx` | the `sk_`-tagged bodies and constraints | the character file's `ragdollName`; the shell's ragdoll modifiers |

Check with `SkeletonRules.CheckRig` (the `.nif` carries the rig's bones bar Havok's
`x_` ones) and, after the body, `SkeletonRules.CheckSkin`. The animations are bound
to this rig by bone name, so every stack should drive bones the rig has:
`ClipReport.Inert` lists stacks that bound none, and a stack that binds only some is
a clip that animates only some.

What the assembly will ask of the skeleton, all by bone name and all optional
(HKSK `docs/behavior-assembly.md` §2.2):

| for | bones | shipped values, which are the defaults |
| --- | --- | --- |
| the get-up pose matcher | two bones beside the root; the shipped ones are template leftovers, any symmetric pair serves | root and pelvis are bone 0 |
| head tracking | the spine-to-head chain, three to five bones | limit 65°, gains 0.075 / 0.05 |
| foot IK | hip, knee, ankle per leg, and the knee axis | ankle heights read from the rest pose; four legs set `isQuadrupedNarrow` |
| partial-body casting | the bones the upper body starts at | a bone mask, inline; no character property |

### 2.1 A rig of its own

A creature whose FBX carries the template's bones and bodies is written over the
template's `skeleton.hkx` by name, which keeps every value neither converter models.
A creature with a rig of its own -- another bone count, another ragdoll -- cannot be:
the counts are what the skeleton mappers, the ragdoll instance and every index in the
file are built over. `HkxSkeletonBuilder` rebuilds the file around the new rig from the
template's objects as prototypes, and `RigRemap` rebinds what the copied character and
behaviours name by index, by bone name:

| what names a bone | in | mapped |
| --- | --- | --- |
| the foot IK's hip, knee and ankle per leg | the character file | by name; the knee axis and the ankle heights re-derived from the new rest pose |
| the mirror table | the character file | rebuilt from the new rig's own left/right spellings |
| a look-at's bones | the shared graph | by name, dropping what the new rig has not got; each forward axis re-derived |
| the bones a get-up keyframes, a powered ragdoll drives, a contact listener watches | the graphs | **ragdoll** bone indices, not rig ones |
| a get-up's and a pose matcher's three bones, a bone-switch's weights | the graphs | by name; a bone with no counterpart falls back to the root and is reported |
| a body part's `PartNode` and VATS target | the BPTD | by the same map |

The map from the template's names to the new rig's is `NewCreature.BoneMap`; bones
called the same need no entry.

## 3. The body

Built, and the same code on both routes. The body FBX goes through the armour
import (`PluginAuthoring.Import` with `AuthoredKind.Armor`, the kin's skin as the
template): NIFBX converts the mesh to a NIF under the creature's mesh folder, the
textures the FBX names are written as DDS beside it and the mesh pointed at them,
the mesh is checked against the record that names it (`MeshRules`), and the records
follow -- an ARMO with slot Body copied from the kin's skin, one ARMA per addon
copied and made to name the new race, the `WorldModel` the new mesh. The BPTD is
copied from the kin because its model names bones, and must name bones this
skeleton has. The footstep set hangs off the addon, and it is where the creature's
sounds will live (§6).

## 4. The animations, with roles

Built for the import; the roles are the assembly's input.

Each animation is given **roles** -- what it is, not what it is called, because the
shipped names agree on nothing (`MTForward`, `WalkForward`, `Forward_Walk`, `RunF`).
A roled animation names the FBX and the stack inside it; the role record is
positional, so its members are given by name:

```csharp
var animations = new List<RoledAnimation>
{
    new("clips.fbx", Stack: "Idle",         Roles: [new(RoleKind.Idle)]),
    new("clips.fbx", Stack: "WalkForward",  Roles: [new(RoleKind.Walk, Heading: Heading.Forward)]),
    new("clips.fbx", Stack: "RunForward",   Roles: [new(RoleKind.Run,  Heading: Heading.Forward)]),
    new("clips.fbx", Stack: "TurnLeft",     Roles: [new(RoleKind.TurnInPlace, Side: Side.Left),
                                                    new(RoleKind.TurnInPlace, Side: Side.Right, Mirror: true)]),
    new("clips.fbx", Stack: "Attack1",      Roles: [new(RoleKind.Attack, Name: "attackStart_Bite")],
                                            Events: [new("preHitFrame", 0.25f), new("HitFrame", 0.40f)]),
    new("clips.fbx", Stack: "Recoil",       Roles: [new(RoleKind.Recoil)]),
    new("clips.fbx", Stack: "StaggerSmall", Roles: [new(RoleKind.Stagger, Magnitude: Magnitude.Small)]),
    new("clips.fbx", Stack: "StaggerLarge", Roles: [new(RoleKind.Stagger, Magnitude: Magnitude.Large)]),
    new("clips.fbx", Stack: "Death",        Roles: [new(RoleKind.Death)]),
    new("clips.fbx", Stack: "GetUp",        Roles: [new(RoleKind.GetUp), new(RoleKind.Reanimate)]),
};
```

**The import happens inside `Assemble`, in this order, and the order is the
point.** The character file's animation list is every clip's cache index, so the
project has to exist before an animation can be added to it: `Assemble` creates the
project and an empty character file, then imports each roled animation in the order
given -- `ClipExchange.ImportClips` writes the stack as an uncompressed animation
under the project's `Animations`, `AddAnimation` appends it, its travel goes into
the cache row as root motion and its annotations become events -- and only then
writes the graph over the list. A Havok animation already on disk is added the same
way without the conversion. Nothing about the FBX has to be done beforehand.

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
| walk, run, trot, sprint, swim | **required** -- not to move (that is the controller) but because the speed table and the ladder rungs are computed from it |
| canned turn | **required, as rotation on the root**: the state is animation-driven and the turn is what the block carries |
| attack, power attack, bash, get-up, death, stagger, recoil, aggro, kill-move victim | **required where the clip should carry the actor**; combat measures an attack's reach from it |
| turn-in-place loops, idles, combat idle, equip, unequip, block, feed, lay | none: the controller turns the actor and the clip poses it |
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
    // A MOVT holds walk and run together; one iState per distinct name, and the race's
    // roles point at them. Only the names are decided: the eight speeds are read off
    // the walk and run clips' travel per heading (HKSK docs/speed-data.md §7.2), which
    // is how the shipped records were authored. Walk and run are one type here.
    MovementNames = new() { [MovementRole.Walk] = "DirewolfDefault", [MovementRole.Run] = "DirewolfDefault" },
    // MovementOverrides: only to force speeds the clips do not deliver; the clips then play scaled.
    // AttackEvents is derived from the Attack roles' names when left out.
};

AssemblyPlan plan = BehaviorAssembler.Plan(spec);       // nothing written
// plan.Plan      -> LocomotionPlan.Quadruped (forward walk with left/right turn variants)
// plan.Modules   -> { Attacks, Recoil, Stagger, AnimatedDeath, GetUp, Reanimate, Idles }
// plan.Slots     -> every role, filled, reused or empty
// plan.Warnings  -> "WalkForward carries no root track", "no canned turns: cannedTurn* events will be ignored"
// plan.IStates   -> { iState_DirewolfDefault: 0 }
// plan.Movements -> { DirewolfDefault: forward walk 148.2, run 452.7, back 0, sides 0, turn rates 180/270/360 }
//                   -- the MOVT to write, read off the clips; a heading with no clip reads 0
// plan.Events    -> the core, plus attackStart_Bite, plus the modules'

AssembledProject built = BehaviorAssembler.Assemble(cache, spec, group: "Canine");
// `cache` is the output's copy of the three merged files, loaded from SourceMeshes
// and saved to the output's Meshes; the project lands under Actors/Canine/<Name>/.
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

Built, in `ImportCreature`, and almost all of it carries over unchanged. What
`ImportCreature` does today, step by step, and what the assembly route changes:

| step in `ImportCreature` | today | on the assembly route |
| --- | --- | --- |
| the Havok files | the template's project, character, behaviours and animations copied beside it | **replaced**: `BehaviorAssembler.Assemble` writes them; the rig still comes from the skeleton FBX as below |
| the skeleton | FBX → `skeleton.nif` (`SkeletonExchange.ImportMesh`) and the rig with its ragdoll (`ImportHavok`; `HkxSkeletonFile.Write` when the FBX has the template's bones and bodies, `HkxSkeletonBuilder.Write` when it has its own), checked by `MeshRules` and by `CreatureChecks` against each other | same |
| movement types | the template's `iState_` constants renamed in every copied graph and its `MOVT`s copied under the new names with the speeds given | **changed**: one `MOVT` per `plan.Movements` entry, `MNAM` the name, the eight speeds as the plan read them off the clips, the turn rates by family, thresholds `FLT_MAX`; no renaming, since the graph was written with the names, and no speeds to give |
| the race | `RACE` duplicated; skeleton, project and movement defaults repointed | **changed in one field**: `Attacks` written from the plan's attack events instead of copied |
| the body | the armour import (§3): mesh to NIF, textures to DDS, ARMO and ARMA copied and re-raced | same |
| the body part data | copied when the skeleton is replaced | same |
| sounds | the footstep set copied onto the body; per event given, footstep, impact set, impact and sound copied and the files placed under `Sound\FX`; an event the set has no footstep for is refused | **changed**: the kin's set knows the kin's events (`NPCWolfBark`), not this creature's, so an event the triggers carry and the set lacks gets a footstep, impact set, impact and sound **added**, tagged with the event, instead of being refused |
| the NPC | copied, re-raced, re-skinned | same |
| the caches | the template's entry copied, the clips imported by `ClipExchange.ImportClips`, `CacheGeneration.Amend`, save | **changed**: the entry and the clips are the assembly's (§4); `Amend` and save as today |
| idle records | **the template's, copied onto the new behaviour** -- an idle serves the actors whose behaviour is the file it names, and a copied graph is a different file, so without copies the creature never receives `moveStart` (below) | **new**: one `IDLE` per `idle*Start` event the assembly declared, **linked into** a copy of the kin's non-combat idle root re-conditioned on the new race -- an idle record that hangs off no root's parent-and-sibling chain is never picked; and the kin's kill-move (`KillMove<Kin>Root`) and camera-path idles, which test the *victim's* race, copied and re-conditioned, or the creature is never kill-moved |

So the new code is the graph template writer in HKSK and, here, three substitutions
in a sibling of `ImportCreature`. The **kin** race is the shipped creature whose
non-graph records are borrowed. Every record `docs/new-race.md` §1 lists, and
where each comes from:

| record | from the kin | from the assembly | from you |
| --- | --- | --- | --- |
| `RACE` | keywords, material, impact set, voice, loot sounds, abilities, unarmed reach and damage, the physical numbers, flags (`Walks`, `Swims`, `Flies`) | `BehaviorGraph` = the project path; `Attacks` = one entry per `plan.Events` attack, its event name the entry's; `BaseMovementDefault*` = the movement types by role | `Name`, `SkeletalModel` (§2), `Skin` (§3) |
| `MOVT` | the turn rates, where the kin's family default is not wanted | one per `plan.Movements` entry, `MNAM` exactly the name, the eight speeds read off the clips, thresholds `FLT_MAX` | the names, and an override only where a clip's travel is not the speed wanted |
| `BPTD` | copied, its model the new skeleton `.nif` | -- | -- |
| `ARMO` / `ARMA` | the skin's shape | -- | the body (§3) |
| `FSTS` / `FSTP` / `IPDS` / `SNDR` | the kin's set copied onto the body, its entries kept for the kin's events the triggers still use | a footstep chain added per triggered event the set lacks, tagged with the event | the audio files per event, as `Sounds`; an event with no audio gets the chain with the kin's sound |
| `IDLE` | the non-combat root, the kill-move root and the camera paths, copied and re-conditioned on the new race | one per `idle*Start` event the assembly declared, its `ENAM` that event, chained under the copied root | which idles the AI may pick, and their conditions |
| `CSTY`, `CLAS`, `FACT`, `LVLI`, `PACK` | copied | -- | -- |
| `NPC_` | a copy of the kin's `Npc` with the new race, skin, class, style, factions, death item | -- | which |
| `LVLN` | -- | -- | the leveled lists that put it in the world (`AddToLeveledList`) |

Two dependencies no FormLink records, and the assembly makes both explicit: the
movement types by `MNAM` from the graph's constants, and the sound descriptors by
editor id from the animations' events. Both come off the plan.

**Kill-moves are not a role like the others.** A shipped kill-move is one paired
animation with two halves, the killer's on the human rig and the victim's on the
creature's, and the humanoid graph holds the killer's state for each one that
exists (`docs/paired-animations.md`, HKSK). So a new creature can be kill-moved in
exactly two cases: its rig is a shipped victim's rig, bone for bone, and it declares
the victim states for those animations under their shipped names; or a paired
animation is authored against both rigs, imported through HKSK's paired exchange,
and the killer's state is added to the humanoid graph as well as the victim's to the
creature's -- which is an edit of `0_master`, outside anything here. Absent either,
the `KillMoveVictim` roles are left out and the creature dies the ordinary way; the
kin's kill-move idles are then not copied either, since they would choose kill-moves
the pair cannot start.

The proposed call, a sibling of `ImportCreature` with the graph step swapped:

```csharp
CreatureResult built = authoring.AssembleCreature(new NewCreatureFromRoles
{
    Kin = "WolfRace",                      // records borrowed; no Havok file of it is used
    Name = "Direwolf",                     // files and records prefixed like every import's
    SourceMeshes = extractedMeshes,        // the game's three caches; copied to the output and amended
    Skeleton = "direwolf_skeleton.fbx",
    Body = new() { [ModelSlot.Main] = "direwolf.fbx" },
    Animations = animations,               // RoledAnimation, as §4; imported inside Assemble
    Bones = bones,
    MovementNames = new()                  // by role; each name becomes a MOVT and an iState_<name>,
    {                                      // its speeds read off the clips (plan.Movements)
        [MovementRole.Walk] = "DirewolfDefault",
        [MovementRole.Run]  = "DirewolfDefault",
    },
    // MovementOverrides = new() { ["DirewolfDefault"] = MovementSpeeds.Uniform(150, 450) },  // only to force it
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

Running the same request twice is safe for the caches and the records: an amend of
an entry that is already right changes nothing (HKSK `docs/animation-data.md` §3),
and the records are made by editor id. The files are overwritten. What is not
undone by a re-run is a name changed between runs: the old project, records and
cache rows stay, under the old name, beside the new.

## 8. Checks, before the game and in it

Before:

- `SkeletonRules.CheckRig`, `SkeletonRules.CheckSkin` (SKAssets);
- `CreatureChecks`, which `ImportCreature` runs and reports as findings: **the files
  against each other**, since each converter reads the FBX its own way and each file
  is correct on its own. The skeleton NIF's bones against the Havok rig's, the skin's
  bind pose against the skeleton's bones, the skin's triangles against the FBX's, every
  vertex weighted, every partition naming a biped slot, every bone carrying the sphere of
  what it moves, a worn shape hanging off the root, and every block the size its header
  says. Each of the eight was added because it had already gone wrong once on the cat:
  see the traps below and `docs/case-study-house-sabre-cat.md`;
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
- **A `MOVT` name is global.** `MNAM` is matched by name across the load order, so a
  new creature's types carry the prefix like its records; `DogDefault` would find
  the dog's.
- **A kill-move victim needs the killer's half too**, in the humanoid graph, and a
  paired animation authored for both rigs; a new rig cannot borrow the wolf's.
- **The paths in the archives use `/`** and the records `\`; compare normalised.
- **An idle record serves the behaviour file it names, and names it from Meshes.**
  The sabre cat's 49 name `Actors\SabreCat\Behaviors\SabreCatBehavior.hkx`: all 3,458
  idle filenames in the masters begin `Actors\` and not one begins `Meshes\`. A copy
  that writes the path from the Data folder instead reaches none of its idles. They are
  how the AI's
  actions reach the graph at all: `moveStart`, the turns, `swimStart`, `staggerStart`,
  `recoilStart`, `bleedOutStart`, sitting, lying down, dying. A copy of the graph under
  another folder is not that file, so a creature without copies of them stands where it
  is placed and never takes a step. `ImportCreature` copies each one, keeping its place
  in its tree -- an idle that hangs off no root's parent-and-sibling chain is never
  picked -- and se-cmd's `retarget` does the same.
- **Blender writes its unit scale onto the root node.** Its exporter multiplies every
  root's transform by `100 * scale_length`, and by a flat 100 with no unit system.
  HKFBX reads a root's bones and ignores its scale; NIFBX applies it. So the
  `skeleton.hkx` and every clip come out right and the `skeleton.nif` and the skin 100
  times too large, with no error anywhere. Export from a metric scene of scale length
  0.01 -- one Blender unit, one game unit -- and the factor is 1 both ways.
- **An arm of a parametric blend sits where its clip actually goes**, which is the
  clip's own travel or its own yaw, over its own duration, *at the rate the generator
  plays it*. The play rate is written on the clip and the arm on the blend, and nothing
  checks them against each other. The sabre cat's six forward arms satisfy it to the
  decimal -- trot 208.7 at twice rate is the fast band's 417.4, run 490 at 1.15 and at
  0.75 are 563.6 and 367.5 -- and only two of the six are played at a rate of one, which
  is why reading the clip and multiplying by nothing looks right. Getting it wrong is a
  creature that slides at low speed and turns a fraction of what it was asked for.
- **A shipped creature's forward clips may not turn at all.** The sabre cat's
  `WalkForwardL` travels 162 units and rotates a tenth of a degree, so the arms of its
  turning blends are an animator's description of what a clip looks like and the engine
  does the turning. Clips authored in Blender usually carry the turn in their root
  motion, which makes those arms mean something and makes the rule above apply to them.
- **A worn mesh names the biped slot it is worn in**, once per skin partition, and the
  slots run 30 to 61. A converter writes 0; the game reads that as a biped object out of
  range, stops trying to skin the mesh, looks for a `Prn` string naming a node to hang it
  off, and refuses the mesh with "Could not find parent node extra data". The addon knows
  the slot: the lowest bit its body template sets, plus 30.
- **A skinned shape has no bound of its own.** The engine rebuilds one each frame from a
  sphere per bone, held in that bone's space, and a converter leaves them all empty and
  at the origin. Merging coincident points of no size is a bound that is not a number,
  which the engine propagates up the tree and culls everything beneath.
- **A skinned shape hangs off the root.** 1,459 of the game's skinned shapes do, and the
  126 that do not are effects, thrown weapons and generated faces, never a body worn
  through an armour addon. Blender parents a body to its armature and the FBX says so.
- **A NIF's block sizes are derived data** and nothing recomputes them on an edit. A
  texture path rewritten after the conversion measured the blocks is a file the Creation
  Kit refuses with "Stream size mismatch" and a viewer opens without complaint.
- **A vertex shared across a UV seam is several vertices in a NIF**, and the weights
  have to reach all of them. The converter used to give them to one copy; the others
  are moved by nothing and drawn at the origin, which tears the skin open along every
  seam while every triangle is present and the bind pose is exact (NIFBX, fixed).
  Blender also leaves weights unnormalised and unlimited: limit each vertex to four
  influences and normalise before exporting.
