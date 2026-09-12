# A creature's skeleton, out to one FBX and back to the byte

An actor's skeleton is stored twice, in two formats, and neither copy is
complete. `skeleton.nif` has the bone tree, the collision shapes, the rigid
bodies and the constraints; `skeleton.hkx` has the animation rig, the ragdoll,
and the mappers between them. A DCC can open neither.

`SkeletonExchange` folds both into one FBX and takes them apart again, and the
claim this document exists to support is the strict one:

> Over the 45 creatures in the shipped game that ship both files with a ragdoll,
> `nif + hkx → FBX → hkx` reproduces **all 45 skeleton.hkx files byte for
> byte**, and every field the per-field diagnostic checks — 30 of them — is
> bit-identical.

Not "within tolerance". A `skeleton.hkx` that comes back with one bit changed is
a file the game still loads and the ragdoll may still behave differently in, so
there is no threshold below which a difference is acceptable. `dotnet test
--filter "FullyQualifiedName~RoundTripCorpus"` asserts equality of bytes.

## The numbers

| | Count | Bit-exact |
| --- | ---: | ---: |
| Creatures with both files and a ragdoll | 45 | 45 |
| Rig bones (translation, rotation, scale) | 2,602 | 2,602 |
| Ragdoll bodies (11 fields each) | 982 | 982 |
| Capsule shapes (radius and endpoint) | 868 | 868 |
| Ragdoll joints (13 fields each) | 909 | 909 |

The two halves of the test are separate on purpose. The **control** reads a
`skeleton.hkx` and writes it straight back out, which says the Havok library can
hand a file back unchanged; the **trip** goes through the FBX. When both fail the
fault is below this library, and telling those apart saved chasing a precision
bug that was really a release-pinning bug.

## The rule the whole thing rests on

**Carry what you do not model, and do not rewrite what did not change.**

It reads as a truism and it is the single idea behind every fix in this layer. It
was arrived at from the other end: from a round trip that was 0 of 45 with 91,456
differing bytes, where each investigation ended at a field somebody had
recomputed when they could have copied it.

Two corollaries worth stating, because they are where the cost went:

- **A default is not an absence.** A collision filter of zero is a real value —
  172 vanilla bodies carry it, being the ones outside the ragdoll — so
  "regenerate the missing ones" renumbered them and the trip stalled at 810 of
  982. Hence `sk_filters_carried`: a marker saying the zeroes are the file's own.
- **Derivation and exactness are different jobs.** A field can be worth deriving
  and still wrong to derive when the answer is in hand. Both paths exist here and
  the choice is made per export, not per field.

## Derived, or carried

Everything in the FBX is one or the other. Where the mesh and the Havok file both
describe something, the derivation was measured against the shipped game before
being trusted, and the measurement is why the split falls where it does.

### Derived from the mesh

These come out of the NIF's `bhkRigidBody`, which is the right source when there
is no Havok file at all — which is to say, for new content. The column is how often the
derived value is **bit-identical** to the shipped `skeleton.hkx`, over 982
bodies:

| Field | Derived from | Bit-exact |
| --- | --- | ---: |
| Friction | `Friction` | 953 |
| Quality type | `Quality Type` | 982 |
| Restitution | `Restitution` | 920 |
| Inverse mass | `1 / Mass` | 734 |
| Capsule | first/second point, **swapped** | 716 of 847 |
| Linear damping | `Linear Damping` × 26.58824 | 29 |
| Angular damping | `Angular Damping` × 26.58824 | 0 |

Bit-exactness is a harsher measure than agreement, and the two damping rows are
where they diverge most: compared with a tolerance the derivation agrees on
99.7% of the angular values while matching the bits of none of them. The ratio
26.58824 is exact — the same number at the minimum, median and maximum over 85
samples — but Havok stores damping as a **half-float**, 10 bits of mantissa, and
a scaled 32-bit mesh value essentially never lands on one.

So the derivation is sound and the table is not an indictment of it. It says only
that a derived number and a copied number are not the same number, and a byte
comparison can tell.

The rigid body's physics live inside a version-union compound in the NIF, under
`Rigid Body Info\…`. Asking the block for `Mass` directly returns nothing, with
no error.

### Carried, because the mesh cannot answer

Each of these was put to the whole corpus as a derivation and each failed:

| Field | Why the mesh cannot say |
| --- | --- |
| Ragdoll bone names | Two naming conventions cover 69.8% of vanilla bodies; the rest are per-creature habits. The chaurus flyer drops its species prefix, so `ChaurusFlyerPelvis [Pelv]` becomes `Ragdoll_Pelvis [Pelv]01`. |
| Which nodes are rig bones | The mesh is always a superset, carrying between 2 and 46 extra, and no flag or block type separates them: `0x8000E` sits on both sides. |
| Inertia tensor | The mesh's tensor is an isotropic placeholder on three quarters of the bodies. |
| Motion type | Every body in the mesh says `MO_SYS_BOX_INERTIA`; 138 become sphere inertia in the ragdoll. |
| Collision filter | The mesh's layer, flags and group are `8, 0, 0` on every body, while the ragdoll's filter differs body by body. A *correct* generated filter matches vanilla 5.8% of the time, because two of its fields are an allocation rather than data. |

