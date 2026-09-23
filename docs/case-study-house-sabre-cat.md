# A house cat from a sabre cat: the case study

    Status:   RECORD of work done. Every number here was measured from the files
              named, and every Creation Kit message was read out of
              CreationKit.exe rather than guessed at. The creature is built and
              loads; one behaviour is still unconfirmed in a running game (§10).
    Reads:    docs/creature-from-fbx.md (the route this follows), docs/authoring.md
              §6 (the copy route's API), docs/skeleton-exchange.md and
              docs/animation-export.md (the FBX conventions); in HKSK,
              docs/animation-data.md, docs/animation-set-data.md,
              docs/speed-data.md (the caches)

A cat, modelled and animated in Blender by hand, made into a Skyrim creature by
copying the sabre cat and putting the cat's own rig, mesh and animations through
it. This document is what that cost: the conventions the exchange has to keep,
the faults it uncovered on both sides, and what each of them looked like before
it was understood.

It is written as a case study because most of the work was diagnosis. The
library changes are small and each is obvious once stated; finding out which of
a dozen silent things was wrong is the part worth writing down.

## 1. What was made

    Data/CatSimple.esp                          73 records
    Data/Meshes/animationdatasinglefile.txt     51 projects, the cat's added
    Data/Meshes/animationsetdatasinglefile.txt  the cat's attack block
    Data/Meshes/speeddatasinglefile.txt         the cat's speed block
    Data/Meshes/actors/HouseCat/                project, character, 4 behaviours,
                                                rig, skin, 131 animations
    Data/Textures/actors/HouseCat/              2 DDS

The records: a race, an armour and one addon, body part data, an NPC, two
movement types, 58 idles, seven sound descriptors and a sound marker.

The inputs, all hand-made:

    catsimple_fbx/Cat_Simple_LowPoly.fbx      the mesh and its rig
    catsimple_blend/Cat_Simple_Anim_RM.blend  124 actions, with root motion
    catsimple_textures/texture/               the albedo and normal

and three scripts that turn them into what the import reads: one that writes the
ragdoll FBX and the body FBX, one that retargets every action onto the ragdoll
FBX's rig and writes the clip FBX, and one that maps the sabre cat's 57 animation
slots onto the cat's clips and adds 74 slots of its own.

Rebuilt end to end by `ZzCatCreature.Import` in the authoring tests, with
`CAT_MOD`, `SKASSETS_HAVOK_MESHES` and `SKASSETS_SKYRIM_DATA` set. Nothing here
is done by hand, which is the point: every fault below was fixed in the library
and the creature rebuilt, never patched in place.

## 2. The FBX conventions, and the one that bites

**A Blender scene must be metric at a scale length of 0.01.** Otherwise the
exporter writes a scale of 100 on every root node. HKFBX ignores a root's scale
and NIFBX applies it, so the Havok rig and the clips come out right and both NIFs
come out a hundred times too large -- a discrepancy that reads as "the skeleton
has a scale effect with respect to the mesh" and is neither file's fault. The
unit system must be set *after* importing anything measured in metres, or the
import is scaled instead.

**The clips come from the .blend, not the FBX.** The mesh FBX carries no root
motion. The actions do, so each is evaluated frame by frame on the .blend's
armature, constraints and all, and every bone's world pose is carried onto the
same bone of the *skeleton FBX's* armature. The two rigs do not share bone
frames: the animator's bones have the rolls they were drawn with and the root
points up, while the skeleton was built from an FBX import, whose bones Blender
reoriented and whose root rests unrotated. A clip is a list of bone transforms in
the bones' own frames, so the frames have to be the skeleton's or every bone is
off by its rest difference. The transfer is a turn to face +Y, a scale to 70
units a metre, and the rests' difference taken out per bone.

**The body FBX hangs its bones under a plain node.** An armour mesh is skinned to
the actor's skeleton, not to one of its own, and the check that says so reports
`armor-external-skeleton` when the bones sit under something else.

## 3. A rig of its own

The cat's rig is not the sabre cat's: 46 bones against 64, 20 ragdoll bodies
against 28, and almost every name different. Two pieces of library work carry a
creature across that gap.

`HkxSkeletonBuilder` writes a skeleton packfile for a rig the template has not
got, cloning each constraint list from its own prototypes and sharing motors,
because a skeleton file is not a list of bones but a graph of bodies, joints,
constraints and the resource tree that names them.

`RigRemap` then rebinds everything in the copied behaviour that named a bone by
index: foot IK legs with their knee axes and ankle heights re-derived, the mirror
table, look-at bones and axes, the keyframed and powered-ragdoll and contact
lists, scalar bone indices, bone weights. Where a bone has no counterpart the
entry is dropped and said so in a note -- the cat has no ribcage bone, so seven
look-at modifiers lose one.

The mapping from the template's bones to the cat's is given by the request, 28
pairs of names. That map is why §9 had to be careful about what may be renamed.

## 4. The mesh faults, and what the game says about each

Four separate things were wrong with the NIFs, and not one of them showed in a
viewer.

**Seams.** 364 of 1,555 vertices were unweighted, in stripes along the UV seams.
A control point split at a seam becomes several vertices in the NIF and NIFBX was
giving the weights to one of them; the rest collapsed to the origin. Fixed in
NIFBX with a control-point-to-vertices map, with a regression test whose fixture
is a Blender script that welds a seam so the file asks for the split.

**Stale block sizes.** A NIF's header states how long every block is and the game
checks each block against it. The sizes are derived data, and nothing recomputed
them: retargeting the textures lengthened `BSShaderTextureSet` from 116 bytes to
132 while the header went on saying 116. NifSkope reads by structure and opened
it happily; the Creation Kit refused the mesh with **"Stream size mismatch."**
NIFSharp's `Save` now refreshes the header, and `CreatureChecks.BlockSizes`
reports every block either way.

**No slot on the partitions.** The Kit refused the body with

    Could not find parent node extra data for 'BASE meshes\actors\HouseCat\HouseCatSkin.nif'.

which is from `bipedanim.cpp`, next to "Extra data 'Prn' on '%s' is not an
NiStringExtraData" and, in the same function, "Precondition: The BIPED_OBJECT
should not be out of range." A dismembered skin names a biped slot per partition
and the slots run 30 to 61; NIFBX has no way of knowing which and wrote 0. The
game reads that as a biped object, finds it out of range, stops trying to skin
the mesh to the actor, looks instead for a `Prn` string naming a node to hang it
off, finds none because a skinned mesh does not want one, and gives up. The
armour addon knows the slot, so it passes it down: the lowest bit its body
template sets, plus 30. The cat says 32, which is what 362 of the game's own worn
meshes say; only 14 partitions in the whole game say 0 and none of them is a
body.

**No bound.** Loading the actor filled the log once a frame with

    bound center is NaN and propagates from here, resulting in this node
    complete hierarchy being culled.

Nothing in the files is a NaN -- every number in both NIFs and all 138 packfiles
was read, and the only one that is not a number is Bethesda's own uninitialised
`m_errorOutTranslation`, which the sabre cat's quadruped behaviour ships with
too. The NaN is made at load. The message comes from `nibound.inl` lines 83 to
85, three `_fdtest` calls against the NaN code, inside the one function that
transforms a bound by a transform; its only caller is `NiSkinInstance` in
`niskininstance.cpp`, which builds a skinned shape's world bound by seeding it
from bone zero's stored sphere and merging the rest, each transformed by its
bone's world transform. The merge cannot be the source: its one division is
guarded by a distance greater than 1e-6, a constant read out of the binary. So
the bound is rebuilt every frame from one sphere per bone, held in that bone's
space -- and the conversion left all 46 of them empty and at the origin. The
game's own bodies carry real radii, 45.9 units on the sabre cat's pelvis, and not
one bone of the four shapes in its body has an empty sphere. Each is measured now
from the vertices that bone actually moves; on the wolf they come back the size
Bethesda shipped them, within a tenth either way.

**Where the shape hangs.** A skinned shape is placed by its bones, so its parent
only says where the engine starts from, and every body the game has an actor wear
is a child of the root: 1,459 of the shipped skinned shapes are, and the 126 that
are not are effects, thrown weapons and the parts of a generated face. Blender
parents a body to its armature and the FBX says so, so the cat's body arrived
under a node of its own carrying that node's transform. Skinned shapes are moved
to the root now and keep where they stood, what their parents did to them
composed in first; unskinned shapes are left alone, since a tree of them is how a
mesh with moving parts is built.

**And what a skeleton carries besides bones.** A converted skeleton has the
bones, the ragdoll and a `BSXFlags`, which describes the skeleton completely and
is not a complete skeleton file. Every one the game ships hangs three more pieces
of extra data off its root:

- `BSBound` named `BBX`, the actor's box. It is measured rather than copied,
  since the sabre cat's is the sabre cat's size: half extents around a centre
  that is the box's own height up, which is what all 49 shipped skeletons state
  and what leaves the box standing on the ground rather than straddling it.
- `BSBoneLODExtraData`, a distance and a bone per entry. Each is looked up under
  the name the new rig has for it; a list with nothing left is left out rather
  than written empty, which is what the cat gets, the sabre cat's one entry being
  a finger.
- `NiIntegerExtraData` named `SkeletonID`, which looks like an identity and is
  not one. The shipped skeletons share a handful of values between creatures with
  nothing in common: 1361955 covers the hare, the wolf, the draugr and the falmer,
  and 207579012 the bear, the dog, the cow, the dragon and the player. It records
  an export run, so any value does and the template's is copied.

No creature skeleton has a detection phantom; only two Dragonborn ones have a
phantom at all. Two non-bone *nodes* the sabre cat has are still absent from the
cat's skeleton, `MagicEffectsNode` and the camera target.

## 5. What the Creation Kit says, and where it says it from

Four messages, each traced to the code that prints it. They are recorded here
because the wording gives no hint and the same words cover very different faults.

| Message | From | Means |
| --- | --- | --- |
| Could not find root behavior | `0x141f54370`, lookup at `0x141f3b0f0` | The project was not found **by name in the merged animation cache**, whose first behaviour in the file list is the race's root. The cache has to be installed, not just the loose files. |
| Stream size mismatch | the NIF reader | A block consumed a different number of bytes than the header declared. |
| Could not find parent node extra data | `bipedanim.cpp` | A worn mesh could not be skinned, so the Kit looked for a `Prn` node name and there was none. §4. |
| aSize != 0 | `memorymanager.cpp` line 708 | An allocation of zero bytes. Benign: the handler returns a pointer from a global and toggles bit 4 so the next zero-size request differs, which is the usual trick for giving distinct addresses to distinct empty objects. Every array that is empty in the cat's meshes is empty in the sabre cat's too. |

## 6. The records

The copy route duplicates the template's records and points them at the new
creature. Faults found, each now fixed in the library:

**Two addons, one race.** The sabre cat's skin dresses two races through two
addons, and re-racing both onto the new race left the Kit reporting "Armor
priorities are the same (0) for part 'BODY' (32)". Only the addon that dresses
the template's own race is kept; the rest are left out and said so.

**One addon keeps the import's mesh name.** Meshes given to an addon by name are
numbered so two cannot overwrite each other, but a creature wears one addon and
had nothing to be told apart from, so the cat came out as `HouseCatSkin_0.nif`,
which in this game reads as the light end of a weight slider.

**The NPC's attack race.** An actor takes its reach, its damage and the attack
events it may send from its attack race, and the copy still named the sabre cat's.
The cat was asking to swing at 85 units for 35 damage and ignoring its own record.

**A fault in the masters.** `SabreCatNoSpeed`, the parent of every turn in place
the sabre cat has, names the **skeever's** behaviour file. The rule that picks up
an idle tree passed it over as another creature's, which left the copy of its
child pointing back into the sabre cat's tree. It is taken along now and given
the file its branch plays under. This is the class of fault se-cmd keeps a table
for.

**Reach.** Set to 40 by hand and wrong: every vanilla creature that attacks uses
at least 64, and the combat AI will not swing until it is inside that reach,
which two collision capsules keep it from closing much under 60. A cat at 40
circles forever.

## 7. The caches

The merged files are the part a mod author forgets. The Kit and the game look a
project up by name in `animationdatasinglefile.txt` and take the first behaviour
in its file list, so a creature whose loose files are perfect and whose cache is
not installed reports that its root behaviour cannot be found.

The cat's block is the sabre cat's with its file list replaced, its clip entries
renamed with the nodes, and 393 checksums against the template's 204 -- the
difference being 131 animations against the sabre cat's spline-compressed set.
The attack block matches the sabre cat's exactly: eight attacks with the same
clip counts.

## 8. What the graph had to learn

The sabre cat's behaviour plays 57 animations. The cat has 131, so the graph is
amended rather than merely retargeted: sneaking, falls and jumps, swimming,
backward and fast locomotion, a wider idle repertoire, lying on the side, and
dying. Every number the new blends and thresholds need is read off the cat's own
clips -- speed as travel over duration, turn rate as the root's yaw over duration
-- so nothing is a threshold read off a shipped curve.

The result is that all 131 clips are played by something, which the build
asserts, and an engine evaluator reaches the right clip for every event the game
sends: each attack, recoil, stagger, swim, death, sneak and fall.

## 9. Naming, and what a name is for

A copy that keeps the template's names is confusing wherever a name is shown, so
the import renames after the creature: the files, the records, the animations,
the graph's nodes, its events, its variables and its character properties. The
rule is made from the template's name rather than listed by hand -- the whole of
it, its last word, and the leading words' initials before that word -- because
the sabre cat's records rarely spell it out: `SCatRecoil`, `ScatReset`,
`CatIdleWarn`, `SabreCatDefault`. A short form has to stand at the front and end
where a word ends, so `Cat` opens `CatIdleWarn` and not `Catch`. A name
misspelled by one letter still counts, which is how the masters' own
`SabreCastStartSwimming` comes out right.

Three orderings matter, and each was got wrong once first.

- **The animations are renamed last**, after the clips are imported. A clip
  manifest is written against the template's names: it says
  `Animations\_SabreCat_Idle_Sleep.hkx` and means the slot, not the sabre cat.
  Renaming the files while copying them put the cat's own clips in files nothing
  plays and the sabre cat's in the files the graph names.
- **A movement type's constant is renamed by the pass that makes the record** to
  go with it. Renaming it earlier left the import unable to find the movement
  type it had been asked for.
- **The sounds are copied before their references are touched**, because the name
  *is* the reference. §10.

And what a name is *for* decides whether it may be renamed at all. This was the
sharpest lesson of the session: renaming every name in the copied packfiles went
one file too far, because the project's file list includes the skeleton and a
bone carries a name like anything else. All 64 of the rig's bones were renamed,
after which the request's bone map matched nothing: the cat lost all four foot IK
legs and its keyframed bone lists came back empty. A behaviour node's name is a
label, read by whoever opens the graph and by the animation cache. A bone's name,
a ragdoll body's and a constraint's are identifiers, matched across files by the
bone map, by the skin's bone list and by the ragdoll's own. Only the behaviour
classes are renamed, which is 41 nodes rather than 132.

Two kinds of event keep their names on purpose:

- **Paired kill move events.** `KillMoveSabreCat` is how a man's behaviour and a
  sabre cat's agree on one animation. A creature that renames it can never be
  killed that way, whatever it calls itself.
- **Sound events**, until the records behind them exist. §10.

## 10. Sounds

A sound is asked for by an event or an annotation reading `SoundPlay.` and then
the editor id of the record to play, so the name is the reference and renaming it
silences the creature. The record is copied first, under the creature's name, and
the reference then points at the copy -- a marker at the descriptor beneath it
where that was copied too, which is how `NPCHouseCatAttack` reaches
`NPCHouseCatAttackSD`.

The cat takes eight, seven descriptors and a marker. Only the ones the template
names are taken: a shared graph asks for every species that uses it, and the
quadruped one names the bear's, the cow's, the goat's and the dog's, which belong
to the creatures whose states they sit in.

The copies play the template's audio, since that is the only audio there is. What
they are for is that the files can be replaced without touching the game's own
records, and that a creature's sounds read as its own wherever they are listed.

## 10a. The one record field that did both

The creature loaded, stood, and played idles. In game it slid where it should
have walked and never attacked, and both are the same field.

An idle record reaches an actor by the behaviour file it names, matched against
the graph that actor runs. The copies spelled that path from the Data folder,
`Meshes\actors\HouseCat\Behaviors\HouseCatBehavior.hkx`. The game spells it from
Meshes: all 3,458 idle filenames in the masters begin `Actors\` and not one
begins `Meshes\`, which the bytes of the two plugins say side by side.

So the cat had 62 idle records and reached none of them. No `moveStart`, which
is an actor whose capsule moves and whose legs do not. No `combatStanceStart`,
which is an actor that never draws, and a combat AI that will not order an attack
from an actor that has not. The only idles it played were the ones its own graph
cycles through without being asked.

What made this hard to see is that the race's own graph path was already spelled
correctly, which is why the creature loaded at all. What found it was comparing
the written plugin against Skyrim.esm rather than reading either alone.

Two things were ruled out on the way, and both are worth recording. Driving our
project and the sabre cat's through the same events reaches the same attack clip
in both, with a combat stance and without, so the graph was not refusing the
attack. And of the cat's animations that replace one of the sabre cat's, 47 tell
the graph exactly what the original told it; the 10 that differ differ only in
the sound they name, which is the rename in §10.

## 10b. Where a speed ladder's arms go

A parametric blend on speed -- the *ladder* -- has an arm per gait, and an arm
sits at the speed that arm moves the creature. Not the speed of the clip under
it: the speed of the clip at the rate the generator plays it. The game's own say
this exactly. The sabre cat's trot plays at twice rate for its fast band, 208.7
by 2 is 417.4, and the arm reads 417.4. Its run plays at 1.15 and at 0.75 for
the two run bands, 490 by each, and the arms read 563.6 and 367.5.

The cat's arms were set from the clips' own speeds with the rate forgotten, so
three of the six were in the wrong place. The worst by a factor of twenty-five:
the slow walk band is the walk clip played at a twenty-fifth speed, so an arm
that said 5 delivered 1.3. Everywhere between a standstill and a walk the graph
was asked for a speed it answered at a quarter of, which in the game is a cat
that stands still while it slides along.

They are read off the clips now, after the fast run has been swapped into the
top band so that what an arm says is what the clip beneath it will do. The walk
ladder delivers 1.5, 33.0, 82.3 and 164.6; the run ladder 126.4 and 273.7. The
proportions are the sabre cat's, because the play rates are: its creep is a
twenty-fifth of its walk and so is the cat's.

## 10c. And where a turning blend's arms go

The same rule, in the blends that steer, and one difference of authoring behind
it that is worth knowing before copying any creature.

**The sabre cat's forward clips do not turn.** Its `WalkForwardL` travels 162
units and rotates a tenth of a degree. So the arms of its turning blends -- 62.8,
77.1, 122.7 degrees a second -- are not measurements of anything in the clip.
They are numbers an animator wrote down for how much of a turn each clip looks
like, and the engine does the turning.

**The cat's clips carry the turn in their root motion**, a clean ninety degrees
a second at a walk, because that is how they were authored in Blender. So its
arms have to be what the clips will really do, and the play rate counts again:
the slow walk said ninety where its clip at a twenty-fifth rate delivers four,
and the fast trot said a hundred and thirty-five where its clip at twice rate
turns two hundred and seventy. A graph that picks an arm expecting a turn and
receives a fraction of it is a creature that begins turning across a lot of
ground and comes out barely rotated.

The turn in place needed nothing, and checking it is how the rest was confirmed:
its multiplier divides the requested turn by the looping clip's own rate, and
the sabre cat's expression divides by 112.5 where its clip measures 112.5 to the
decimal. The cat's measures 174.5 and its expression says 174.5.

## 11. What is not settled

**Attacks were reported not to fire in game.** The plugin, the set data, the
caches and the graph all check out, and the engine evaluator reaches the right
clip for every attack event. The reach was 40 and is now 64, which is the leading
suspect and the one thing known to have been wrong. Ruled out along the way: the
character controller capsule (every creature shares 1.7 by 0.4, whatever it
stands at), uncompressed animation support, and NPC aggression, the vanilla sabre
cat being Unaggressive too.

**Two nodes the template has are still missing from the cat's skeleton**,
`MagicEffectsNode` and the camera target. They are child nodes rather than extra
data, so grafting them is a different operation from §4.

**The corpus is shared between test suites that write into it.** Two export tests
failed once in a full run and passed alone and on a repeat. That is how the
corpus was corrupted once before, and it is worth fixing.

## 12. The checks this left behind

Each fault above is now a check that runs on every creature import and reports
either way, so a build says what it verified rather than only what went wrong:

    skeleton-placement  the skeleton NIF against the Havok rig, bone by bone
    skin-bind           the skin's bind pose against where the skeleton stands
    triangles           the FBX's triangles against the NIF's
    skin-weights        every vertex fully weighted
    skin-slot           every partition naming a biped slot
    skin-bounds         every bone carrying the sphere of what it moves
    skin-parent         a worn shape hanging off the root
    nif-block-sizes     every block the size its header says

Plus the probes the diagnosis was done with, kept because the next question of
the same kind starts from them: what hangs off each node of a NIF and what a skin
says about its bones; every number in a file against the ones that are not
numbers; the same over a folder of packfiles; which arrays hold nothing, ours
beside the file we copied; every name in a project sorted by what kind of name it
is; every sound a project asks for and what record answers.
