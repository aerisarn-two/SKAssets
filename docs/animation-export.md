# A creature's animations, in the same FBX as its skeleton

The game keeps an actor's animations as one Havok packfile per clip, over a rig
held somewhere else entirely: **5,120 clip files across the 46 actor folders that
have a skeleton**, and 49 actor projects in the animation cache to index them.
Per project the range is enormous — the witchlight has 7 animation slots, the
player 1,656, and the player's first-person arms another 869. Exported one file at
a time that is 5,120 FBXs each carrying its own copy of a skeleton, and an animator
who wants to see a walk beside a run has to load two scenes.

`ClipExchange` puts them in one scene instead: the skeleton once, from
`SkeletonExchange`, and one **animation stack** per clip over it.

```csharp
FbxDocument scene = SkeletonExchange.Export(mesh, havok);      // the skeleton, once
ClipReport clips = ClipExchange.AddClips(scene, havok.Rig, project);
```

Which clips is the caller's decision, and deliberately so. For a hare, all 18 is
obviously right. For the player it is not: see the measurements below.

## What it does over the shipped game

46 of the 49 actor projects, every slot each one has:

| | |
| --- | ---: |
| Actors exported | 46 |
| Animation stacks | 2,733 |
| …that the cache records as travelling | 1,689 |
| **Inert stacks** (bound to no bones) | **0** |
| Animations the project named and the folder lacked | 0 |
| Animations that would not decode | 0 |

Every stack bound the **whole** rig — for each of the 46, the fewest and the most
bones bound across its clips are both that rig's own bone count.

### What it costs

Linear in clips, and not small. Decompressing the spline curves dominates
everything else, and on Linux it runs Havok's own codec through mopper under Wine:

| | Clips | Time | Size |
| --- | ---: | ---: | ---: |
| Hare | 18 | 0.4 s | 8 MB |
| Falmer | 122 | 13 s | 110 MB |
| Draugr | 216 | 60 s | 187 MB |
| 46 actors together | 2,733 | ~7 min | 2.5 GB |

So the player's 1,656 slots in one file is on the order of a gigabyte and a half
and some minutes — a decision somebody should make on purpose rather than
discover, which is why `slots` is a parameter and not an assumption.

### The three that are not in the table

`DefaultMale` and `DefaultFemale` (1,656 slots each) were left out for the size
above, not for any difficulty. The third is a real gap:

- **`FirstPerson`** (869 slots) has a Havok rig — `skeletonfirst.hkx` — and **no
  mesh skeleton at all**. There is no `.nif` in the folder, because the
  first-person arms are skinned attachments rigged to it from elsewhere. A scene
  for it has to be built from the rig alone.
- **`DefaultFemale`** is a naming trap rather than a gap: its mesh is
  `skeleton_female.nif`, beside `skeleton_female.hkx`. Pairing a rig with "the
  `skeleton.nif` in the same folder" finds nothing; pairing on the rig's own stem
  finds it. That rule holds for 47 of the 49 and fails only here and above.

## The mesh has animation of its own

An actor's `skeleton.nif` can carry bone animation directly, with **no
`NiControllerSequence` and no `NiControllerManager` around it** — just
`NiTransformController`s hanging off the nodes. Not one actor skeleton in the game
holds a sequence, and 17 of the 46 hold controllers: the deer's has 39 of them,
113 curve nodes' worth, and NIFBX writes them as a stack of their own.

That is why those 17 come back with one stack more than `AddClips` reported, and
it is the behaviour that was wanted: the scene ends up holding the animation from
both files, which is the whole point of putting them in one. It also means
`AddClips` has to seed its name check from the stacks already in the document
rather than from the ones it adds, or a clip could collide with the mesh's own.

## Two things that are not in the animation file

Both would be silently lost by reading the packfile alone, which is the trap this
part of the work is mostly about.

### Root motion

Havok has a place for it — `hkaAnimation.m_extractedMotion`, an
`hkaAnimatedReferenceFrame` hanging off the animation — and **Skyrim does not use
it**. Sampled across 60 of the player's clips, including every locomotion clip in
the sample, it is `null` on all 60. The travel is recorded in the animation cache
instead, which is what `AnimationSlot.Motion` reads.

