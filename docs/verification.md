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
- Its address depends on the map. Measured live copies: `0x227d498` (HG map 305 and others), `0x227d4d0` (SS map 524), `0x227d4f8` (HG map 525), `0x227d444` (HG map 526), `0x227d488` (HG map 244). Do not hard-code one address.
- Other copies exist (a mirror near `0x239aa40`, a copy at `0x2377a20`). After the player moves, the active copy changes and the mirrors keep the old tile.
- Same-map NPCs have structs of the same shape. A scan that only asks "is there a struct on this map" can be satisfied by an NPC.
- Reading facing from the struct's last field is unreliable; report it, never gate on it. Save Z is not facing either.
- The emulated clock is not the host clock. On a boot of map 300 the game's own `VAR_TEMP_x4000` (written by `GetWeekday`) read 6, and a savestate's `RTC.` section held `00 01 01 06 04 07 01`: 2000-01-01, week byte 6, which is Saturday under the 0=Sunday count melonDS documents (`src/RTC.cpp:176`). Host sync lives in the Qt frontend (`EmuInstance::setDateTime`) and does not run in this core, so anything gated on `GetWeekday` sees that fixed Saturday whatever the host date is: `event-league-gate-rival` is the case in point (`docs/archive/runtime-results.md`).
- The live `SaveVarsFlags.vars[]` array is at `0x227d05c` when map 300 is loaded, and `VAR_TEMP_x4000` is its first entry. It can be found on any map by matching 32 bytes copied from the save's `EVENT_WORK` block (file `0x40DE4`, 736 bytes, then `flags`), because the loaded copy differs from the file only in what the scripts have written since the load.

