# How saves are verified

A save is checked at four levels. Passing the first three says nothing about whether the game accepts the state; only the fourth does. This file explains each level, how the headless runtime harness works, and the mistakes that cost time so they are not repeated.

## The four levels (`docs/archive/hgss_save_cli_engineering_design.md` §9)

| Level | Question | How | Where |
|---|---|---|---|
| A, structural | Is it a valid HG/SS save? | exact size, game identity, CRCs, intact active general and storage copies, edited Pokémon checksums | `validate`, strict open on every command |
| B, invariants | Did the edit stay inside the rules? | base SHA-256 and expected-value preconditions, coordinate mirrors, pouch limits, registry classification (no temp or special ids), byte audit: every changed byte is a requested write, a measured PKHeX bookkeeping byte or a recomputed CRC | `patch`, every writing command |
| C, story semantics | Do the flags, variables, items and badges agree with the game's scripts? | `story check`, badge rules, scene audits | `story check`, `story scene apply` |
| D, runtime | Does the game load it, spawn correctly and run the event? | boot in melonDS, assert the live location, drive inputs | `tools/leveld_run.py` |

`validate` reports `"semantic": "not-evaluated"`. A CRC-valid save is not a story-valid one.

`story check` reports violations of the badge rules (effects, the midgame counter, HM grants) and lists locked field moves for information. It finds 0 violations on the 29 corpus saves and on every applied preset. It does not model world prerequisites such as scene machines for out-of-order badge sets; for those, use a preset or scene.

A scene's own audit (exit 6 on failure) checks the badge bytes, the saved warp, every expected flag, variable and item, and the badge rules.

## Level D: the headless runtime harness

`tools/leveld_run.py` drives the native melonDS core through the melonDS-MCP library: JIT off, software render, FreeBIOS and generated firmware, direct boot. Each run copies the retail ROM and the save under one basename into a staging directory (the originals are untouched), advances frames, captures screenshots, and writes `report.json`. It can capture and restore full emulator states (`.mcpr`, about 19.7 MB), press buttons and touch the screen on a frame schedule, and export the in-game battery (`--extract-save`; the headless core does not flush saves to disk by itself).

Facts established about the harness:

- A fresh boot of 700 frames reaches the title screen, not the overworld. The intro movie is long: the title logo appears near frame 4,400 and the Continue menu near 4,600. A working schedule is to wait for the logo, press Start, press A to continue, then press only walking inputs. Extra A presses after Continue land in the field. Savestates taken at menu time and restored reach the same overworld.
- The player's `Location` struct (five ints: map, warp, x, y, direction; `field_types_def.h:10-16`) is findable by scanning ARM9 RAM `0x02000000-0x02400000`. It was first found by differencing overworld dumps of two maps.
- Its address depends on the map. Measured live copies: `0x227d498` (several maps), `0x227d4d0` (SS map 524), `0x227d4f8` (HG map 525), `0x227d444` (HG map 526). Do not hard-code one address.
- Other copies exist (a mirror near `0x239aa40`, a copy at `0x2377a20`). After the player moves, the active copy changes and the mirrors keep the old tile.
- Same-map NPCs have structs of the same shape. A scan that only asks "is there a struct on this map" can be satisfied by an NPC.
- Reading facing from the struct's last field is unreliable; report it, never gate on it. Save Z is not facing either.

### The spawn assertion

`--assert-spawn map,x,y,z` requires at least two agreeing full-pattern copies on a fresh boot, because a single hit can be a stale mirror or an NPC. When the map's own script moves the player after load, the save keeps the entry tile and the scene records the expected post-load tile (`expect.spawn_after`). `--spawn-primary` then matches the post-load tile in any copy, which is sound because the stale copies hold the saved tile and the two differ by construction. The earlier version hard-coded `0x227d498` and was wrong on three maps; it was fixed in `2ac4acd` and re-checked on the three scenes that use it (Lance, Kimono, Mt. Moon).

### What counts as proof at each rung

1. **Boots, correct spawn.** The save loads and the live location equals the intended tile. 80 of 80 scenes do (`docs/scene-boot-matrix.md`).
2. **Reaches the overworld.** Continue-to-overworld with screenshots. Violet, Lugia and Kanto fixtures, and the scene proofs.
3. **Encounter fought.** The player walks to the actor, presses A and the battle begins. Done for Ho-Oh, Red, Kyogre, Groudon and Rayquaza (`docs/archive/runtime-results.md`; the Rayquaza result is in commit `2ac4acd` and not yet in that file).
4. **Not done**: Lugia (reachable but the battle was not triggered), Articuno, Zapdos, Moltres, Mewtwo, Suicune, Latios, Latias, and the nine story-battle scenes from `2ac4acd`. These have boot rows only. The scene's gate logic comes from source, so a boot row is evidence the state loads, not that the event plays.

Event movies are observed from screenshots, not asserted frame by frame.

## Lessons that were expensive

Each is recorded where it happened; this is the short list.

- **Stale screens.** The bottom-screen capture can show the start menu while the game is not in it. The HGSS bottom screen always shows menu buttons in overworld play. A "menu will not close" diagnosis on the Kyogre run was a stale capture; the Down press that exposed it moved the player out of the room. Check the full screen, and check that a D-pad probe actually moves the player before drawing conclusions. A menu is open when the button grid has a red cursor highlight; the identical grid without a highlight is closed overworld UI. Top-screen hashes cannot see the menu.
- **Exit menus with X, B, B, B, B.** X toggles and B backs out, so the sequence converges to closed from any depth up to four.
- **Wild encounters look like walls.** A battle freezes the coordinates at the entry tile. Use Max Repel when mapping routes, especially on water.
- **Long holds can freeze.** One combined run moved on the first hold then stopped. Hold inputs of at most 60 frames and save a state per leg. Mechanism unknown.
- **Menus differ in input timing.** The start menu single-steps on 2-frame holds; the bag list ignores them and needs 6-8 frames. Touch taps are exact in both but did not work on SAVE.
- **Save with buttons.** X to open, move the cursor to SAVE, then A for the panel, A to confirm, A at the overwrite prompt after the text finishes. The start menu remembers its cursor, so the number of Down presses depends on the base.
- **Never type a hash.** A hand-copied SHA-256 lost a character; always compute it.
- **Validate JSON immediately after writing recipes**; stray commas broke recipes twice.
- **Read the decomp before probing the emulator.** Zone-event JSON replaced about 20 blind maze legs; reading the fly-map code replaced about 20 blind cursor probes.
- **Scan on the right warp field.** The warp id at struct offset +4 is `0xFFFFFFFF` on some outdoor maps and a real id (often 0) elsewhere. Scanning for the sentinel only caused the false claim that interiors drop the struct.

## What `docs/archive/runtime-validation.md` is

An early manual checklist for melonDS written before the headless harness existed. It says there is no headless mode; that is no longer true. Use it only for the manual save-and-diff procedure and the failure triage. This file and `docs/archive/runtime-results.md` describe the current process.