This was worth measuring rather than assuming, and the measurement changed the
design: an implementation was written against `m_extractedMotion` first, verified
against the corpus, found to return "no motion" for every clip in the game, and
deleted. A clip exported without root motion walks on the spot, and a function
whose only possible answer is "none" is worse than no function, because it looks
like an answer.

So root motion comes from `HKSK`, through `Conversions.ToFbx(ClipMovement)`.

### Which clip generators play the animation

A **slot** is what the cache indexes and a **clip** is what the behaviour graph
asks for, and the two are not one to one: several clip generators can play one
animation. The hare has 18 slots and 29 clips; the chicken 20 and 36.

That relation is not recoverable from the stack's name, so it travels as a
property: `ClipExchange.GeneratorsProperty`, tab separated.

## What a stack carries

A stack's name is the animation's file stem, because that is what an animator
reads off a menu. It is not enough to put the clip back, so three things are
written on the stack itself:

| Property | Holds |
| --- | --- |
| `sk_clip_animation` | the animation as the project stores it, e.g. `Animations\WalkForward.HKX` |
| `sk_clip_index` | its position in the character's animation list — the cache index |
| `sk_clip_generators` | the clip generators that play it, tab separated, absent where none do |

## The one number to check after an export

`ClipReport.Inert` — stacks that bound **zero bones**.

A stack whose curves are bound to nothing is present, well-formed, and drives
nothing. It is the failure mode that looks exactly like success: the file opens,
the clip list is right, every clip plays and nothing moves. It happens when the
scene does not hold the bones the clip animates, which is why `AddClips` reports
the bound count per clip rather than just a total.

The report also separates the two ways a clip can fail to arrive at all:

- **Missing** — the project names an animation file the folder does not have.
- **Unreadable** — the file is there and would not decode, with the reason. One
  unreadable clip is not a reason to lose the other 1,858, so the export
  continues and says what it skipped.

## What had to change underneath

Two libraries needed work before this was possible, and both gaps were the same
shape: an API built for one clip per file.

**HKFBX could only build a fresh document with a single stack.** So it gained
`FbxAnimationWriter.AddStack`, which puts an animation over a skeleton a document
already holds. Three things it has to get right that `Build` never did:

- **bind by node name, not bone name.** A scene converted from a NIF has its
  names escaped, so no node is called what the rig calls it, and the caller that
  knows the mapping passes it. Without one, nothing binds and the file is inert.
- **mark the properties animated.** A curve on a property whose flags do not
  admit animation is a curve readers are entitled to ignore, and several do.
- **append the take.** A stack with no take is a clip a reader will not offer, so
  every stack needs one — and only the first can be `Current`.

**The reader blended the stacks together.** `ReadAnimation` walked every curve in
the scene, which is right for one stack and silently wrong for several: their
curves sit on the same properties of the same nodes, so it took whichever the walk
reached last. It now takes a stack name and follows curve node → layer → stack to
decide what belongs to it, and `ReadTakeNames` says what there is to choose from.

Released as HKFBX **1.3.0**. Nothing below 1.3.0 can hold a second clip.

## A naming trap worth knowing

**Clip names are not unique.** Within a single project, three of the game's 49
actor projects hold a repeat — `DefaultFemale`, `DefaultMale` and `FirstPerson`.
`CrossBow_IdleHeld` appears twice under two distinct cache indices, and `Tor_Idle`
twice at the *same* index.

Two stacks of one name is a file whose clips a reader cannot tell apart, so
`AddClips` suffixes a number. A folder name would be more informative and is not
used: a stack name is read off a menu, and the paths are long.

## Running it

```
SKASSETS_SKYRIM_DATA="/path/to/Data" SKASSETS_HAVOK_MESHES=/path/to/loose/meshes \
    dotnet test --filter "FullyQualifiedName~ClipExchangeCorpus"
```

Both halves of the game, from two places: the meshes out of the archives, and the
Havok cache as loose files, because the cache is text the game ships inside a BSA
and HKSK reads a folder. The test's subjects are the hare and the chicken — 38
clips between them, enough to catch an inert stack without decoding 5,120.

## See also

- `docs/skeleton-exchange.md` — the scene the clips are added to, and why it comes
  back byte-exact.
- `docs/asset-kinds.md` — where animation actually lives, for the other seven
  shapes a mesh comes in: the ones driven by Gamebryo controllers inside the mesh,
  the ones that hand it to a Havok project, and the doors the engine opens itself.
