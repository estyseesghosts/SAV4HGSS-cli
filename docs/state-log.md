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

**Status: hypothesis, not yet proven at runtime.** To verify: build a save with flag `0x966` set and a chosen `dynamicWarp {map, warpId}`, boot it headless, read the RAM `Location`. If it holds, `location set` can be replaced by "set dynamic warp to a zone-event warp" for any map with warps, which also gives fresh actors and removes the need for the 64-object list to match.

Caveat on `registry/hgss-us.warp-spawns.json`: `spawn_registry_check.py` explained 0 of 20 safe spawns because it compared warp tiles with *current positions*. That was the wrong test. The registry is a derivation of zone-event warp tables, which is what this mechanism resolves at load. For the 7 distinct saved `dynamicWarp` (map, warpId) pairs in the corpus, the registry has a matching `warp_index` entry in every case, but the saved x/y equals the registry tile in only 1 of 6 non-zero cases (for example map 191 warp 2: saved `(19,3)`, registry `(19,2)`), consistent with the game recomputing x/y from the warp at load. Needs the runtime test above.

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

## Open work from this log

1. Runtime-test the flag-`0x966` + `dynamicWarp` teleport (F5).
2. Name trainer, hidden-item and daily flag families in the registry (F2).
3. Decide what a progress preset must clear as well as set (F1); needs same-milestone samples to separate story from player choice.
4. Identify the remaining unmodelled tables (F6) and the `FLAG_UNK_*` ids that actually change across milestones.
