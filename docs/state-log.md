# State log: what the save corpus shows

Running log of findings from diffing real saves against the decomp and PKHeX. Newest entries at the bottom. Each finding lists its evidence. Tools: `tools/corpus_census.py`, `tools/lineage_diff.py`, `tools/residual_diff.py`. Data: `research/`. Originals in `3DS/hgss-saves` are never modified; any edited save goes to a new path.

## 2026-11-17 corpus census

`tools/corpus_census.py` over `3DS/hgss-saves` (output `research/corpus-census.json`):

- 42 candidate files of size `0x80000`; 29 are unique HG/SS saves; 12 are Pearl, Platinum or White and are rejected with exit 3. (The earlier "71 saves" figure counted every file in the folder, including those, `.dsv` copies, and duplicates.)
- The 29 are: HG Master lineage (18 saves, 1 gym through Kanto), SS M@ster lineage (10, includes a 52 h post-game save), plus single saves for Ash (HG start), NotKris (SS start), Desmond (HG, Dragon's Den), and five Lyra event-distribution saves.
- Coverage is thin where it matters: one save per milestone on each lineage, no two saves at the same milestone from the same trainer. Anything inferred from a single pair is a hypothesis.
- Several files are mislabelled: `Pokémon HeartGold.sav` is a SoulSilver save (game code, SS lineage). Always trust the identity the CLI reports, not the filename.
- All Master-lineage saves carry bag quantities near 990 and 14 TMs; they are not clean progress samples for items. Flags and variables are unaffected by that.

## F1. Progress is not monotonic: flags turn off as well as on

`tools/lineage_diff.py` on consecutive HG Master saves (`research/lineage-HG-Master.md`) shows 2 to 9 flags **clearing** at every step, plus 7 to 65 setting. Almost all clearing flags are `FLAG_HIDE_*`, where *set means the object is hidden*, so clearing makes an NPC or item appear (for example `FLAG_HIDE_JASMINE_IN_GYM` clears when Jasmine becomes available, `FLAG_HIDE_WILLS_ROOM_RETREAT` clears after the Elite Four). Also clearing: `FLAG_ROCKET_TAKEOVER_ACTIVE`, `FLAG_AZALEA_ROCKET_HARASSING_CIVILIAN`, `FLAG_DAILY_*` values, and several `FLAG_UNK_*`.

Consequence: a "set progress to X" feature that only adds flags would leave NPCs hidden that the game expects visible. Progress presets must specify both sets and clears, and the clear half cannot be inferred from "flags that were true" alone.

SS lineage agrees (`research/lineage-SS-Master.md`), including 25 flags clearing across the Elite Four step (`FLAG_DEFEATED_WILL/KOGA/BRUNO/KAREN` clear when the league resets after the Hall of Fame).

## F2. Flag census by family

Across all 29 saves, 9,957 set-flag instances fall into:

| Family | Count | How identified |
|---|---|---|
| `FLAG_HIDE_*` | 5,270 | registry name |
| trainer-defeated | 2,154 | id in `0x550..0xAA0` |
| `FLAG_UNK_*` | 1,430 (184 distinct ids) | registry name; purpose unknown |
| `FLAG_GOT_*` | 643 | registry name |
| other named | 347 | |
| `FLAG_CAUGHT_*` | 47 | |
| hidden items | 46 | id in `800..1030` |
| `FLAG_DEFEATED_*` | 12 | |
| daily | 7 | id in `0xAA0..0xB60` |
| no symbol | 1 | id 16 |

**Correction (same day, recount):** the "trainer" row above bucketed every id in `0x550..0xAA0`, but 626 flags in that span already have names. Counting only ids with no registry symbol: 1,660 of 9,957 instances (17%) are unnamed, and 1,659 of those are trainer-defeated flags; the single other is id 16. Hidden items are already named in `flags.h` (231 `FLAG_HIDDENITEM_*` entries in `800..1030`).

The registry did not name trainer flags because `flags.h:1394-1395` defines them as a range, `TRAINER_FLAG_BASE 0x550` and `FLAG_TRAINER(tr) = TRAINER_FLAG_BASE + tr`, with the trainer ids in `include/constants/trainers.h` (738 ids, 1..737). **Fixed:** `RegistryBuilder` now derives `FLAG_TRAINER_<NAME>` = `0x550 + id` for every trainer id whose flag has no `flags.h` name (733 entries, `confidence: "derived-range"`, never overriding a source-defined name). The change is purely additive against the previous registry (0 removed, 0 changed). `symbols lookup FLAG_TRAINER_RIVAL_SILVER` returns id 1361. All 1,659 unnamed trainer instances in the corpus now resolve.

Six save variables have no symbol: `0x4000-0x4002`, `0x4013`, `0x4020`, `0x4021`. They sit in the temp/object-gfx range the CLI already classifies as non-writable.

## F3. HG and SS agree on the story core

HG Master (4 badges, Ecruteak) vs SS M@ster (4 badges, Ecruteak): 268 vs 284 flags set, 252 in common. The differences are optional state: item-ball pickups (`FLAG_HIDE_ITEMBALL_*`, 9 flags), a few trainers, side events (`FLAG_AZALEA_HARASSED_CIVILIAN`, `FLAG_TRADE_VIOLET_CITY_BELLSPROUT_ONIX`), `FLAG_GOT_OLD_ROD`, `FLAG_GOT_APRICORN_BOX`. So milestone state decomposes into a shared core plus an optional layer; the core can come from the intersection of lineages, the optional layer needs its own policy. One comparison only; not yet tested at other milestones.

## F4. The save does contain actor and position state (correction to `coverage-gaps.md`)

`docs/coverage-gaps.md` says story validity needs "actors, warps, and script context the save cannot encode". Most of that is encoded. Evidence:

`src/save_local_field_data.c:9-30` (pokeheartgold) defines `LocalFieldData`: five `Location` structs (`currentPosition`, `entrancePosition`, `previousPosition`, `dynamicWarp`, `specialSpawn`), `musicId`, `weather`, `lastSpawn` (blackout spawn), `cameraType`, `PlayerSaveData`, poison/safari counters. `Location` is five `int`s (`mapId, warpId, x, y, direction`, 20 bytes). `SAVE_MAP_OBJECTS` (`save_local_field_data.c:33-35`) is 64 `SavedMapObject`s, the actors of the current map.

Offsets, verified against the corpus (`tools/field_locations.py`; all 29 saves decode to sane maps): general-relative `0x1234 + 20*n` for n = 0..4 (current, entrance, previous, dynamic warp, special spawn), then `musicId 0x1298`, `weather 0x129A`, `lastSpawn 0x129C`. PKHeX already uses `0x1234/0x123C/0x1240` for map/X/Y (`SAV4HGSS.cs:186-188`), which is consistent. The special spawn matches each town's fly point (Ecruteak `(397,184)` in two HG saves; Mahogany `(534,184)` in an HG and an SS save), and the blackout spawn id changes per town.

The CLI currently writes only the current location. A position edit that leaves entrance, previous, dynamic warp, spawn and the 64 map objects untouched produces an inconsistent save.

## F5. Continue has a second path that spawns from warp data

`FieldTask_ContinueGame_Normal` (`src/field_warp_tasks.c:415-441`):

- If `FLAG_UNK_966` (id `0x966`) is **set**: take the saved `dynamicWarp`, clear the flag, set the player there, initialise actors from zone-event data (`sub_0205316C` -> `Field_InitMapObjectsFromZoneEventData`).
- Else: restore the saved current position and the saved 64 map objects (`sub_0205323C`).

`sub_02052F94` (`field_warp_tasks.c`, the `location != NULL` branch) resolves `warpId != -1` by overwriting x/y with the warp event's tile from the map's zone events. So a dynamic warp with a valid `warpId` lands on a tile that the game itself defines, with no need to know tile walkability. The game uses exactly this after the Hall of Fame: `game_clear.c` sets `Location_SetToPlayerRoom(dynamicWarp)`, `Location_SetToOutsidePlayerHome(spawnWarp)`, `SetFlag966`.

Corpus check: of 29 saves, exactly one has flag `0x966` set (HG Master 22:04:51, right after the Hall of Fame), and its dynamic warp is the New Bark player room and its special spawn is New Bark. The final SS save (52:45:25) also carries the New Bark player-room dynamic warp with the flag clear, as expected after one Continue. This matches the decomp.

**Status: verified at runtime, 2026-11-17.** Method: `tools/save_edit.py` (hand byte edit plus CRC fix) or `hgss-save location warp` on the HG Master 4-badge Ecruteak save, then `tools/leveld_run.py` boots `heartgold.nds` headless through Continue and scans ARM9 RAM for the live `Location` {map, warp, x, y, direction}. The control (unedited save, same inputs) boots at an Ecruteak Pokémon Center.

- Hand edit, dynamic warp `{73, warp 2, x 0, y 0}` plus flag `0x966`: live Location `{73, warp 2, (479,257), dir 1}` at `0x227d498`, the Violet Gym door tile the warp table lists. The stored `(0,0)` was wrong on purpose, so the game resolved the tile itself.
- `tools/dynwarp_probe.py --sample 6 --seed 11` (random (map, warp) from the table): 6 of 6 landed on the table's tile exactly. Targets: Bell Tower 9F warps 4 and 5, Seafoam Islands B1F warp 2, Route 40 Battle Frontier gatehouse warp 0, Saffron Gym warp 14, Mt. Silver Cave 2F warp 1. One result row also listed unrelated RAM matches at other addresses (coincidental byte patterns); the player struct at `0x227d498` matched in all six.
- A save produced by the shipped `hgss-save location warp` (`location.warp` operation) lands on the same tile. Byte audit: accepted, 0 unclassified bytes.

Scope of the claim: HG, one base save (HG Master 4 badges), 7 targets, Continue only. Not tested: SS, saves from other story points, interior-to-outdoor chains, whether the player can then walk and save normally beyond the first overworld frame (the screenshot shows the overworld and menu), or what the game does with the unchanged `currentPosition` on the next save (it is replaced by the player's real position at that save).

Consequences: `location warp` gives a teleport to any warp event of any map with fresh actors, without decoding walkability. Limits: the arrival tile is a warp event's tile (a door, stairs or map-edge exit), not an arbitrary tile, and the direction field's meaning is unmapped (only `1` is observed in saves).

The warp table (`registry/hgss-us.warp-spawns.json`) is the derivation of the zone-event warp lists that this mechanism resolves at load. `spawn_registry_check.py` explained 0 of 20 safe spawns because it compared warp tiles with *current positions*, which was the wrong test; the runtime result above is the right one. In the corpus, the 7 distinct saved `dynamicWarp` (map, warpId) pairs all have a matching `warp_index` in the table, and the saved x/y matches the table tile in only 1 of 6 non-zero cases (map 191 warp 2: saved `(19,3)`, table `(19,2)`), which fits the game recomputing x/y at load. The runtime probe confirms the table's tile is what the game lands on, for the 7 targets tested.
## F6. Unmodelled save area

`tools/residual_diff.py` masks party, bag, vars and flags and diffs the active general block between consecutive HG Master saves. Each step changes 1,000 to 1,300 bytes outside those. Using PKHeX offsets (`SAV4HGSS.cs:37-79`) and the decomp chunk order (`src/save_arrays.c`, `gSaveChunkHeaders`), the recurring clusters are:

| Offset | Content |
|---|---|
| `0x1C-0x30`, `0x78-0x89` | system/player header, play time |
| `0x1234-0x12A8` | `LocalFieldData` (F4) |
| `0x12B8..0x15F0` | Pokédex (`SAV4HGSS.cs:37`); changes whenever a species is seen |
| `0x2368-0x2376` | PKHeX `X2/Y2/Z` (`SAV4HGSS.cs:198-200`); hot every step |
| `0x23B8-0x2740` | a regular repeating table (stride about 0x10-0x28); not yet identified, a candidate for `SAVE_MAP_OBJECTS` or the chunk before it |
| `0x4434-0x4510` | 4-byte entries, 0x2C stride; unidentified |
| `0x4B3C-0x4CF8` | `OFS_Record` (`SAV4HGSS.cs:74`), game statistics |
| `0x5BB8-0x5BBF` | BP and Battle Tower seed (`SAV4HGSS.cs:339-340`) |
| `0x68A4-0x68B4` | swarm seed and nearby (`SAV4HGSS.cs:336`) |
| `0xBAD4-0xBAE7` | map unlock state (`SAV4HGSS.cs:94`) |
| `0xC0DE` | Pokégear rolodex area (`SAV4HGSS.cs:208`) |

Not yet attributed: the `0x23B8-0x2740` and `0x4434-0x4510` tables. Next step is to compute chunk sizes from the decomp structs and place every cluster precisely.

## F7. Story-state deltas replay exactly (flags and save vars)

`tools/state_delta.py` turns two census saves into a recipe: `flag.set` true for flags on, false for flags off, and `variable.set` (with `expect`) for every changed writable var. `--verify EXE` applies it to the earlier raw save and diffs the result against the later one.

Result on all 10 consecutive steps of the HG "Master" lineage (1gyn through posE4, including Celebi and E4): 0 differing flags every time. The only remaining differences are the vars the tool excludes on purpose: temp var 0x4000 (below 0x4030, not writable; takes values 0..7 across the lineage), `VAR_LOTO_NUMBER_LO` and `VAR_FRIENDSHIP_STEPS` (per-session counters). A first version also excluded every `VAR_UNK_*` var; that left 4 real story vars unset (e.g. `VAR_UNK_4080` 0 to 4 between 1gyn and 2gyn), so the exclusion list is now exactly the three names above.

Scope: this replays states between saves we already have, so the recipes are only as good as those milestones. The 11 HG milestones are one playthrough; choices that differ between playthroughs are not separated yet (open item 3).

## F8. Badges are the hub of a dependency web; the game's own scripts say what each badge changes

Writing only the badge byte leaves the save incoherent. Three layers are tied to it, each checked against the 29 census saves.

1. **Gym-script consequences.** After `GiveBadge` each gym script sets flags and vars (`research/badge-effects.json`, from `tools/script_effects.py`; every row cites file and line). `tools/make_badge_rules.py` classifies each effect against the census. Examples that hold in every save owning the badge: Zephyr sets `FLAG_GOT_TM51_FROM_FALKNER`, `FLAG_UNK_192`, `FLAG_UNK_0F4`, `FLAG_HIDE_WIFI_CLUB_CLOSED_LADY` and the two Bird Keeper trainer flags (27/27); Fog sets `FLAG_GOT_TM30_FROM_MORTY` (20/20); Hive sets four Bug Catcher/Twins trainer flags (24/24). Many `SetVar` effects are overwritten by later story (for example `VAR_SCENE_VIOLET_CITY_OW` set to 1 by Zephyr is never 1 in any later save), so a badge's block is not the final value of those symbols; the rules file lists them as `skipped` instead of copying them.
2. **Derived counter.** `VAR_MIDGAME_BADGES` (0x4134) equals the number of Fog, Storm, Mineral and Glacier badges while that is below 4 (29/29 saves). At 4 the map header scripts (`scr_seq_0684_T26_hdr.s:13`, `scr_seq_0703_T28_hdr.s:12`, `scr_seq_0875_T24.s:46`) compare it to 4, start the Rocket takeover and set it to 5 (`scr_seq_0875_T24.s:86`); every save with all four badges holds 5. The count is order-independent, so any set of those four badges drives the same trigger.
3. **Scene machines.** `VAR_SCENE_ROCKET_TAKEOVER` goes 0, 1 (three midgame badges), 2 or 4 (takeover in progress), 5 (done) in the census. Writers (script tag resolved through `include/constants/maps.h`): Cianwood and its gym, Olivine and its gym, Mahogany and its gym, Goldenrod Radio Tower 1F, the Observation Deck and Goldenrod Tunnel B1F. Readers: Goldenrod (city, Pokemon Center, department store 1F-6F, Radio Tower 1F-5F and Observation Deck, Goldenrod Tunnel 1F-B2F and warehouse), Mahogany and its souvenir shop, Blackthorn, and Azalea's Kurt's house. Other gates found the same way: `FLAG_GOT_SECRETPOTION` is written in Cianwood Pharmacy and read in the Olivine Lighthouse light room, and `VAR_SCENE_LIGHTHOUSE_JASMINE` is written in Olivine and the light room and read in Cianwood Pharmacy, Olivine and the light room, so Jasmine's gym state depends on both cities (`VAR_UNK_410E` is read only by the Olivine gym script). `FLAG_BEAT_AZALEA_ROCKETS` is written in Slowpoke Well B1F and read by Azalea, the Charcoal Kiln, Kurt's house and Ilex Forest. `FLAG_GAME_CLEAR` is read by the Bugsy, Whitney, Jasmine, Morty and Pryce gym scripts (post-game behaviour). Method and raw edges: `tools/script_graph.py`, `research/script-graph.json`, `tools/story_atoms.py`, `research/story-atoms-hg.json`.

Gym scripts themselves have no story guards (Falkner reads only his own TM flag); order is enforced by the world: HM badge checks, NPC placement flags (`FLAG_HIDE_*`) and the scene vars above.

Implemented: `trainer badges set` applies layers 1 and 2 for newly gained badges (`hgss-us.badge-rules.json`, `--raw` to skip). Layer 3 and the world prerequisites for out-of-order badge sets are covered only by `preset apply`, whose states are whole played milestones. `FLAG_UNK_189` is a transient script lock (set and cleared around cutscenes in 60+ scripts); no preset or rule sets it.

## F9. Presets and replays boot

2026-11-17. `tools/state_delta.py` replays (F7) and the 17 presets from `tools/make_presets.py` were booted headless (`tools/leveld_run.py`, `tools/preset_boot_check.py`). A replay of the 1-gym HG save plus all flag/var changes up to 8 gyms loads and spawns in Violet at the saved tile, but it owns one badge with the 8-gym story flags, which is exactly the badge/flag disagreement F8 describes. Every preset, applied to the clean HG or SS base save (`Gen 4 - ... (ENG).sav`), exits 0, has 0 `story check` violations, and Continue puts the live Location on the preset's warp tile (17 of 17; HG presets on `heartgold.nds`, SS presets on `soulsilver.nds`). A preset differs from its source save only in the fields it deliberately skips (flag 0x966, `VAR_LOTO_NUMBER_LO`, `VAR_FRIENDSHIP_STEPS`, temp var 0x4000), checked with `diff`.

What this does not show: that the game behaves correctly in play after each preset (cutscene triggers, NPC placement). The boot check proves the save loads and the position is right.

## F10. The counter rule triggers the Rocket takeover at runtime

2026-11-17. Prediction from F8: entering a map whose header script tests `VAR_MIDGAME_BADGES == 4` starts the takeover. Mahogany's header (`scr_seq_0703_T28_hdr.s:12`) jumps to `scr_seq_0930_T28.s:18`, which sets `VAR_SCENE_ROCKET_TAKEOVER` to 2, sets `FLAG_UNK_0C5` and `FLAG_ROCKET_TAKEOVER_ACTIVE`, starts a phone call from Prof. Elm and sets the counter to 5.

Test: `preset apply hg-06-mineral` on the clean HG base (counter 3), then (A) `trainer badges set --johto 127` (adds Glacier; the rules raise the counter to 4) and `location warp 87 3` (Mahogany), or (B) only the warp. Both booted headless through Continue (`tools/leveld_run.py`, frames 4200-6600). A shows Elm's call "something weird is happening with the radio broadcasts" over the Mahogany overworld; B shows the plain overworld with no call.

So the derived counter is load-bearing: writing badge 6 without the counter (the `--raw` path) would have produced a save in which the takeover never starts.

## F11. The cutscene trigger table

`tools/scene_triggers.py` extracts every "VAR == value" trigger from the decomp: 187 zone-event coordinate triggers (`files/fielddata/eventdata/zone_event/*.json`, fields `var`, `val`, `x`, `z`, `w`, `h`, `scriptId`) and 128 map header on-frame triggers (`InitScriptGoToIfEqual` in `scr_seq_*_hdr.s`), 315 in all over 169 variables (27 of them script temp vars). Each entry carries the script and everything it can write. It is shipped as `registry/hgss-us.scene-triggers.json` and queried with `hgss-save story triggers --var/--map`.

This makes the scene machines readable. `VAR_SCENE_ROCKET_TAKEOVER`: the Mahogany header sets it to 2 when `VAR_MIDGAME_BADGES == 4` (F10); stepping on Goldenrod Tunnel B1F with the var at 2 runs `scr_seq_D37R0102_004`, which sets 3 (`scr_seq_0094_D37R0102.s:695`); the Radio Tower 1F trigger at 3 runs `scr_seq_D23R0101_009`, which sets 4 or 2 (`scr_seq_0029_D23R0101.s:457,463`); a Goldenrod trigger also waits on 3.

To stage a given cutscene: set its variable to `value`, put the player in its map (frame triggers fire on load; coord triggers need the player to step on `area`). Not yet automated: the tile coordinates are tile data, so reaching a coord trigger from a warp needs walking.

## F12. HM grants and badge gates

Two more prerequisite layers, both in `registry/hgss-us.badge-rules.json` and checked by `story check`.

HM grants: four scripts set an HM flag and give the item in one block: `FLAG_GOT_HM01` (`scr_seq_0092_D36R0101.s:1293`, Ilex Forest), `HM02` (`scr_seq_0875_T24.s:39`, Cianwood), `HM03` (`scr_seq_0928_T27R0501.s:378`, Ecruteak Dance Theater), `HM08` (`scr_seq_0740_T01R0301.s:208`). Across the saves the flag and the item agree in 29 of 29, so unlike TMs (consumed) an HM flag without its item is an inconsistency. HM04-HM07 have no grant flag in `flags.h`; their gives are not tracked.

Field gates: `src/field_move.c` refuses a move without its badge: Cut/Hive (189), Fly/Storm (221), Surf/Fog (254), Strength/Plain (295), Rock Smash/Zephyr (330), Waterfall/Rising (365), Rock Climb/Earth (397), Whirlpool/Glacier (608). Whirlpool is a move, not an HM item (HM05 is Defog). A missing badge is not an inconsistency (a player can own the HM and lack the badge), so `story check` lists these as `locked_field_moves` for information.

Grant flags follow story order in the HG lineage: HM01 appears by 3 badges, HM02/HM03 by 5, which is the pattern a closure resolver has to reproduce. Observed order, recorded as `observed_badges` per grant and reported by `story check` as advisory `observed-order`: every save with HM01 owns badges 0-1, every save with HM02 or HM03 owns badges 0-4, the one HM08 save owns all Johto badges. No script enforces this (the grant scripts do not test badges), so it is lineage evidence only. Not yet implemented: closure (asking for badge N and receiving the HM, scene vars and NPC flags that must precede it).

## F13. `story build`

`story build` composes the two things that are verified: a played-save preset as the base and badge consequences for the rest. Masks 0, 5, 73 and 255 on a blank HG gave 0 violations; 255 chose `hg-08-rising` and leaves only Rock Climb locked (Earth). Mask 73 (Zephyr, Fog, Glacier) on `hg-01-zephyr` booted to Violet Pokemon Center (497,271), matching warp 7. What it does not do: a badge beyond the base gets no scene variables, NPC flags or HM grants, so mask 73 holds Fog and Glacier without Surf. Closing that needs more than one lineage of evidence.

## F14. The scripts that give badges and HMs have no story guards

`tools/script_guards.py` walks every script entry label through GoTo/GoToIf*/CallIf* edges and records, for each SetFlag/ClearFlag/SetVar/AddVar/GiveBadge, the flag and variable conditions on the path that reaches it (`research/script-guards.json`: 497 scripts, 1,869 distinct writes, 6,650 guarded paths).

Result for the writes that matter. All 16 `GiveBadge` paths carry no story guard once battle results (`VAR_SPECIAL_RESULT`) and temp vars are dropped; the one exception, Plain with `FLAG_UNK_0B7`, is set earlier in the same script (`scr_seq_0886_T25GYM0101.s:175`), so it is not a prerequisite. `FLAG_GOT_HM02` (`scr_seq_0875_T24.s:39`) and `FLAG_GOT_HM03` (`scr_seq_0928_T27R0501.s:378`) are unguarded; HM01 only tests that it was not already given. HM08 tests `VAR_UNK_4096 == 2` (`scr_seq_0740_T01R0301.s:95`), but the one save that owns HM08 has that var at 0, because `scr_seq_0735_T01.s:27` resets it: a scene var is cleared after use, so it cannot be audited from the end state. TM givers test only `VAR_MIDGAME_BADGES` and `VAR_SCENE_ROCKET_TAKEOVER != 4` (TM11).

Consequence for the resolver: ordering is not in the scripts that write progress. It is in access (which map is reachable, which NPC blocks the path, which frame or coordinate trigger fires), so prerequisites for an arbitrary badge set have to come from the map graph (warps, `FLAG_HIDE_*` placement, the F11 triggers), not from the gym or HM scripts. This replaces the earlier reading of the `needs` edges in `research/story-atoms-hg.json`, which are file-level reads and not guards (the HM01 edge there points at a read on a different branch, `scr_seq_0092_D36R0101.s:65`).

## F15. Map reachability: pipeline built, collision semantics not yet calibrated

Goal: derive which field-move gates a map needs, since F14 showed the scripts do not encode order. `tools/nds_files.py` reads the NDS filesystem and NARCs; `tools/map_data.py` loads the overworld matrix (`files/fielddata/mapmatrix/map_matrix/map_matrix_0000_EVERYWHERE.bin`, format from `src/map_matrix.c:34-58`), the land-data NARC (ROM `a/0/6/5`, 676 members, tile attributes u16 at member offset 0x14 per `include/terrain_attributes.h:11-13`) and the behaviour enum from `include/constants/metatile_behavior.h`. `tools/map_reach.py` floods the 47x17-block overworld (503,808 tiles) with warps, field-move gates and ledges.

Checks that passed: row-major tile order (door and `WARP_*` behaviours sit under the zone-event warps: 77 DOOR, 70 WARP_* of 1,317 vs near zero transposed); behaviour ids line up (`WATER_SEA` = 0x15 on sea tiles); with every gate open the walk covers New Bark, Route 29/46/30/27 and Cherrygrove and the 14 interiors behind them.

What fails: the walk stops at Route 30 north (556,288-289) and again at Route 31 (520-525,266; 544-547,268). Those tiles read `0x8000` (blocked, behaviour 0) yet players cross them: of 26 played-save positions, 3 are reachable (Cherrygrove, New Bark and the start), Violet (496,274) is not. Opening only the `HEADBUTT` behaviour makes Violet reachable but opens 119,279 tiles, which is far too permissive. So bit 15 is not the whole collision rule for those tiles; the cause is unknown (candidates: collision also derived from the building/object layer of the land-data member, or tile heights). Until the 26 positions are reachable, the per-gym gate sets would be wrong, so none are shipped. `--probe X,Z` and `--open BEHAVIOURS` in `map_reach.py` are the calibration hooks.

F15 follow-up. Bit 15 as "blocked" is supported by the saves: of the 26 distinct played positions, 16 are on overworld tiles with bit 15 clear, 7 are interior-map coordinates with no overworld tile, and 3 are on bit-15 tiles (entrances) (Blackthorn 676,177 and 669,180; Ruins of Alph 441,287). The failure is connectivity: with doors, ledges and water all treated as free, the 503,808 tiles split into 521 components of at most 8,290 tiles, and Cherrygrove/New Bark (one component) is separate from Violet, Azalea, Goldenrod, Ecruteak and Cianwood. The Route 30 corridor (550-552, 288-289) dead-ends against a 4x4 block of `0x8006` tiles (550-553, 284-287) although the zone event for Route 30 and 31 lists no object or warp there, so the join between the Route 30 and Route 31 blocks is not in the tile attributes as read. An emulator walk from the HG Cherrygrove save did not help: the player did not move under a held UP because an NPC stands on the tile north of the start (live structs show an NPC at 564,391). Open: find how the game joins blocks (candidates: runtime matrix edits by scripts, the object/model sections of the land-data member, or a loader detail not in `src/map_matrix.c`), then rerun the 26-position check.

## Open work from this log

1. ~~Runtime-test the flag-`0x966` + `dynamicWarp` teleport (F5).~~ Done; shipped as `location warp`. Remaining: test on SS and other base saves, and a broader sample of the 1,317 table entries.
2. ~~Name trainer flags in the registry (F2).~~ Done (733 derived entries).
3. Decide what a progress preset must clear as well as set (F1); needs same-milestone samples to separate story from player choice.
4. Identify the remaining unmodelled tables (F6) and the `FLAG_UNK_*` ids that actually change across milestones.
