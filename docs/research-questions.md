# Research questions: what each one answers

`docs/archive/hgss_save_state_research_plan.md` §5 defines 98 questions with stable IDs. This file records, for each, what the repository currently answers. Nothing here is new research; every row points at an existing finding (IDs in `docs/findings.md`, or the file named).

Status values:

- **Answered**: the question's decisive experiment or an equivalent source trace was done and the result is recorded.
- **Partly**: some of the question is answered. The row says which part.
- **Not investigated**: no work in the repository addresses it. The plan's wording still applies.

A question marked Answered is answered for US HG/SS and the corpus in `docs/corpus.md`. Where only one playthrough or one version was checked, the row says so. The project was driven by a practical goal (generate verified saves for the Kohjoh port), so large parts of the plan's long tail (clock, Safari, Pokéathlon, Pokéwalker, hardware) were never started.

Tally at the end of the file.

## A. Save mechanics (FMT)

| ID | Status | What we know |
|---|---|---|
| FMT-01 | Not investigated | No corruption experiments on the game. PKHeX's selection rule is documented (`docs/save-format.md` §2): counters decide, checksums do not. The CLI refuses a save whose active copy is invalid instead of falling back. |
| FMT-02 | Partly | Documented from PKHeX source that general and storage can be active in different partitions; the CLI selects each independently. A played save flipped the active partition A to B (`docs/archive/story-state-field-knowledge.md` §8). A corpus save with general and storage in different partitions has not been recorded. |
| FMT-03 | Partly | One in-game save compared with its staged source: 925 changed bytes, only play time, location, `VAR_FRIENDSHIP_STEPS` and the partition flip. No separate no-op `S0→C1→C2` control. |
| FMT-04 | Partly | PKHeX export is not byte-identical to its input (changed-box bits `0x3FFFF`, backup CRCs, footers). The CLI derives that baseline from PKHeX and audits every other byte. A game load-and-resave of a PKHeX export was not compared. |
| FMT-05 | Partly | Handled by design: strict open requires both active copies intact and the writer rewrites through PKHeX. Not tested against the game with distinct active copies. |
| FMT-06 | Not investigated | Auxiliary blocks are located and checksummed (`docs/save-format.md` §2); recovery behaviour not tested. |
| FMT-07 | Not investigated | |
| FMT-08 | Not investigated | |

## B. Lifetimes (LIFE)

| ID | Status | What we know |
|---|---|---|
| LIFE-01 | Partly | SOURCE: 64 map-temp flags from id `0x1`, cleared on map transition (`flags.h:15-16`, `ClearTempFieldEventData`). The registry classifies them and the CLI rejects writes. Which are written by scripts, and reset timing, was not traced. |
| LIFE-02 | Partly | Temp vars `0x4000-0x401F` exist in the save array and hold values in saves (`0x4000` takes 0-7 across the HG lineage, F7). The CLI classifies them non-writable and excludes them from replays. Per-slot survival and consumers not traced. |
| LIFE-03 | Partly | Special vars start at `0x8000` and are not in the save (14 registry symbols); saved vars are `0x4000-0x416F`. No script-time experiment. |
| LIFE-04 to LIFE-08 | Not investigated | Daily flags are `0xAA0-0xB5F` (`flags.h:2027-2028`); presets leave `FLAG_DAILY_*` as the base has it. No clock experiments. |

## C. Positions, warps, objects (MAP)

