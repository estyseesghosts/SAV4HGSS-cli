# Findings index

One row per finding, with the result in a sentence, the kind of evidence, and where the full account lives. The accounts themselves stay in their original files because the generated registry (`registry/hgss-us.scenes.json`) and other docs cite them by path and number (`docs/archive/state-log.md F14`, and so on). Read `docs/story-state-model.md` first for the connected explanation; use this file to find a specific result.

Evidence labels: SOURCE (decomp lines), SAVE_DIFF (compared real saves), INTERVENTION (edited copy), RUNTIME (observed in melonDS), NEGATIVE (predicted change did not occur). See `docs/archive/hgss_save_state_research_plan.md` §2.

Status: **holds** (no later contradiction), **corrected** (a later entry changed it), **open** (partial or unresolved).

## A. State log (`docs/archive/state-log.md`)

| ID | Result | Evidence | Status |
|---|---|---|---|
| Census | 42 files of `0x80000` bytes; 29 unique HG/SS saves, 12 rejected (Pearl, Platinum, White). One save per milestone per lineage. Several filenames mislabelled | SAVE_DIFF | holds. The earlier "71 saves" counted every file |
| F1 | Progress is not monotonic: 2-9 flags clear at every step, mostly `FLAG_HIDE_*` (set = hidden) | SAVE_DIFF | holds |
| F2 | Flag families by count; 1,659 of 1,660 unnamed set flags were trainer flags defined only as a range; registry now derives 733 `FLAG_TRAINER_*` names | SOURCE, SAVE_DIFF | holds, registry fixed |
| F3 | HG and SS share a story core (252 of 268/284 flags at 4 badges); the difference is optional and version-exclusive state | SAVE_DIFF | holds, one milestone compared in depth |
| F4 | The save stores actors and position state: five `Location`s, music, weather, spawn, 64 map objects | SOURCE, SAVE_DIFF (29 saves) | holds. Corrects `docs/archive/coverage-gaps.md` |
| F5 | Continue has a second path: flag `0x966` plus `dynamicWarp` spawns the player on a warp event tile with fresh actors; no walkability needed | SOURCE, RUNTIME (7 targets, HG) | holds. Shipped as `location warp`. SS and long play after the first frame untested |
| F6 | General-block areas the tool does not model, by offset cluster | SAVE_DIFF | open: `0x23B8-0x2740` and `0x4434-0x4510` unidentified |
| F7 | Flag and variable deltas replay exactly across all 10 HG steps; 0 differing flags | INTERVENTION | holds |
| F8 | Badges drive three layers: gym-script consequences, the `VAR_MIDGAME_BADGES` counter, scene machines | SOURCE, SAVE_DIFF | holds. Layers 1-2 implemented in `trainer badges set` |
| F9 | All 17 presets (18 now) boot at the intended tile; a flag replay without badge consequences boots but is incoherent | RUNTIME | holds. Boot proves load and position only |
| F10 | The counter rule starts the Rocket takeover at runtime; leaving it at 3 does not | RUNTIME (A/B) | holds |
| F11 | 315 cutscene triggers (187 coordinate, 128 frame) over 169 variables, shipped as a registry | SOURCE | holds. Coordinate triggers not automated |
| F12 | HM01/02/03/08 flag and item always agree (29/29); field moves are badge-gated in `field_move.c` | SOURCE, SAVE_DIFF | holds. HM04-07 have no grant flag |
| F13 | `story build`: preset base plus badge consequences; out-of-order badges get no scene variables or HM grants | INTERVENTION, RUNTIME | holds, limits stated |
| F14 | No story guards on badge or HM award scripts; order lives in access and triggers | SOURCE (path walk) | holds. Supersedes the `needs` edges in `story-atoms-hg.json` |
| F15 | Map reachability pipeline built; collision semantics not calibrated (521 components, joins between blocks missing) | SOURCE, SAVE_DIFF | open. `tile_path.py` deleted after six probes. Free spawns shipped instead (F19); collision only still blocks the per-gym field-move gate analysis and walkability checks |
| F16 | 80 scenes composed from preset, badges, scene ops, warp; 80/80 reach their tile | RUNTIME | holds. Table of story battles corrected (see `docs/open-questions.md`) |
| F17 | Map-init scripts decide actor visibility and can move the player; version locks for legendaries | SOURCE | holds. The `CheckBattleWon` over-report is fixed: the walker records the opcode and the negated fall-through guard |
| F18 | Mt. Silver and Red are a five-write unlock chain | SOURCE, SAVE_DIFF (end state only) | holds. Scenes rewritten to carry it |
| F19 | The dynamic warp can spawn on any tile: `warpId == -1` keeps stored x/y (`field_warp_tasks.c:206-209`) | SOURCE, RUNTIME (7 maps) | holds. Shipped as `location warp MAP -1 --x X --y Y`. Tile walkability unchecked |
| F20 | Latios and Latias are gated on `VAR_UNK_4043 == 0x06F2`, the Mystery Gift Enigma Stone var; the item alone is silent | SOURCE, RUNTIME (both fought) | holds. Both scenes write `VAR_UNK_4043 = 1778` |
| F21 | Cianwood's winch is a save-block gimmick wiped by the map's own init (`Save_Gymmick_Init`); `VAR_TEMP_x4000` is stored but zeroed on load; the waterfall flag is sound-only | SOURCE, RUNTIME | holds. Chuck beaten by turning the winch in-session. The planner's refusal message is wrong (see `docs/open-questions.md`) |
| F22 | E4 rooms run an intro from the frame table that walks the player; scripted walking does not trip coordinate triggers | SOURCE, RUNTIME | holds. Lance's battle is a coordinate trigger, not an object script |
| F23 | The Dance Theater is one script run (five `TrainerBattle`s); the in-game save proves its delta, including `VAR_UNK_40FA = 1` (the Lugia gate) | SOURCE, SAVE_DIFF (in-game save) | holds. Temp vars are in `SaveVarsFlags.vars[]` |