### Carried, because an FBX channel loses it

An FBX node states its placement as `Lcl Translation` and `Lcl Rotation`, and the
rotation is **Euler angles**. A quaternion turned into Euler angles and back is
not the quaternion it started as, so a reference pose cannot survive the channel:

- of 2,602 bones, **64** came back bit-identical;
- eight creatures had a bone land *tens of units* out — worst 61.6 — where Euler
  extraction folded near a gimbal and the fold was not undone the same way. That
  is not rounding, and it is what said the problem was structural rather than a
  matter of digits.

The channels stay, because a DCC needs them to show and edit the skeleton, and
the exact value travels beside them in `sk_pose`, which the import prefers where
it is present and falls back from where a tool has dropped it. Joint frames do
the same through `sk_frame_a` and `sk_frame_b`, being whole matrices.

Everything carried this way is written with round-trip formatting. .NET's default
`float` formatting has been shortest-round-trippable since Core 3.0, so parsing
what was written returns the same float; `G6` or `G9` would not.

## The two vocabularies

A constraint comes out of the mesh naming the two **nodes** it joins —
`Pelvis_rb` and `LFemur_rb`. A Havok reader wants the two **ragdoll bodies** —
`Ragdoll_Pelvis` and `Ragdoll_LFemur`. The mapping between those vocabularies is
precisely what this library carries, so the translation belongs here and nowhere
lower. `JointBridge` does it after the bodies are named, because it is done out
of what they were named.

Three findings shaped how, and all three came from the shipped files disagreeing
with themselves:

**Key a joint by the body it moves, not by the pair.** A body hangs from exactly
one parent, so the child names the joint. The **hare's** spine is numbered in
opposite directions in its two files: the mesh hangs the arms off
`Ragdoll_Spine03` where the ragdoll hangs them off `Ragdoll_Spine01`. 50 of the
909 joints match on the child and on nothing else. Where the two disagree the
Havok file is the one being reproduced, so the far name is overwritten from it
rather than trusted.

**Look bodies up by their plain names.** Keying by the escaped FBX spelling
matched only the creatures whose bones need no escaping — the cow, and nothing
with a bracket in its bone names. `NPC L Forearm [LLar]` is
`NPC_s_L_s_Forearm_s__ob_LLar_cb_` on the node and neither string is the one a
constraint states.

**Add a node for a joint the mesh cannot express**, the way the bone union
already adds one for a body the mesh does not have. The **frostbite spider's**
left and right `Leg_01` bodies are crossed between its two files, so one mesh
constraint names a body the ragdoll gives to the other leg: that one is marked as
the mesh's own and dropped on import, and the joint it failed to account for gets
a node of its own. Without it the ragdoll came back a joint short and all 32
joints after it had moved up one position.

## What lives only in one file

The mesh is *nearly* a superset of the rig and not quite, so both sides travel
and `sk_origin` says which had it. A node with no `sk_origin` is in both files.

- **Havok-only bones.** The `x_`-prefixed ones belong to Havok and are in no
  mesh. Excluding them takes the rig check from 131/204 to 199/204.
- **Havok-only bodies.** Pelt simulators, character bumpers and controllers have
  no node in the mesh at all — so the bridge never reaches them, and they need
  their settings written where they are invented. Leaving that out gave six
  bodies a friction of 0 against a stated 0.5: the wolf's three pelt simulators,
  the netch's capsule, the wisp's controller, and one leg of the frostbite
  spider.
- **Mesh-only nodes.** The file root, the actor node, weapon and magic mounts, IK
  helpers, and in the storm atronach's case an entire second unused skeleton.

The rig's own bone list travels as `sk_rig_bones` on the scene root, tab
separated — a bone name may hold a space, a bracket or a colon, but never a tab.
It is authoritative for both order and spelling, because a skeleton is its
indices as much as its names: the mappers, the animation tracks and the ragdoll
all address bones by position.

### What makes the fold legitimate

The two files agree about where the bones are. Composed to world space, the bones
both files carry land in the same place to floating point — over the werewolf's 80
shared bones, a median difference of 0.0000 and a maximum of 0.28. So the rig is
one skeleton stored twice, and the FBX carries it once.

The body correspondence is exact rather than approximate: **a NIF rigid body is a
ragdoll bone**. Across all 45 creatures, every ragdoll body rides a rig bone that
owns a rigid body in the mesh — 45 of 45, via `RigBone`.

One rule that looks obvious was tested and **refuted**: pruning the nodes the
`skeleton.hkx` does not use by dropping those with no rigid body below them
succeeds on 0 of 45 creatures and loses 1,421 bones.