| ID | Status | What we know |
|---|---|---|
| MAP-01 | Answered | Position state is five `Location`s (current, entrance, previous, dynamic warp, special spawn), music, weather, blackout spawn and 64 saved map objects (F4). The minimum for a safe placement is a warp: flag `0x966` plus `dynamicWarp` (F5); `warpId == -1` keeps stored x/y, so the same path spawns on any tile (F19). Tile walkability is not verified. |
| MAP-02 | Partly | Editing current M/X/Y alone leaves the other four locations and actors inconsistent (F4). The two Continue paths are documented from source (F5). 20 verified spawn tiles reload with matching live Location. Interior placement by `location.set` was found to need its own pattern work (`docs/archive/research-findings.md`). A camera/follower/object comparison against a real warp was not made. |
| MAP-03 | Partly | Map header frame triggers fire on load; shown by F10 (takeover call plays on warp into Mahogany) and F17 (init scripts move the player in 22 of 80 scenes). Three entry paths for one map were not compared with a script log. |
| MAP-04 | Partly | Actor visibility is flags (`FLAG_HIDE_*`), but map init can override them (F17). `SAVE_MAP_OBJECTS` is documented from source; the matching save bytes are not attributed (F6). |
| MAP-05 | Partly | Save Z is not facing (Z=4, 7 seen; directions are 0-3). Facing and elevation effects on events were not tested. |
| MAP-06 to MAP-08 | Not investigated | |

## D. Johto and Kanto story (STORY)

| ID | Status | What we know |
|---|---|---|
| STORY-01 | Partly | `VAR_PLAYER_STARTER` is 155 (Cyndaquil) on the Master/M@ster/Ash/Desmond saves and 158 (Totodile) on Lyra/NotKris; no Chikorita save exists (`docs/archive/coverage-matrix.md`). Starter and first-battle writes were not diffed. |
| STORY-02 | Answered | Three layers: badge byte, gym-script consequences, the post-badge world. Gym scripts set a TM flag and trainer flags after `GiveBadge`; effects are in `research/badge-effects.json` and `hgss-us.badge-rules.json` (F8, F14). |
| STORY-03 | Answered | The takeover starts when a map header sees `VAR_MIDGAME_BADGES == 4` (F8, F10, runtime-checked). |
| STORY-04 | Answered | Corpus values 0, 1, 2, 4, 5; transitions to 2, 3, 4 and writers/readers are mapped (F8, F11). `Save_VarsFlags_IsInRocketTakeover` recognizes 2-4 (plan). |
| STORY-05 | Partly | The Kimono sequence runs at `VAR_UNK_410C == 4` from the theater header (`scr_seq_0701_T27R0501_hdr.s:12-14`) and writes through 6 (`scr_seq_0928_T27R0501.s:717,865`); a scene reproduces it. Post-Rocket prerequisites for the whole chain were not fully traced. |
| STORY-06 | Partly | League reset flags clear after the Hall of Fame (F1); `FLAG_GAME_CLEAR` is read by five gym scripts; flag `0x966` is set once after the Hall of Fame (F5). Credits-timing saves not captured. |
| STORY-07 | Partly | `FLAG_UNLOCKED_WEST_KANTO` is set in Viridian (F18). Map unlock bits at `0xBAE7` are documented, not tested. Ship-access flow not traced. |
| STORY-08 | Answered | Five-write chain from Blue's gym through Oak, Route 22 guards and Red (F18). Omit-one tests not run. |
| STORY-09 | Not investigated | |
| STORY-10 | Partly | `registry/hgss-us.scene-triggers.json` enumerates every `VAR == value` trigger over 169 variables (F11). Whether each value is saveable was not checked. |

## E. Ho-Oh, Lugia, Kimono Girls (LUG)

The repository stages Lugia and Ho-Oh states and checks their gates. It does not answer what the unknown flags mean.

