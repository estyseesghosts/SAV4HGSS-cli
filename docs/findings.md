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

## E. Save mechanics (FMT-01, FMT-02, FMT-08)

| ID | Result | Evidence | Status |
|---|---|---|---|
| FMT-B | The main-block footer is `{count, size, magic, slot, crc}` in the last `0x10` bytes. The dword at `-0x14`, which PKHeX calls a "major counter", is the last payload dword and is `0` in all four chunk copies of `Lyra - Pichu.sav` | SOURCE (`save.c:329-376`, `include/save.h`), SAVE_DIFF | holds |
| FMT-C | The save counter is one global value. A save writes `loaded count + 1` into the partition it did *not* load, so the partitions stay one apart and trade the lead every save | SOURCE (`save.c:622-635`, `save.c:425`), RUNTIME (S0 to C1 to C2) | holds |
| FMT-D | A boot loads one whole partition, and the general and storage counters inside it must agree. A per-block split is not loadable | SOURCE (`save.c:429-526`), RUNTIME (7 crafted states) | holds |
| FMT-E | A chunk with a bad CRC costs its whole partition, not just the chunk: the game loads the other partition and says "The save file is corrupted. The previous save file will be loaded." | RUNTIME, SOURCE (`save.c:95-96`, `check_savedata.c:260-263`) | holds. Also the Ash HG exhibit, `save.c:503-515` |
| FMT-F | With no consistent partition the game shows "The save file will be erased due to corruption or damage." and starts a new game | RUNTIME, SOURCE (`check_savedata.c:256-259`) | holds |
| FMT-G | The 29-save corpus cannot separate PKHeX's per-block rule from the game's per-partition rule: all 29 are `IS_GOOD` and both rules name the same partition in 29/29 | SAVE_DIFF (`research/save-selection.json`) | holds |
| FMT-H | `hgss-save blank` produced chunks the game would reject: PKHeX writes only the CRC, leaving size, magic and slot zero. Blanks now carry the full footer | SOURCE (`save.c:338-347`), INTERVENTION | corrected, `HgssFactory.WritePartitionFooters` |
| FMT-I | A PKHeX no-op export differs from its input in 5 bytes: the forced box mask `0x3FFFF` at storage+`0x12004` in the active partition and that partition's storage CRC. The mask survives the next in-game save, which writes the other partition, and is gone once the game writes that partition - the second save. A PKHeX export is loadable as-is | INTERVENTION, RUNTIME (`research/fmt-04-roundtrip.json`) | holds |
| FMT-J | A general-only edit cannot undo a newer PC block, because that state cannot be loaded: the game takes one partition and a split is `TOTAL_FAIL`. The edit itself changes the flag byte, both CRCs and the 3-byte forced box mask; no box byte moves, and the edit survives a boot and a save into the other partition | INTERVENTION, RUNTIME | holds |
| FMT-K | The six auxiliary chunks are written at `sector` and `sector+64`, which is the same slot in the other partition, not two copies inside one (flash is 128 sectors of `0x1000`; `save.c:906-907`). `SAVE_MISC` is general `0x2064`: the key table `0x230C` is `+0x2A8` and `rivalName 0x22D4` is `+0x270`, both matching the decomp struct and PKHeX. | Runtime, 9 boots (`research/aux-matrix.json`) | Answered |
| FMT-L | Battle Hall and Battle Video recovery depends on the `prefer` byte, not on `saveno`: the same corrupted copy 1 warns ("The most recent Battle Hall record is corrupted.") when `prefer = 0` and is silent when `prefer = 1`. No valid copy gives "The Battle Hall record has been erased." With `extraChunksExist = 0` the check never runs and both a dead pair and a good pair are silent. | Runtime, 9 boots | Answered |
| FMT-M | A no-op in-game save does not touch the auxiliary region at all: 0 of ~780 changed bytes fall in `0x23000-0x2F000` of either partition, including when the chunk is dead and the boot printed a recovery message. The repair, when it happens, goes to `SAVE_MISC` (`prefer`), not to the chunk. | Runtime, 3 saves | Answered |

### The counter and the write target