- A save cycle is one boot. After the write the game returns to the field with the touch panel still up, and another X press moves the cursor instead of reopening the save flow. `tools/save_once.py` polls the field panel (a green panel on the bottom screen's left half; dialogs are orange) rather than counting frames, then sends X, right, down, A, A, A. The write finishes about 840 frames after the third A.
- The save check runs during the intro, before the title. A damaged file shows its message near frame 1,800 and waits for A, so the message text can be read without reaching the menu (`leveld-out/fmt-matrix/<case>/d2.png`, `d3.png`).
- Which partition loaded is readable from the *next* save. The game writes into the partition it did not load, with counter `loaded + 1` (`save.c:624`, `save.c:635`). Boot each candidate with a distinct play time and the written chunk carries the loaded one's time plus the seconds spent in the field. The counter alone does not prove a write: after a fallback it repeats a value the rejected partition already held.

### The spawn assertion

`--assert-spawn map,x,y,z` requires at least two agreeing full-pattern copies on a fresh boot, because a single hit can be a stale mirror or an NPC. When the map's own script moves the player after load, the save keeps the entry tile and the scene records the expected post-load tile (`expect.spawn_after`). `--spawn-primary` then matches the post-load tile in any copy, which is sound because the stale copies hold the saved tile and the two differ by construction. The earlier version hard-coded `0x227d498` and was wrong on three maps; it was fixed in `2ac4acd` and re-checked on the three scenes that use it (Lance, Kimono, Mt. Moon).

### What counts as proof at each rung

1. **Boots, correct spawn.** The save loads and the live location equals the intended tile. 80 of 80 scenes do (`docs/scene-boot-matrix.md`).
2. **Reaches the overworld.** Continue-to-overworld with screenshots. Violet, Lugia and Kanto fixtures, and the scene proofs.
3. **Encounter fought.** The player walks to the actor, presses A and the battle begins. Done for all 16 gym leaders, all 5 Elite Four rooms, Ho-Oh, Lugia, Kyogre, Groudon, Rayquaza, Suicune, Articuno, Zapdos, Moltres, Mewtwo, Latios, Latias and Red (13/13), and the Mt. Moon, Azalea, Goldenrod Tunnel, Petrel (Radio Tower and Rocket HQ), Archer, Kimono chain, Burned Tower, Victory Road 3F and Indigo Plateau entrance story battles (`docs/archive/runtime-results.md`). Each row carries a frame number and the enemy or trainer string that was on screen.
4. **Not done**: nothing at the story-battle level. One scene cannot arm in this harness: `event-league-gate-rival` arms its coord trigger only for `GetWeekday` 1 or 3 (`scr_seq_0819_T10R0101.s:34-47`), and the headless clock is fixed at 2000-01-01 with week byte 6 (`VAR_TEMP_x4000 = 6`, read from the live vars array), so the entry script clears `VAR_UNK_4119` every time. The fight was proved through the `VAR_UNK_40FC = 1` shortcut described in `docs/archive/runtime-results.md`. A scene's gate logic comes from source, so a boot row is evidence the state loads, not that the event plays.

Event movies are observed from screenshots, not asserted frame by frame.

## Lessons that were expensive

Each is recorded where it happened; this is the short list.

- **Stale screens.** The bottom-screen capture can show the start menu while the game is not in it. The HGSS bottom screen always shows menu buttons in overworld play. A "menu will not close" diagnosis on the Kyogre run was a stale capture; the Down press that exposed it moved the player out of the room. Check the full screen, and check that a D-pad probe actually moves the player before drawing conclusions. A menu is open when the button grid has a red cursor highlight; the identical grid without a highlight is closed overworld UI. Top-screen hashes cannot see the menu.
- **Exit menus with X, B, B, B, B.** X toggles and B backs out, so the sequence converges to closed from any depth up to four.
- **`X` opens the field menu. `Start` only works at the title screen.**
- **`up` is z−1.** The move menu is a 2×2 grid ("down" from move 1 lands on move 3).
- **In-game save with the buttons.** `X` to open, move the cursor to SAVE, then A for the panel, A to confirm, A at the overwrite prompt after the text finishes. Which d-pad path reaches SAVE depends on where the cursor was left; the measured path from a fresh menu open is `right` (cursor to `Ash`) then `down` (SAVE), because `right` alone lands on the trainer card. Pin the path to the frame that proved it.
- **The `.sav` is not readable the instant the game says it saved.** The manual melonDS front end writes the battery file asynchronously: 300 frames after the overwrite prompt the file on disk was a torn image (partition A's general block with a CRCs mismatch and pre-battle flags) and the settled file appeared about 60 seconds later with a different SHA-256. Always wait for the file's hash to stop changing before diffing, or the diff describes a half-written save.
- **Staging a different save needs a different ROM path.** `load_rom` on the path that is already loaded returns `ok` without restarting, and `reset_emulation` restarts the game but keeps the save image it loaded earlier - a reset with a swapped `heartgold.sav` still played the old party, flags and money. Re-stage under a second directory (a junction to the same ROM costs no disk) or restart the emulator process.
- **Scripted walking does not trip coordinate triggers.** Triggers are evaluated against real steps; spawn so the player must walk onto the strip.
- **Map-init scripts run after the save loads.** They can move the player (Lance's room ends on (6,22), the Kimono theater on (7,7), Mt. Moon on (5,4)); `expect.spawn_after` records the post-load tile.
- **A multi-member party against a trainer with two or more Pokémon raises the Shift prompt**; a zeroed nickname renders as garbage and wedges the message box. Use a single-member party.
- **Staged party members come from `party create`, not from a file in `%TEMP%`.** `hgss-save party create --species N --level N --out FILE.pk4` writes the 236-byte party form with the species' own level-up moves (so move slot 1, which a bare A press selects, is not another species' Fire move), and it is byte-for-byte reproducible: the build that produced the recorded fight proofs reproduces `AF2E7616...` for Typhlosion Lv100 with `--moves 53,247,89,7 --ot TEREZI`. Import it with `party import` as usual.
- **Save state is not step history.** The Location struct is a warp record, not a live position feed, on some maps (Route 25 stayed at (1407,38) while the player stepped); in interiors it tracks steps. It answers "where did this save spawn".
- **Wild encounters look like walls.** A battle freezes the coordinates at the entry tile. Use Max Repel when mapping routes, especially on water.
- **Long holds can freeze.** One combined run moved on the first hold then stopped. Hold inputs of at most 60 frames and save a state per leg. Mechanism unknown.
- **Menus differ in input timing.** The start menu single-steps on 2-frame holds; the bag list ignores them and needs 6-8 frames. Touch taps are exact in both but did not work on SAVE.
- **Never type a hash.** A hand-copied SHA-256 lost a character; always compute it.
- **Validate JSON immediately after writing recipes**; stray commas broke recipes twice.
- **Read the decomp before probing the emulator.** Zone-event JSON replaced about 20 blind maze legs; reading the fly-map code replaced about 20 blind cursor probes.
- **Scan on the right warp field.** The warp id at struct offset +4 is `0xFFFFFFFF` on some outdoor maps and a real id (often 0) elsewhere. Scanning for the sentinel only caused the false claim that interiors drop the struct.

## What `docs/archive/runtime-validation.md` is

An early manual checklist for melonDS written before the headless harness existed. It says there is no headless mode; that is no longer true. Use it only for the manual save-and-diff procedure and the failure triage. This file and `docs/archive/runtime-results.md` describe the current process.
