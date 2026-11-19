# Data files and research tools

What each data file under `registry/`, `research/` and `fixtures/` is, which tool produces it, and who consumes it; plus what each script in `tools/` does. Everything in `registry/` and `research/` is generated. Regenerate it with the tool named; do not hand-edit.

Pinned inputs: `pret/pokeheartgold` at `4e9563397ab4e5c407271472935f9e270ac6a804` (local checkout `3DS/pokeheartgold`), PKHeX.Core 26.10.18, .NET 10. The decomp is an unstable work in progress, and every registry records the commit it was built from.

## `registry/`: shipped with the CLI

The CLI reads these at run time. The publish step copies them beside the exe.

| File | Schema | Contents | Built by |
|---|---|---|---|
| `hgss-us.symbols.json` | `hgss-save.symbols/v1` | 5,485 symbols (flags, vars, maps, items, species, moves) with id, kind, source file and line, writability; 438 skipped macros listed with reasons | `hgss-save symbols build-registry --headers DIR --commit SHA` |
| `source-manifest.json` | | commit, counts per kind | same |
| `hgss-us.script-refs.json` | `hgss-save.script-refs/v1` | 9,960 reads/writes/checks of 965 symbols in `scr_seq/*.s`, each with file, line, enclosing `ScrDef` label and access kind (`read`, `write`, `check`) | `hgss-save script index --scripts DIR --commit SHA` |
| `hgss-us.badge-rules.json` | `hgss-save.badge-rules/v1` | per badge (16): effects of the gym script, classified `confirmed`, `source-only` or `skipped`; the `VAR_MIDGAME_BADGES` counter; 4 HM grants; 8 field-move badge gates | `tools/make_badge_rules.py` (from `research/badge-effects.json` and the census) |
| `hgss-us.warp-spawns.json` | `hgss-save.warp-spawns/v1` | 540 maps with their zone-event warp and door arrival tiles (1,317 warp events) | `tools/zone_spawn_index.py --decomp DIR` |
| `hgss-us.scene-triggers.json` | `hgss-save.scene-triggers/v1` | 315 "VAR == value" cutscene triggers with script and reachable writes | `tools/scene_triggers.py` |
| `hgss-us.scenes.json` | `hgss-save.scenes/v1` | 80 scenes: base preset, badges, ops, warp, expected state, citations | `tools/make_scenes.py` |
| `presets/index.json`, `presets/*.json` | `hgss-save.presets/v1`, patch recipes | 18 full-state presets (`docs/corpus.md`) | `tools/make_presets.py` |

Symbol kinds: `persistent_flag` 2,847, `save_work` 320, `temporary_flag` 127, `temporary_var` 48, `special_runtime_var` 14, `range_constant` 49, `unknown` 4, plus `map` 541, `item` 546, `species` 509, `move` 480. Writable kinds are `persistent_flag` and `save_work`. Names derived from a range (trainer flags) carry `confidence: "derived-range"`.

Symbols are annotations on numeric ids; the id is the identity. Names can change with the decomp.

## `research/`: evidence and analysis outputs

Not read by the CLI at run time (scene audits cite some by path). They record how conclusions were reached.

| File | Contents | Built by | Used by |
|---|---|---|---|
| `corpus-census.json` | per save: SHA-256, game, trainer, TID/SID, badges, map, play time, dex, bag, party, every set flag and nonzero variable. 29 saves, 12 skipped | `tools/corpus_census.py` | almost every research tool |
| `lineage-HG-Master.md`, `lineage-SS-Master.md` | flags on/off and variable changes between consecutive saves of one trainer | `tools/lineage_diff.py` | F1, F7, F8 |
| `badge-effects.json` | for each of 16 `GiveBadge` statements, the straight-line effects after it, with file and line | `tools/script_effects.py --anchor GiveBadge` | `make_badge_rules.py` |
| `script-graph.json` | which scripts write (879 symbols) and read (439) each flag/variable | `tools/script_graph.py` | `story_atoms.py`, `delta_by_script.py` |
| `script-guards.json` | the flag/variable conditions on the path to every state write: 497 scripts, 1,869 distinct writes, 6,650 guarded paths | `tools/script_guards.py` | F14 |
| `story-atoms-hg.json` | 200 step-sized units of change across the HG lineage, linked by script data flow. The edges are file-level reads, not guards (F14) | `tools/story_atoms.py` | exploration only |
| `save-selection.json` | per corpus save: the game's boot status, the partition it loads, both partitions' counters, and PKHeX's per-block pick. 29 unique saves, all `IS_GOOD`, zero disagreements | `tools/save_selection_scan.py` | FMT-02 |
| `fmt-04-roundtrip.json` | four stages of a PKHeX no-op round trip with counters, box masks, chunk CRCs and byte deltas between them | `tools/noop_roundtrip_report.py` | FMT-04 |
| `fmt-matrix.json` | per crafted fault: predicted status, message, loaded partition, written partition, and the play time carried into it | `tools/fmt_matrix_report.py` | FMT-01, FMT-08 |
| `aux-matrix.json` | per crafted auxiliary-chunk fault: predicted and observed recovery, which partition each copy came from, and the `SAVE_MISC` `prefer`/`extraChunksExist` state after the save | `tools/aux_matrix_report.py` | FMT-06, FMT-07 |
| `scene-actor-gates.json` | for each of 80 scenes: writes to the actor's hide flag in map init, with conditions; any init instruction that moves the player; and any init write to a variable one of the map's coord triggers tests, with the stored value and whether some coord trigger on that map wants it (10 scenes, 8 writes that differ, 9 that match) | `tools/scene_actor_check.py` | F17, scene audits |

`research/script-graph.json` and `script-guards.json` are large (0.4 MB and 1.4 MB). Open them with a JSON-aware tool or use the query flags (`script_graph.py --query T22GYM0101`).