`Save_WriteManInit` takes `saveData->saveCounter`, stores it in the write manager and increments the field (`save.c:622-624`); the footer is built from it (`save.c:370`). At boot the field was set from the chosen partition's footer (`save.c:425`), so the value written next is `chosen count + 1`. The chunk goes to the partition that was *not* loaded (`save.c:635`, `writeMan->curSector` written with `lastGoodSector == 0`).

Measured on `hgss-saves/Lyra - Pichu.sav`, one save per boot (`tools/save_once.py`), all four chunk CRCs valid at every step:

| save | general count p0 | storage count p0 | general count p1 | storage count p1 | loaded | written |
|---|---|---|---|---|---|---|
| S0 input | 10 | 10 | 9 | 9 | p0 | - |
| C1 | 10 | 10 | 11 | 11 | p0 | p1 |
| C2 | 12 | 12 | 11 | 11 | p1 | p0 |

C2's p0 is 12, not 11: the counter is not per partition. The two partitions stay one apart and the leading one flips each save.

### What one no-op save changes

C1 to C2 differs in 28 bytes: 27 in the general chunk and 1 in the storage footer.

| Offset (chunk-relative) | Change |
|---|---|
| `+0x0B..+0x0F` | clock/timestamp bytes |
| `+0x14`, `+0x2294` | two 20-byte timestamp blocks |
| `+0x44` | one `u16` (two in the two saves) |
| `+0x86..+0x89` | play time (hours `u16`, minutes, seconds) |
| `+0xE4A`, `+0xE5C..+0xE66` | three script variables, in `EventWork` |
| `+0x0C0DE` | one byte |
| `+0x4434..+0x4513` | five `u32` timestamps, in the range F6 lists as unmodelled |
| `+0xF618` | general footer counter |
| `+0xF626` | general footer CRC |
| storage `+0x21A00` | storage footer counter only |

The storage payload is byte-identical; only its counter moves, which matches `numModifiedBoxes == 0` and the changed-box bits staying at `0`. Two `u32` timestamps (`+0x4460`, `+0x5BBC`, `+0x68A4`) also carry the save time, so `0x4434-0x4510` is live state, not a static table.

### Selection, from the game's own tree

`tools/fault_matrix.py` crafts one fault per image from C2 (each partition given a distinct play time: p0 `1:17`, p1 `2:34`), predicts the status with a port of `Save_GetSaveFilesStatus`, boots it, then saves once in-game and reads which partition moved. `loaded` comes from the play time carried into the written chunk (marker plus elapsed), not from the counter, because a fallback makes the counter repeat a value.

| fault | predicted | message | loaded | wrote | new counts p0/p1 |
|---|---|---|---|---|---|
| none (control) | `IS_GOOD` p0 | none | p0 | p1 | 12/13 |
| newer general CRC bad | `SLOT_FAIL` p1 | corrupted / previous loaded | p1 | p0 | 12/11 |
| newer storage CRC bad | `SLOT_FAIL` p1 | corrupted / previous loaded | p1 | p0 | 12/11 |
| older general CRC bad | `SLOT_FAIL` p0 | corrupted / previous loaded | p0 | p1 | 12/13 |
| newer general counter +1 | `SLOT_FAIL` p1 | corrupted / previous loaded | p1 | p0 | 12/11 |
| both general CRCs bad | `TOTAL_FAIL` | erased due to corruption | none | - | new game |
| p0 general + p1 storage bad | `TOTAL_FAIL` | erased due to corruption | none | - | new game |

All seven matched their prediction. Two consequences worth stating:

- A file whose general and storage blocks are current in different partitions is not "half valid": the game finds no consistent partition and erases the save. The old per-block reading of such a file named a state the game can never hold.
- After a fallback the next save writes count `loaded + 1`, which in this fixture is the value the rejected partition already had (12). A tool cannot conclude "no write happened" from an unchanged counter; the payload is the evidence.

Evidence: `research/fmt-matrix.json`, screenshots under `leveld-out/fmt-matrix/<case>/` (`d2`/`d3` hold the message), and the per-case `after_save.sav`.

### Corpus check

`tools/save_selection_scan.py` ports the same tree and runs it over the corpus: 30 files, 29 unique, all `LOAD_STATUS_IS_GOOD`, 17 loading p0 and 12 loading p1. PKHeX's per-block pick agrees with the game in all 29. So the corpus contains no counterexample either way; the crafted states are what distinguish the rules, which is why the CLI had to be changed rather than its inputs re-examined.

