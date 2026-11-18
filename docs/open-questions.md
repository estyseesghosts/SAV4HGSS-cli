# Open work, limits, and corrections

Two lists. The first is what is not known or not done, with what would unblock it. The second is claims made earlier in the project that were later found wrong, so nobody cites the old version.

## Open work

### Not done, and why it matters

| Item | State | What unblocks it |
|---|---|---|
| Arbitrary X/Y placement | `location set` accepts only 20 verified tiles; `location warp` reaches any of 1,317 warp event tiles. "Any location" currently means any warp tile | Calibrate collision (F15): the overworld splits into 521 components because block joins are missing from the tile attributes as read. Needs the real join rule (script matrix edits, land-data object section, or a loader detail), then all 26 played positions must be reachable before any verdict ships |
| Coordinate triggers | Frame triggers fire on load; coordinate triggers need the player to stand on a tile (F11) | Walkability, or walking in the emulator |
| Out-of-order badge sets | `story build` gives badges their gym consequences but not scene variables, NPC flags or HM grants for the world to match (F13) | Prerequisites from the map graph, which needs F15 |
| One HG playthrough | Cannot separate story from player choice (F1) | A second HG run dumped at the same milestones |
| Chikorita start | No save with starter 152 | Play the intro and save at Cherrygrove |
| Lugia battle | Spawned and reachable on a composed save; the run stopped when a greedy walker oscillated at (16,26). Not fought | Plan the path with `tools/map_data.py` `tile_grid` (matrix 144, bit 15 blocked) instead of guessing directions |
| Other legendaries | Articuno, Zapdos, Moltres, Mewtwo, Suicune, Latios, Latias have boot rows only | Walk to the actor and press A on each |
| Nine story-battle scenes | Rivals, Petrel, Archer, Kimono Girls, boot-verified only | Fight each |
| Lake of Rage pre-Gyarados | No fixture. The trek reached Route 43 (524,149) mid-battle | Resume from `hg-6gyn-surf.sav` and the banked `.mcpr` anchors (local `leveld-out/`) |
| Unmodelled general-block tables | `0x23B8-0x2740` and `0x4434-0x4510` (F6) | Compute chunk sizes from decomp structs and place each cluster |
| Named flags | Unnamed flags that move between HG milestones (`tools/unk_flags.py`) | Attribution from the writer script plus runtime tests |
| Actor checker | Does not model `CheckBattleWon`; over-reports one Mt. Moon write | Add the opcode to the walker |
| Facing field | The u16 at +16 of the live `Location` lagged once | More observations |
| Unexplained tooling | A long combined input hold freezes after the first hold | Root cause unknown; use short holds |
| Most of the research plan | 58 of 98 questions untouched | `docs/research-questions.md` |

### Deliberate refusals

These are rules, not gaps (`docs/archive/story-state-field-knowledge.md` §6):

- Backward resets ("un-beat Clair to restage Ecruteak") fabricate states the scripts never produce.
- Arbitrary teleports (exit 8 `location-unverified`).
- A walkability verdict from undecoded data.
- Mid-cutscene `.sav` states do not exist; use savestates.
- Granting a key item whose give-flag or counter would disagree.

### Documentation state

- Counts in older docs have drifted. `README.md` says 54 tests (the suite is 69); `docs/archive/coverage-gaps.md` and `docs/archive/story-state-field-knowledge.md` say 19 safe spawns (the file holds 20); `docs/archive/state-log.md` F9 says 17 presets (there are 18). The generated files and tools hold the current numbers.
- Raw agent transcripts `chatlog00.*`, `chatlog01.md` are git-ignored and kept for provenance only. Nothing in them is needed to use the project.

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
| The start menu was stuck open on the Kyogre run | A stale screen capture; the Down press moved the player out | `docs/archive/runtime-results.md` |
| `D43R0103` is Goldenrod Underground, then Victory Road 2F | Map 179 `MAP_VICTORY_ROAD_3F`; the Goldenrod rival is `D37R0104`, map 201 | F16 |
| `spawn_registry_check.py` is a valid check of the warp table | It compares warp tiles with free-roam positions and explains 0 of 20; runtime probing is the right test | F5 |
| Mt. Silver needs `FLAG_UNLOCKED_MT_SILVER` | Five-write chain | F18 |
| `needs` edges in `story-atoms-hg.json` are prerequisites | File-level reads, not guards | F14 |
| `docs/archive/SAV4HGSS.md`: TM/HM pouch is 100 slots | 101 | `docs/save-format.md` |
| `docs/archive/runtime-validation.md`: no headless mode | `tools/leveld_run.py` exists | `docs/verification.md` |
| Rotom is a scene | Not producible from flags alone | F17 |