| ID | Status | What we know |
|---|---|---|
| LUG-01 | Partly | Gate at `scr_seq_0884_T24R0801.s:46` (`HasItem ITEM_SILVER_WING`); fixture `ss-lugia-pre-event-stage` and scenes boot. The omit-one-prerequisite experiments were not run. |
| LUG-02 | Partly | The five Kimono fights are one script run (`scr_seq_0928_T27R0501.s`, `_016`) with `VAR_UNK_410C == 4`; the run sets `VAR_UNK_40FA = 1` (the Lugia gate) and `VAR_UNK_410C` to 6 (F23). The exact per-sister write ordering inside a mid-chain loss was not traced. |
| LUG-03 | Not investigated | `FLAG_UNK_10B` meaning not determined. |
| LUG-04 | Not investigated | `FLAG_UNK_109` meaning not determined. |
| LUG-05 | Not investigated | Scene logic clears the hide flag and `FLAG_ENGAGING_STATIC_POKEMON` for Lugia (F17); the factorial test with `FLAG_CAUGHT_LUGIA` was not run. Lugia itself was fought from a composed save with an operator-imported Surf Shellder at frame 9263. |
| LUG-06 | Partly | Temp vars are stored in the save (`SaveVarsFlags.vars[]`, `0x4000` base) but zeroed by `ScriptManager` on map load (`script_manager.c:406-407`); `VAR_BATTLE_RESULT` survived an in-game save (F23). Which temp vars drive camera sync was not annotated. |
| LUG-07 | Partly | The Kohjoh crash was not reproduced or localized in this repository. Lugia was triggered from a composed save on 2026-11-18 (frame 9263, `LUGIA Lv70`); the room required a Surf-capable party. |
| LUG-08 | Not investigated | |
| LUG-09 | Partly | Version locks: Kyogre HG, Groudon SS, Latios HG, Latias SS, Rayquaza both HG and SS while `VAR_SCENE_EMBEDDED_TOWER < 7` (F17). Ho-Oh is HG-only and the HG room is the summit; the SS equivalent was not read. Parallel CFG comparison not done. |
| LUG-10 | Partly | Composed saves reach the Ho-Oh, Kyogre, Groudon, Rayquaza, Suicune, Articuno, Zapdos, Moltres, Mewtwo, Latios, Latias, Lugia and Red battles by warp, not by the legitimate route; they behave correctly. A cutscene-setup comparison was not made. `VAR_UNK_4043 = 0x06F2` is required for the Latis actors (F20). |
| LUG-11 | Not investigated | |
| LUG-12 | Not investigated | |

## F. Unknown flags and variables (SYM)

| ID | Status | What we know |
|---|---|---|
| SYM-01 | Answered for scripts | `registry/hgss-us.script-refs.json`: 9,960 references over 965 symbols from `scr_seq/*.s`; `research/script-graph.json` has 879 written and 439 read symbols. Native C helpers are not indexed. |
| SYM-02 | Partly | Operations per variable are in the index; classification into stage, boolean, counter, graphics id was done for specific variables (`VAR_MIDGAME_BADGES` is a derived counter; `VAR_UNK_4121` is a single-reader switch). |
| SYM-03 | Partly | Co-writes and co-reads are in `story-atoms-hg.json`; F14 limits what those edges mean. `VAR_UNK_40AC` and `VAR_UNK_410C` are shown to be checkpoint sequences. |
| SYM-04 to SYM-09 | Not investigated | `tools/unk_flags.py` lists unnamed flags that actually move between HG milestones (`docs/archive/state-log.md`, open item 4), which feeds SYM-04 but does not answer it. |

## G. Pokémon, followers, battles (PKM)

| ID | Status | What we know |
|---|---|---|
| PKM-01 to PKM-05, PKM-07 | Not investigated | |
| PKM-06 | Not investigated | Party and box import verify the PK4 checksum and species; no transfer normalization test. |

## H. Items (ITEM)

| ID | Status | What we know |
|---|---|---|
| ITEM-01 | Partly | Silver Wing and Blue Orb: the item's only script touchpoint is the gate's `HasItem`, so granting it fabricates nothing. The Blue Orb also matters to the Kyogre transition (`scr_seq_0134_D52R0102.s:36-38`). |
| ITEM-02 | Partly | HM item and flag agree in 29 of 29 saves (F12); HMs are given with their flag, TMs are consumed. A comparison of an edited bag against an organic gift over later play was not made. |
| ITEM-03 | Partly | The `scr_seq_0884_T24R0801.s` gate differs by version (Clear Bell/Rainbow Wing versus Tidal Bell/Silver Wing). Parallel checkpoints not collected. |
| ITEM-04 | Partly | Field moves require badges in `field_move.c`; HM and move ownership checks not tested one factor at a time. |
| ITEM-05, ITEM-06 | Not investigated | |