### PKHeX no-op export, loaded and resaved (FMT-04)

`hgss-save patch leveld-out/fmt-lyra-c2/out.sav --recipe <empty> --out ...` gives an export with no
semantic diff and 5 changed bytes: the box-changed mask at storage+`0x12004` goes `0x000000` to
`0x03FFFF`, and that partition's storage CRC follows. PKHeX forces the mask so the game rewrites every
box on its next save (`SAV4HGSS.cs:52-58`).

| stage | counters p0/general | mask p0 | mask p1 | play time p0 / p1 |
|---|---|---|---|---|
| game save (C2) | 12/12, 11/11 | `0x000000` | `0x000000` | 3:02:35 / 3:01:08 |
| PKHeX no-op export | 12/12, 11/11 | `0x03FFFF` | `0x000000` | 3:02:35 / 3:01:08 |
| after one in-game save | 12/12, 13/13 | `0x03FFFF` | `0x000000` | 3:02:35 / 3:02:58 |
| after two in-game saves | 14/14, 13/13 | `0x000000` | `0x000000` | 3:03:20 / 3:02:58 |

Each stage boots and saves normally; both saves changed only the written partition (7 bytes, then 12).
The export is loadable as it stands, and its partition is the one the game loaded: the first save was
written to the other partition and carried the loaded play time (3:02:35 plus 23 s).

The forced mask does not converge on first use. It is payload, so it survives until the game rewrites
that partition, which happens on the second save. That is also why the second save jumps p0 from 12 to
14: the footer counter is `loaded + 1`, not `previous + 1`.

Not measured: the "saving a lot of data" path (`Save_NumModifiedPCBoxesIsMany`, >= 6 boxes). The mask is
read at load into `boxModifiedFlags` (`save.c:98`), so the first save enters with all 18 boxes marked,
but the write finished inside the driver's 1,500-frame window and the message was not captured.

### General-only edits and the PC block (FMT-05)

The question assumes the general block and the PC block can be current in different partitions. That state
does not survive a load: the game selects one partition (FMT-01, FMT-02), and the crafted `cross-partition`
file is `TOTAL_FAIL` - it erases the save rather than mixing blocks (`research/fmt-matrix.json`).

What a general-only edit actually writes, measured on C2 (`sha 5fe1aba3...`) with
`hgss-save flag set <save> 0x200 true` (`sha 8cfc7866...`): 8 bytes, all in the active partition.

| offset | change | region |
|---|---|---|
| general `+0x1104` | `8A` to `8B` | the flag bit (0x10C4 + 0x200/8) |
| general `+0xF626` | new CRC | footer |
| storage `+0x12004..+0x12006` | `00 00 00` to `FF FF 03` | forced box mask |
| storage `+0x21A0E` | new CRC | footer |

No byte of box data (storage `0` to `0x12004`) changes, and the diff reports 0 unclassified bytes. So an
edit described as "general only" is not literally storage-neutral - PKHeX sets the box mask in the active
partition's storage so the game rewrites every box - but it does not touch the PC payload.

Booted that file and saved once: 8 bytes changed, the flag propagated into the written partition
(`+0x1104` `8A` to `8B` there), and afterwards both partitions' box data is byte-identical. The mask in the
loaded partition stays as PKHeX wrote it, the same way it does after a no-op export (FMT-04).

The writer side is closed by refusal. `hgss-save patch` on `cross-partition` and on
`newer-general-crc-bad` exits 4 and creates no output:

- `TOTAL_FAIL` - "The save is not in the state the game would load ... The game finds no consistent
  partition and erases the save (\"The save file will be erased due to corruption or damage.\")"
- `SLOT_FAIL` - "... The newest partition is inconsistent, so the game loads partition B and announces
  \"The save file is corrupted. The previous save file will be loaded.\""

The preflight runs before PKHeX's own checksum gate, so the refusal names the game's status instead of
"Small block checksum is invalid", which PKHeX emits because it picks the newer copy without validating it
(`HgssFactory.cs`).

### Auxiliary chunks: where the copies live, and what a recovery costs (FMT-06, FMT-07)

The six extra chunks (Hall of Fame, Battle Hall, four Battle Videos) are listed in
`gExtraSaveChunkHeaders` (`save_arrays.c:334-373`) and are at partition-relative `0x23000`,
`0x26000`, `0x27000`, `0x29000`, `0x2B000`, `0x2D000`. Each is written twice, at `hdr->sector` and
`hdr->sector + 64` (`save.c:906-907`). Since flash is 128 sectors of `0x1000` and a partition is 64
sectors, the second write is the **same slot in the other partition**. There is one copy per partition,
and PKHeX compares exactly that pair (`SAV4.cs:189`, `CompareExtra(Data, Data[PartitionSize..], block)`).

`SAVE_MISC` sits at general `0x2064`, established two ways rather than assumed. Its `unk_02A8[2][5]` and
`unk_02D0[5]` (`save_misc_data.h:55-56`) are the aux key table, which PKHeX calls `Extra = 0x230C`
(`SAV4HGSS.cs:72`): `0x230C - 0x2A8 = 0x2064`. Independently, `rivalName` is at `+0x270` in the struct
(`save_misc_data.h:45`) and at `0x22D4` in PKHeX (`SAV4HGSS.cs:196`): `0x2064 + 0x270 = 0x22D4`. The
`extraChunksExist` bit is bit 0 of `+0x29B` (`save_misc_data.h:51`); in the corpus it is set in exactly
the one save that carries initialized aux chunks (`Gen 4 - Pokemon HeartGold (ENG).sav`) and clear in the
four Hall-of-Fame-bearing saves that carry `0xFFFFFFFF` in every key slot.

The gate matters. `Save_CheckFrontierData` (`save.c:540`) returns before looking at any chunk when
`extraChunksExist` is clear, and skips any chunk whose key pair is `-1` (`save.c:542`). `gate-off-both-bad`
has both Battle Hall copies dead and the gate cleared, and the boot is silent; `both-bad` is the same
corruption with the gate set, and the boot prints "The Battle Hall record has been erased."

For chunks 1-5 the `saveno` is never compared (`save.c:1057-1078`). What decides is the `prefer` byte
(`SAVE_MISC + 0x2D0[id-1]`), which selects which of the two stored keys is tested first, and only falling
through to the second one sets `ret2_p`, which becomes `statusFlags |= 4`:

| corruption | `prefer` | message | source |
|---|---|---|---|
| copy 1 dead | 0 | "The most recent Battle Hall record is corrupted." | `save.c:1022-1030`, `save.c:546` |
| copy 1 dead | 1 | none | `save.c:1031-1040` |
| copy 2 dead | 0 | none | `save.c:1057-1070` |
| copy 2 dead | 1 | "The most recent Battle Hall record is corrupted." | `save.c:1071-1078` |
| both dead | either | "The Battle Hall record has been erased." | `save.c:1087-1095`, `save.c:544` |
| any, gate off | either | none | `save.c:540` |

Every row booted and the message matched. `copy2-newer` was built to test whether `saveno` could break
the tie between two valid copies; it does not. With both keys equal and `prefer = 0` the partition-A copy
is taken silently even though partition B advertises `saveno = 2`. The first draft of the predictor
assumed `saveno` ordering here, the boot disproved it, and the predictor was corrected against
`save.c:1057-1078` rather than patched to fit the observation.

A no-op save leaves the whole aux region alone: in `control-clean`, `copy1-bad-prefer0` and `both-bad`,
zero of the roughly 780 changed bytes fall inside `0x23000-0x2F000` of either partition. The one thing a
recovery does write is `SAVE_MISC`: `copy1-bad-prefer0` comes back with `prefer[0] = 1`, which records that
copy 2 is now the current one, while the dead copy is not repaired on load. `both-bad` comes back with
`prefer` unchanged at 0 and the chunk still dead on both sides, so "erased" describes the status flag the
game shows, not a write.

### Consequence in the CLI

`src/HgssSave.PKHeX/Preflight.cs` now implements the game's tree, reports it as `blocks.load_status`, keeps `active_general` and `active_storage` equal, and refuses any image that is not `IS_GOOD` with the reason. `tests/HgssSave.Tests/SelectionTests.cs` covers the six loadable/unloadable shapes. Before this, `Preflight` compared the `-0x14` dword first and selected each block independently, so a split or damaged image could be read as a valid save.

