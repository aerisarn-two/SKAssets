# animgen

Brings the three animation caches -- `animationdatasinglefile.txt`,
`animationsetdatasinglefile.txt`, `speeddatasinglefile.txt` -- up to date with the
game's other assets, and writes all three:

    animgen <meshes> <data> [--plugin <file>]... [--project <name>]... [-o <folder>]
            [--slack <factor>] [--tolerance <units>] [--force]

- `<meshes>` — the extracted `meshes` folder: the three caches and the Havok files.
- `<data>` — the game's `Data` folder, for `Skyrim.esm`, `Update.esm` and the three DLC
  masters.
- `--plugin` — a plugin to read after the masters, as a path or a name in `<data>`, its
  records overriding theirs; repeatable, in load order.
- `--project` — bring only this project up to date, in all three files; repeatable. It
  is added where the files do not list it yet, as an actor when a race in the load order
  wears it, and every other project's entries are written back as they were read.
- `-o` — the folder to write to. Default the current one. Writing into `<meshes>`
  replaces the game's caches and needs `--force`.
- `--slack`, `--tolerance` — as for `setgen` and `speedgen`.

Without `--project` every project is brought up to date: the animation data is amended
entry by entry, since its root motion and event lists are in no Havok file (HKSK's
`docs/animation-data.md` §3), and the set data and the speed table are generated whole,
exactly as `setgen` and `speedgen` write them. The algorithm is HKSK's
`CacheGeneration`; this tool reads the plugins, which HKSK does not.

## Against the shipped files

Run over the extracted game and its five masters:

|                     | every project                              | `--project WolfProject`            |
| ------------------- | ------------------------------------------ | ---------------------------------- |
| animation data      | byte-identical, 429 of 429 entries          | byte-identical                     |
| set data            | 6 of 49 entries identical; 98.9% of the shipped files listed, 307 of 320 attacks, the moving flag agreeing on 294 | 48 of 49 entries identical; the wolf keeps 72 of 78 files and all 7 attacks, and names one more |
| speed data          | 41 projects against 49, 128 keys against 88, 76 in common; 82.3% of the shipped points within 2% as the game reads them | 48 of 49 blocks identical; the wolf's reads within 2% at all 323 shipped points |

The set data and the speed table are built for what the engine reads, not to reproduce
the shipped ones, so they differ by design: `setgen` and `speedgen` say where and why.
The horse's and the werewolf's caches number their clips against another character
list -- the horse's up to 88 against the 51 its character lists, the werewolf's at
another animation's slot for every clip -- and their speed curves hold 289 of 289 and
217 of 230 of their shipped points only because HKSK reads a clip's motion at the
cache's own number there; read through the character's list, they held 0 and 26. The
projects below half are the riekling (134 of 1,037), the benthic
lurker (21 of 253), the first person (39 of 209), the daedra (23 of 77) and the horker
(39 of 119).