## B. Research findings (`docs/archive/research-findings.md`)

| Topic | Result | Evidence | Status |
|---|---|---|---|
| Lake of Rage gate | Red Gyarados is present by default; `FLAG_GOT_RED_SCALE` clear = pre, set = post; HG 6 badges is pre, 7 badges post. Corpus has no save at the lake | SOURCE (`scr_seq_0938_T29.s`), SAVE_DIFF | holds. No `hg-lake-pre-gyarados` staged |
| Interior Location structs | Outdoor pattern with the `FFFFFFFF` sentinel loses the player on entering an interior; interiors use real warp ids | RUNTIME | corrected: the "interiors drop the pattern" claim was a scan limitation (`docs/archive/story-state-field-knowledge.md` §7) |
| Bag input timing | In the bag list, 2-frame d-pad holds are ignored, 6-8 frames step; touch taps are exact | RUNTIME | holds, tooling note |
| Ecruteak east exit | The only exit is the Route 42 gatehouse at (415,175-176); the y=184 road dead-ends at a tree wall | SOURCE, RUNTIME | holds |
| Move-teach synthesis | Surf taught to a Mantine by export, one PKHeX edit (Move3 97 to 57, PP 15), import with `--expect-species`; 130 data and 2 checksum bytes changed, 0 unclassified | INTERVENTION, RUNTIME | holds. First approved synth |
| Olivine pre-Jasmine (COH-01) | HG 5 badges Cianwood to Olivine needed only `location.set`; flags were identical | SAVE_DIFF, INTERVENTION | holds |
| Lake trek | Reached Route 43 (524,149) mid-battle, then the session ended; no fixture | RUNTIME | open |
| Mt. Silver and Red (STORY-08) | Five-write chain | SOURCE | holds (same as F18) |

## C. Field knowledge (`docs/archive/story-state-field-knowledge.md`)

| Topic | Result | Evidence | Status |
|---|---|---|---|
| Live Location struct | Five ints in ARM9 RAM; same layout as the save's `Location`; found by differencing two overworld dumps | RUNTIME | holds. Address varies by map (`docs/verification.md`) |
| Save Z is not direction | Z=4 and 7 occur in saves; directions are 0-3 | SAVE_DIFF, SOURCE | holds |
| Stale mirrors | After a walk only the primary copy moves; the mirror at `0x239aa40` keeps the old tile | RUNTIME | holds. Spawn assertion needs at least two agreeing copies on a fresh boot |
| Same-map NPC structs | NPCs share the struct shape; map-only agreement is not enough | RUNTIME | holds |
| Facing field | The u16 at +16 did not track facing in one run | RUNTIME | open; never gate on it |
| NO$GBA and DeSmuME containers | RLE-compressed NO$GBA data decodes with `ndssc`; `.dsv` has a `0x7A` footer | SAVE_DIFF | holds |
| Gate shapes | Gym ladder via TM flags; legendary pre-states via caught + hide + scene; event movies via hide-flag pairs | SOURCE, SAVE_DIFF | holds. Key-item grants allowed only where the item's only script touchpoint is the gate's `HasItem` |
| Play-to-fill loop | Walk, in-game save by buttons, extract the battery, `inspect`, `diff`: only played deltas appear | RUNTIME | holds |
| Fly map | Cursor moves tile by tile; hit test at (playerX, playerY-2) while description reads (playerX, playerY) | SOURCE (`overlay_101`, `fly_map.c`), RUNTIME | holds |
| Surf mechanics | Face water and press A; decorative ponds give no prompt; wild encounters freeze coordinates and look like wall bumps | RUNTIME | holds |
| Boot timing | A 700-frame boot reaches the title screen, not the overworld; the overworld needs about 4,200 frames and title navigation | RUNTIME | corrected: earlier "boot = overworld" readings were title-screen RAM |

## D. Runtime results (`docs/archive/runtime-results.md`, `docs/scene-boot-matrix.md`)

| Result | Evidence |
|---|---|
| All 30 fixtures boot headless; Violet, Lugia and Kanto fixtures continue to the overworld and many assert the live spawn (`docs/archive/runtime-results.md`) | RUNTIME |
| 80 of 80 scenes reach their tile on the correct ROM (78 HG, 2 SS) | RUNTIME (generated matrix; regenerated 2026-11-18 to include the Latios/Latias re-boot) |
| All 16 gym leaders fought from composed saves | RUNTIME (frame numbers, `docs/archive/runtime-results.md`) |
| All 5 Elite Four rooms fought from composed saves | RUNTIME (frame numbers, `docs/archive/runtime-results.md`) |
| Legendaries and Red fought from composed saves: Ho-Oh, Lugia, Kyogre, Groudon, Rayquaza, Suicune, Articuno, Zapdos, Moltres, Mewtwo, Latios, Latias, Red (13/13) | RUNTIME (frame numbers, `docs/archive/runtime-results.md`) |
| Story battles fought: Mt. Moon rival, Azalea rival, Goldenrod Tunnel rival, Petrel (Radio Tower), Archer, Kimono chain (five fights in one cutscene), Burned Tower rival, Victory Road 3F rival, Rocket HQ Petrel, League-entrance rival | RUNTIME (frame numbers) |
| League-entrance rival gate | Fought from a `VAR_UNK_40FC = 1` test state. The scene's real state (`40FC = 2`) never arms: the headless clock reports weekday 6, and `scr_seq_0819_T10R0101.s:34-47` arms only for 1 or 3 | RUNTIME |
