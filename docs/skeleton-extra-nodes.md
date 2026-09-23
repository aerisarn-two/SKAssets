# The nodes a skeleton NIF has that its rig has not

    Status:   MEASURED. The census is over the 49 creature skeletons in the
              extracted meshes that have both a NIF and a Havok rig. Every claim
              about the engine was read out of SkyrimSE.exe or CreationKit.exe,
              and every claim about records out of the masters.
    Reads:    docs/skeleton-exchange.md (what the two halves of a skeleton are),
              docs/case-study-house-sabre-cat.md §4 (why this was asked)

A creature's skeleton is two files. `skeleton.hkx` is the rig the animation
drives: a list of bones, a ragdoll, and the mappings between them. `skeleton.nif`
is the same bones as a node tree, and **more besides**. This document is what
the more is, how much of it any creature actually has, and which of the three
mechanisms each node belongs to.

It exists because a skeleton rebuilt from an FBX has the bones and nothing else,
and it was not obvious what that costs.

## 1. The census

Of the 49 skeletons with both halves:

- the smallest has **2** nodes beside its bones, the largest **64**;
- **278 distinct names** appear across them;
- **none** has only bones.

The root node is one of them: it is named after its file (`Skeleton.nif` on 46
of the 49), and the Havok rig's root bone is named something else. So one node
beside the bones is a floor, not a feature.

The ones that recur:

| Node | On |
| --- | --- |
| `MagicEffectsNode` | 21 of 49 |
| `NPC L/R FootBox`, `HeelPivot`, `ToePivot` | 11 each |
| `Capsule01` | 10 |
| `NPC` | 8 |
| `CharacterController` | 7 |
| `CharacterBumper` | 6 |
| `WEAPON` | 5 |
| `SHIELD`, `QUIVER`, `WeaponBack`, `LagBone` | 4 each |

Everything below that is one creature's own: the wisp's twenty-odd cloth wraps,
the storm atronach's `swirly_debris_*`, the wolf's `WolfPelt`.

**So no, not every creature has a `MagicEffectsNode`.** Fewer than half do. The
sabre cat is one of them, and has exactly three nodes beside its bones:
`MagicEffectsNode`, `Camera01.Target`, and the root.

## 2. Three mechanisms, and how to tell them apart

A node that is not a bone is there for one of three reasons, and the test is
simple: **look for its name in the binary, then in the masters.**

### 2.1 The engine knows the name

`SkyrimSE.exe` holds a run of string constants that are node names it looks up
on an actor's tree. They sit together in `.rdata` from about `0x14178A3F0`,
which is how they were found: a name of interest turned out to have neighbours.

    NPC Root [Root]        NPC Head [Head]       NPC Pelvis [Pelv]
    NPC Spine [Spn0]       NPC Spine1 [Spn1]     NPC Spine2 [Spn2]
    NPC L/R Foot, Calf, UpperArm, Forearm        NPC COM [COM ]
    NPC LookNode [Look]    NPC Tail1             NPC TailHub
    Camera1st [Cam1]       Camera3rd [Cam3]
    NPC L MagicNode [LMag] NPC R MagicNode [RMag] NPC Head MagicNode [Hmag]
    ProjectileNode         BlastRadiusNode        TorchFire
    WeaponSword  WeaponDagger  WeaponAxe  WeaponMace  WeaponBack  WeaponBow
    ArrowQuiver  ArrowBone  Backpack  PinnedLimb  LaserSight  AimSight
    Prn          AttachLight  AttachSound  ModelSwapNode  Decal Node

Beside them, in the same region, are the names of the things that attach *to*
them and the markers that are not nodes at all -- `EditorMarker`, `Sound
Marker`, `Skinned Decal Node`, `grabLeft`, `grabRight`.

`CharacterBumper` is in the binary twelve times, next to "update character
state", "Character movement" and `bhkNiCollisionObject`: it is the collision
object the character controller pushes other actors with, and the engine finds
it by name on the tree.

A node in this list is part of a contract with the code. Rename it and the
feature it serves stops working, silently.

### 2.2 A record names it

`MagicEffectsNode` is **in neither binary**. Nothing in the code looks it up.
It is named from data, in two places, and both are in the masters:

**As a body part's node.** A `BPTD` record's parts each carry a `BPNN` field,
the name of the node that part hangs on. `MudcrabPartData` and
`DLC2HMDaedraPartData` name `MagicEffectsNode` there. The same field is where
the string `BASE` comes from -- the one that opens the Creation Kit's "Could not
find parent node extra data for 'BASE meshes\...'" -- because the Kit builds a
model-database key out of the part node and the model path.

**As an attach point, through a keyword.** Skyrim.esm holds a keyword whose
editor id is

    AtT_NamedNode&NPC%SPC%Spine2%SPC%%LBR%Spn2%RBR%_NamedNode&MagicEffectsNode

which is an encoded list of node names -- `%SPC%` a space, `%LBR%` and `%RBR%`
the brackets -- reading `NPC Spine2 [Spn2]` and then `MagicEffectsNode`. Art
objects carrying that keyword attach at whichever of those the actor has. That
is why the node is optional: a creature without it simply is not a candidate for
the art that asks for it, and the other name in the list answers instead.

The third data mechanism is the one the Kit's message is about: a model can
carry an `NiStringExtraData` named `Prn` giving the node to hang itself off, and
`Prn` is in the binary's list above because the code reads that string.

### 2.3 Nobody knows the name

The rest are authoring leftovers, and they are the majority of the 278. They are
in neither binary and in no record:

- 3ds Max helpers that came along in the export: `Camera01.Target`,
  `Camera02.Target`, `Tape01.Target`, `Capsule01`, `Capsule02`, `PreviewCam`,
  `PreviewCamTarget`, `Camera Control`, and a node named `_`;
- rigging aids: `NPC L/R FootBox`, `HeelPivot`, `ToePivot`, on eleven skeletons
  each, none of them a bone of the rig and none of them named in code;
- per-creature effect and cloth anchors: the wisp's wraps, the storm atronach's
  debris nodes, `WolfPelt`.

`Camera01.Target` is on exactly one of the 49, the sabre cat's. That is the
signature of a leftover rather than a feature.

## 3. What this means for a creature built from an FBX

A rebuilt skeleton needs the nodes from §2.1 that its behaviour and its records
actually use, and nothing from §2.3. The list is short for a quadruped: a cat
that never casts, never carries a weapon and has no first-person camera needs
none of them.

What it does need is the **extra data** on the root, which is a separate question
and is covered in the case study: the actor's box, the bone LOD list, and the
skeleton identifier. A node is looked up by name when something wants it; extra
data is read every time the actor is loaded.

The one open item for the cat is `MagicEffectsNode`. Nothing in the code wants
it, no record of the cat's names it, and the keyword that does names
`NPC Spine2 [Spn2]` first -- which the cat has not got either, its spine being
its own. So a magic effect that asks for that attach point will not find one on
the cat and will fall back to the actor's root, which is what happens to the 28
creatures that ship without the node.

## 4. How the census was taken

`ZzSkeletonNodes` walks every `skeleton*.nif` under the extracted meshes that has
a packfile of the same name beside it, reads both, and lists the NIF's node names
that are not bones of the rig. The aggregate at the end of its output is the
table in §1.

The binary side is a string search followed by a pointer search: a name that
appears as a constant is looked for both as a direct reference and as an entry in
a table of `const char*`, because the interesting ones are in tables. The record
side is a byte search of the masters followed by a walk back to the nearest
`EDID`, which names the record the string belongs to.
