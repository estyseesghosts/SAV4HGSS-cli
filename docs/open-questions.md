# Open work, limits, and corrections

Two lists. The first is what is not known or not done, with what would unblock it. The second is claims made earlier in the project that were later found wrong, so nobody cites the old version.

## Open work

### Not done, and why it matters

| Item | State | What unblocks it |
|---|---|---|
| Arbitrary X/Y placement | `location warp MAP -1 --x X --y Y` spawns on any tile but does not check walkability or elevation; `location set` stays allowlisted | Walkability check (`--probe-tile`: report the four neighbours' blocked bit) |
| Collision calibration (F15) | The overworld splits into 521 components; block joins are missing from the tile attributes as read. `tile_path.py` was deleted after six probes disagreed with its land-data model in four places | The real join rule (script matrix edits, land-data object section, or a loader detail); all 26 played positions must be reachable before any verdict ships |
| Coordinate triggers | Frame triggers fire on load; coordinate triggers need the player to stand on a tile (F11, F22) | Walkability, or walking in the emulator |
| Out-of-order badge sets | `story build` gives badges their gym consequences but not scene variables, NPC flags or HM grants for the world to match (F13) | Prerequisites from the map graph, which needs F15 |
| One HG playthrough | Cannot separate story from player choice (F1) | A second HG run dumped at the same milestones |
| Chikorita start | No save with starter 152 | Play the intro and save at Cherrygrove |
| Story battles not fought | **Closed.** Every story battle in `docs/archive/state-log.md` has been fought from its scene warp or a composed state and diffed against the game-written save: Mt. Moon, Azalea, Goldenrod Tunnel, Burned Tower, Victory Road 3F, the League entrance, Petrel (Radio Tower and Rocket HQ), Archer and the Kimono chain. The League-entrance fight was reproduced through the `VAR_UNK_40FC = 1` branch because the headless clock never reaches that scene's weekday (see the RTC row) | — |
| Rocket HQ door opens on the second press | On the flags-set save the first A press on the (23,15) door takes the closed branch and the second opens it. Nothing in the script can change between the two: `TrainerFlagSet` is only called from a won battle (`src/scrcmd_battle.c:249`) and `ScrCmd_SetTrainerFlag` (`src/scrcmd_c.c:524`) | A breakpoint on `ScrCmd_CheckTrainerFlag` (`src/scrcmd_c.c:537`) reading the argument and `comparisonResult` on both presses |
| Lake of Rage pre-Gyarados | No fixture. The trek reached Route 43 (524,149) mid-battle | Resume from `hg-6gyn-surf.sav` and the banked `.mcpr` anchors (local `leveld-out/`) |
| Unmodelled general-block tables | `0x23B8-0x2740` and `0x4434-0x4510` (F6) | Compute chunk sizes from decomp structs and place each cluster |
| Named flags | Unnamed flags that move between HG milestones (`tools/unk_flags.py`) | Attribution from the writer script plus runtime tests |
| Actor checker | **Closed.** The walker now records `CheckBattleWon` in the path and the negated `Compare` for every conditional fall-through, so the Mt. Moon write at `scr_seq_0007_D02R0101.s:52` reports the win gate instead of looking unconditional. It also reports map-init writes to variables that the map's own coord triggers test, which is the League-gate failure in source form: `T10R0101` ontransition 47 sets `VAR_UNK_4119 = 0` with guards `VAR_UNK_40FC != 1`, `FLAG_UNK_AA9`, weekday != 1, weekday != 3. Same 224 flag writes across 80 scenes; 10 scenes and 17 variable writes are new | — |
| `variable.set VAR_TEMP_*` | **Closed.** The named form applies with the same map-load warning as `variable.set 0x4000` (`PatchPlanner.cs:229-246`); the message now says only special vars (`0x8000+`) are excluded. `diff`, `vars list` and `var get` resolve temp-var names too (`SymbolRegistry.VarNameFor`) | — |
| Facing field | The u16 at +16 of the live `Location` lagged once | More observations |
| Unexplained tooling | A long combined input hold freezes after the first hold | Root cause unknown; use short holds |
| Boot timing | Boot is frame-counted (logo at ~4,400, Continue menu at ~4,600), landing A during the fade at least three times | Poll for the title logo / Continue menu state instead of counting frames |
| Most of the research plan | 58 of 98 questions untouched | `docs/research-questions.md` |
| Lugia party precondition | The Lugia proof depended on an operator-imported Shellder with Surf; the scene does not encode a party precondition | A Surf-capable party check in the scene audit, or a note in the scene |
| Progress presets must clear as well as set | Open since the corpus was built; needs a second HG playthrough at the same milestones | A scheduled play session, or decide to answer it from source instead |
| Victory Road rival ids | **Closed.** The League-entrance scene is `event-league-gate-rival` (map 300); `event-rival-victory-road` keeps map 179 (Victory Road 3F). The old id differed only in word order while naming a different map | — |
| Emulated RTC weekday | **Measured.** The headless core never syncs the DS clock to the host: the savestate's RTC section reads `2000-01-01 04:07:01`, week byte 6, and the game agrees (`VAR_TEMP_x4000 = 6`, a value only `GetWeekday` writes in `scr_seq_0819_T10R0101.s:34`). melonDS counts the week byte from 0=Sunday (melonDS `src/RTC.cpp:176`) with 2000-01-01 as 6 (`:186-187`), so it is Saturday and `event-league-gate-rival`'s gate days (1 and 3) are Monday and Wednesday; its coord trigger cannot arm in this harness | A core that syncs or exposes the RTC. melonDS's frontend path would report 3 for a 2026-11-18 host (`EmuInstance::setDateTime`, `src/frontend/qt_sdl/EmuInstance.cpp:1206-1214`), but the MCP core does not call it |
| Boot matrix regeneration | Generated from run summaries; regenerated 2026-11-18 to fold in the Latios/Latias re-boot, but it must be regenerated as part of every registry commit | Make the matrix part of the registry commit path |

### Deliberate refusals

These are rules, not gaps (`docs/archive/story-state-field-knowledge.md` §6):

- Backward resets ("un-beat Clair to restage Ecruteak") fabricate states the scripts never produce.
- Unverified warp indices (exit 8 `warp-unverified`). A `warpId == -1` free spawn is allowed; the tile is the caller's claim to verify in-game (F19).
- A walkability verdict from undecoded data.
- Mid-cutscene `.sav` states do not exist; use savestates.
- Granting a key item whose give-flag or counter would disagree.

### Documentation state

- Counts in older docs drift. `README.md` said 54 tests and `docs/artifacts-and-tools.md` said 69; the suite is 73. `docs/archive/coverage-gaps.md` and `docs/archive/story-state-field-knowledge.md` say 19 safe spawns (the file holds 20); `docs/archive/state-log.md` F9 says 17 presets (there are 18). The generated files and tools hold the current numbers.
- Raw agent transcripts at the repository root (`chatlog00.*`, `chatlog01.md`) are git-ignored and kept for provenance. `chatlog06.md` (the 2026-11-18 session record, 4 h, one build-agent session that added the scene layer) lives at the workspace root, one directory above this repo, not in it.

## Corrections

Claims that were stated, later disproved, and where the fix is.

| Claim | Correction | Where |
|---|---|---|
| The save cannot encode actors, warps and script context (`docs/archive/coverage-gaps.md`) | Mostly encoded: five locations, 64 map objects, a Continue path from the dynamic warp | F4, F5 |
| The corpus has "71 saves" | 29 unique HG/SS saves; the figure counted every file | State log census |
| The HG Master lineage has 18 saves | 11 unique | `research/corpus-census.json` |
| Trainer flags are unnamed | `flags.h` defines them as a range; 733 names derived | F2 |
| Fixture boots prove overworld reachability | A 700-frame boot is the title screen; spawn matches there prove load, not overworld | `docs/archive/story-state-field-knowledge.md` §8 |
| The D-pad is broken | Two misreads: a stale mirror and a static menu UI | §3, §5 item 9 |
| Interiors drop the Location struct | The scan keyed on the `0xFFFFFFFF` warp sentinel, which interiors do not use | `docs/archive/session-log.md` |
| The live player struct is at `0x227d498` | The address varies by map | `docs/archive/runtime-results.md`, `docs/verification.md` |
| `D43R0103` is Goldenrod Underground, then Victory Road 2F | Map 179 `MAP_VICTORY_ROAD_3F`; the Goldenrod rival is `D37R0104`, map 201 | F16 |
| `spawn_registry_check.py` is a valid check of the warp table | It compares warp tiles with free-roam positions and explains 0 of 20; runtime probing is the right test | F5 |
| Mt. Silver needs `FLAG_UNLOCKED_MT_SILVER` | Five-write chain | F18 |
| `needs` edges in `story-atoms-hg.json` are prerequisites | File-level reads, not guards | F14 |
| `docs/archive/SAV4HGSS.md`: TM/HM pouch is 100 slots | 101 | `docs/save-format.md` |
| `docs/archive/runtime-validation.md`: no headless mode | `tools/leveld_run.py` exists | `docs/verification.md` |
| Rotom is a scene | Not producible from flags alone; the map rewrites every appliance flag on transition | F17 |
| Kyogre/Groudon/Rayquaza/Articuno/Zapdos/Moltres/Mewtwo/Suicune/Latios/Latias/Lugia are not fought | All 13 fought from composed saves on 2026-11-18 | `docs/archive/runtime-results.md` |
| The Kimono battle is five separate encounters | One script run; `scr_seq_0928_T27R0501.s` `_016` chains all five, sets `VAR_UNK_40FA = 1` (the Lugia gate) | F23 |
| "The player is stuck inside raised geometry" in Cianwood | The winch cutscene holds input; a negative movement result is not map data while input is locked | F21, `docs/archive/runtime-results.md` |
| The start menu was stuck open on the Kyogre run | A stale bottom-screen capture; the decisive test is the live Location across a d-pad press | `docs/archive/runtime-results.md` |
| `VAR_TEMP_*` / temp vars are not persistent save fields | They are in `SaveVarsFlags.vars[]`; they are zeroed on map load | F21, F23 |
| The Kimono save sequence is `X → right` (or `X → right → down`) | The cursor position is not pinned to a frame in either record; measure the menu instead of guessing the path | F23 |
| `docs/runtime-results.md` is the live path | It moved to `docs/archive/runtime-results.md` | docs reorganisation |
| `cli-reference.md`: arbitrary tiles are impossible | Free spawns ship as `location warp MAP -1 --x X --y Y` | F19 |