## The collision filter

`hkpGroupFilter::calcFilterInfo` packs four fields into 32 bits:

```
(systemGroup << 16) | (dontCollideWith << 10) | (subSystemId << 5) | layer
```

`layer` is 5 bits, the two identifiers 5 bits each (so 31 is the maximum), and
`systemGroup` the top 16. The rule that makes a generated filter correct, rather
than merely well-formed, is that **`dontCollideWith` is the parent body's
`subSystemId`** — which is what stops a limb colliding with the limb it hangs
from. Found after four failed attempts that compared it against a position;
`RagdollFilter.Allocate` gets it right on 954 of 954.

So a filter can be authored from scratch, and it is generated whenever a scene
did not carry one. It still is not the number the original had, which is why
`carryFilters` defaults to on and `sk_filters_carried` records that it was.

## The property convention

Three prefixes, three owners. A DCC script reading a scene from here needs the
`hkb_` and constraint properties; the `sk_` ones are this library's own bookkeeping
and can be ignored by anything that is not rebuilding the Havok file.

| Property | Written by | Holds |
| --- | --- | --- |
| `sk_rig_bones` | SKAssets, on the root | the rig's bone list, tab separated |
| `sk_filters_carried` | SKAssets, on the root | `1` when the filters are the file's own |
| `sk_origin` | SKAssets | `havok` or `nif`; absent means both |
| `sk_pose` | SKAssets | 10 floats: translation, quaternion, scale |
| `sk_frame_a`, `sk_frame_b` | SKAssets | 16 floats, a joint frame |
| `sk_joint_index` | SKAssets | the joint's position in the Havok file's list |
| `hkb_ragdoll_bone`, `hkb_rig_bone` | SKAssets | the body's two names |
| `hkb_in_ragdoll` | SKAssets | `0` for a bumper or controller |
| `hkb_motion_type`, `hkb_quality_type` | SKAssets | Havok enums |
| `hkb_collision_filter` | SKAssets | the packed filter |
| `hkb_inverse_mass`, `hkb_friction`, `hkb_restitution` | SKAssets | body settings |
| `hkb_linear_damping`, `hkb_angular_damping` | SKAssets | body settings |
| `hkb_inverse_inertia` | SKAssets | three floats |
| `hkb_shape`, `hkb_capsule_a/_b/_radius` | SKAssets | the collision capsule |
| `constraint_type` | NIFBX | `Ragdoll`, `LimitedHinge`, … |
| `constraint_body_a`, `constraint_body_b` | NIFBX, retranslated here | the bodies a joint joins |
| `constraint_frame` | NIFBX | `A` on the far-frame child node, `B` on the joint |
| `coneMaxAngle`, `twistMinAngle`, … | NIFBX, overwritten here | the eight shared limits |
| `hkc_*` | NIFBX | the whole NIF descriptor, as strings |

Node names carry a convention too: `_rb` for a rigid body, `_sp` for a simple
shape phantom, `_con_` between the two halves of a constraint's name, and
`_attach_point` at the end of it. A far frame is a child node named
`…_attach_point_frame_a`. Names are escaped: space → `_s_`, `[` → `_ob_`, `]` →
`_cb_`, `:` → `_dd_`.

Blender caps a name at 63 characters and rewrites the overflow as a hash, and
19% of vanilla constraint names — 311 of 1,632 — are over that. That is why the
body names are stated as properties instead of being parsed out of the node name,
and the parse is only a fallback.

## Versions matter here

The convention above is split across three packages, and a version that predates
part of it fails quietly rather than loudly. NIFBX **1.0.1** writes a constraint
node carrying its `hkc_` descriptor dump and nothing a Havok reader looks for: no
body names, no far frame, none of the eight limits. Every joint read back
nameless, with identity frames and a cone of zero, and it looked exactly like a
precision problem. **1.1.0** is the first release with it.

HKFBX **1.2.1** is the release that stopped rewriting fields a caller never
changed, which is what makes the control half of the test pass at all.

## Running it

```
SKASSETS_SKYRIM_DATA="/path/to/Data" dotnet test \
    --filter "FullyQualifiedName~RoundTripCorpus"
```

Opt-in, like the other corpus suites, because the archives are not
redistributable. About three seconds for both halves over the 45 creatures; the
game's data is read-only and everything is written to a temporary directory.

## See also

- `NIFBX/docs/hkx-constraint-spec.md` — what a Havok constraint holds, and how a
  NIF descriptor maps onto it.
- `NIFBX/docs/dcc-constraint-interop-spec.md` — rules R1–R7 for turning these
  properties into working constraints in Blender, Maya and Max, with the traps
  each host adds.
- `SKDcc` — the per-host scripts that do it.
- `docs/asset-kinds.md` — what a skeleton is among the eight shapes a mesh comes
  in, and which companion files an actor needs.