## `fixtures/`

| File | Contents |
|---|---|
| `recipes/*.json` | 30 fixture recipes (`hgss-save.fixture/v1`): a played base save by SHA-256, a patch, expected state, runtime checkpoints. Bases are not committed; pass `--bases DIR` |
| `safe-spawns.json` | 20 map/x/y tiles seen in real saves; the only tiles `location set` accepts |
| `event-prereqs.json` | 13 story beats with script reads and writes (file and line) and observed values |

## `tools/`: research scripts

Python 3, standard library plus the melonDS-MCP bindings for runtime tools. All read-only on saves and ROMs; outputs go to explicit paths.

### Save analysis

| Script | Purpose |
|---|---|
| `corpus_census.py` | Catalogue every HG/SS save in a tree through the CLI; collapse duplicates by SHA-256; record non-HGSS files as skipped |
| `lineage_diff.py` | Flag and variable deltas between consecutive saves of one trainer |
| `residual_diff.py` | Byte-diff the general block with modelled regions masked out, to find state nobody understands (F6) |
| `field_locations.py` | Print the five `Location`s, music, weather and spawn of every census save (F4) |
| `state_delta.py` | Turn two census saves into a patch recipe with flags on and off; `--verify` replays it (F7) |
| `unk_flags.py` | Which unnamed flags change between HG milestones |
| `save_edit.py` | Raw byte editor on a copy with CRC repair, for experiments the CLI does not model (dynamic warp, flag `0x966`, single flags, raw pokes) |
| `save_selection_scan.py` | Port the game's partition-selection tree (`save.c:429-526`) over a save tree: status, loaded partition, and where PKHeX's per-block pick differs |
| `fault_matrix.py` | Craft one boot-time save fault per image, predict the status, boot it, save once in-game and record which partition loaded and which was rewritten |
| `fmt_matrix_report.py` | Summarise a `fault_matrix.py` run into `research/fmt-matrix.json` |
| `noop_roundtrip_report.py` | Stage a PKHeX no-op export, the save after one in-game save and the save after two, and record counters, masks, play times and byte deltas |
| `aux_fault_matrix.py` | Craft one auxiliary-chunk fault per image (dead copy, both dead, key mismatch, gate off, `saveno` disagreement, both `prefer` values), predict the recovery from `sub_020284A4`, and boot each |
| `aux_matrix_report.py` | Summarise an `aux_fault_matrix.py` run into `research/aux-matrix.json` |
| `block_selection_scan.py` | Superseded by `save_selection_scan.py`; its per-block question is not the game's |

### Decomp and ROM analysis

| Script | Purpose |
|---|---|
| `script_effects.py` | Follow a script's straight-line path from an anchor and list the flag/variable/badge/item effects |
| `script_graph.py` | Writer/reader graph for every flag and variable |
| `script_guards.py` | Path conditions for every state write (F14) |
| `story_atoms.py`, `delta_by_script.py` | Group a lineage step by writer script |
| `badge_effect_check.py`, `make_badge_rules.py` | Check badge effects against the census and build `badge-rules.json` |
| `scene_triggers.py` | Extract coordinate and frame triggers (F11) |
| `scene_actor_check.py` | Walk map-init scripts for hide-flag writes and player moves (F17) |
| `zone_spawn_index.py` | Build the warp table from zone-event JSON |
| `spawn_registry_check.py` | Compare the warp table with safe spawns. Explains 0 of 20 because safe spawns are free-roam tiles; it is the wrong test and is kept as a record |
| `nds_files.py`, `map_data.py`, `map_reach.py` | ROM filesystem and NARC reader, overworld tiles and collision, reachability flood fill (F15, unresolved) |

### Generators

| Script | Purpose |
|---|---|
| `make_presets.py` | Build presets from census milestones |
| `make_scenes.py` | Build the 80-scene registry. Its tuples are the contract; edit with a citation and a boot check |

### Runtime (melonDS)

| Script | Purpose |
|---|---|
| `save_once.py` | One no-op in-game save, exported as a battery file: polls the field panel instead of counting frames, navigates the save menu, exports |
| `boot_probe.py`, `boot_timeline.py`, `save_probe.py`, `save_timing.py` | Boot and save-menu instrumentation: step-by-step screenshots, message text, and the frame at which the write finishes |
| `contact_sheet.py` | Tile several screenshots into one labelled image for reading message text |
| `leveld_run.py` | Headless boot of a staged save: copies ROM and save, advances frames, captures screenshots and savestates, scans RAM for the live `Location` (`--assert-spawn`, `--spawn-primary`), injects inputs and touches, exports the in-game save (`--extract-save`) |
| `dynwarp_probe.py` | Runtime test of flag `0x966` plus dynamic warp (F5) |
| `preset_boot_check.py` | Apply every preset to a clean base and boot it (F9) |
| `scene_boot_check.py` | Compose and boot every scene |
| `scene_boot_matrix.py` | Render run summaries as `docs/scene-boot-matrix.md` |

Runtime tools need the melonDS-MCP native core (`MELONDS_MCP_LIB`, `PYTHONPATH` or `--mcp-python`), the retail ROMs and a base save. An SS save loads only in the SS ROM, so SS scenes need `--base-ss` and `--rom-ss`.

## Source layout

`src/HgssSave.Core` holds format-independent logic (patch planning, byte audit, registries). `src/HgssSave.PKHeX` is the only layer that touches PKHeX.Core (`HgssLayout.cs` copies PKHeX's private constants with line citations; tests fail if PKHeX changes). `src/HgssSave.Cli` is the command surface (`HelpText.cs`, `docs/cli-reference.md`). `tests/HgssSave.Tests` has 73 tests.