## I. Time and phone (RTC), J. Systems (SYS)

| ID | Status |
|---|---|
| RTC-01 to RTC-08 | Not investigated. `VAR_BUENAS_PASSWORD_SET`, `VAR_LOTO_NUMBER_LO` and `VAR_FRIENDSHIP_STEPS` appear in lineage diffs but were not analysed. |
| SYS-01 to SYS-08 | Not investigated. Offsets for roamers, Safari Zone, Pokéathlon, Pokéwalker and Mystery Gift are listed in `docs/save-format.md` §3 and §8, with no behaviour work. |

## K. Coherence (COH)

| ID | Status | What we know |
|---|---|---|
| COH-01 | Answered for cases tried | Olivine pre-Jasmine needed only `location.set` from a played save (`docs/archive/research-findings.md`). Badge sets need the three layers (F8). The preset-plus-warp composition is the working minimum for scenes (F16). |
| COH-02 | Partly | HG and SS compared at 1, 2, 4, 8 badges (F3, `docs/archive/state-log.md` open item 3). Two organic histories on one version do not exist in the corpus. |
| COH-03 | Not investigated | Boot checks prove load and position, not two further milestones of play (F9). |
| COH-04 | Not investigated | |
| COH-05 | Partly | A replay that disagrees on badges versus flags boots (F9); the game's correction behaviour was not observed. |
| COH-06 | Not investigated | |
| COH-07 | Partly | HG/SS flag-set comparison (F3); no version conversion is attempted, and the CLI rejects a save for the wrong ROM. |
| COH-08 | Partly | `story check` audits badge consequences, counters, HM grants (0 violations on the 29 saves and on all applied presets). A reject/warn/safe classification of arbitrary contradictory saves does not exist. |

## L. Measurement (TRACE)

| ID | Status | What we know |
|---|---|---|
| TRACE-01 | Not investigated | The index covers `.s` scripts only. No instrumented comparison against runtime reads and writes. |
| TRACE-02 | Not investigated | Changes are attributed to writer scripts by file (`tools/delta_by_script.py`), not by traced program counter. |
| TRACE-03 | Not investigated | No hardware run and no emulator-versus-hardware comparison. |
| TRACE-04 | Partly | The byte audit and 73 tests verify the CLI round trip against PKHeX; the game's own round trip was observed once (the play-to-fill loop). |
| TRACE-05 | Partly | `docs/archive/coverage-matrix.md` and `docs/archive/coverage-gaps.md` list missing branches (Chikorita, pre-Blue, pre-Red, Lake pre-Gyarados, Mewtwo and Groudon pre-states). |
| TRACE-06 | Partly | HG and SS were compared for the core; other languages were not. |

## Tally

Counted per ID (98 questions), since some rows cover a range:

- Answered: MAP-01, STORY-02, STORY-03, STORY-04, STORY-08, SYM-01, COH-01 (7).
- Partly: FMT-02 to FMT-05 (4), LIFE-01 to LIFE-03 (3), MAP-02 to MAP-05 (4), STORY-01, STORY-05, STORY-06, STORY-07, STORY-10 (5), LUG-01, LUG-02, LUG-06, LUG-07, LUG-09, LUG-10 (6), SYM-02, SYM-03 (2), ITEM-01 to ITEM-04 (4), COH-02, COH-05, COH-07, COH-08 (4), TRACE-04, TRACE-05, TRACE-06 (3) = 35.
- Not investigated: the remaining 56.

The practical result: the project answered the questions that block generating verified saves at story points (badges, the Rocket takeover, Red, positions via warps) and none of the long-tail systems.
