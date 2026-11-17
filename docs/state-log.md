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

## Open work from this log

1. ~~Runtime-test the flag-`0x966` + `dynamicWarp` teleport (F5).~~ Done; shipped as `location warp`. Remaining: test on SS and other base saves, and a broader sample of the 1,317 table entries.
2. ~~Name trainer flags in the registry (F2).~~ Done (733 derived entries).
3. Decide what a progress preset must clear as well as set (F1); needs same-milestone samples to separate story from player choice.
4. Identify the remaining unmodelled tables (F6) and the `FLAG_UNK_*` ids that actually change across milestones.
