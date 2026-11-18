# HGSS save file format

Reference for the raw US HeartGold/SoulSilver save (`SAV4HGSS` in PKHeX). It describes what is in the file, where, and which parts the game treats as persistent story state. It replaces `docs/archive/SAV4HGSS.md` as the document to read; that file is the original walkthrough, has rendering damage (collapsed code blocks, favicon artifacts), and contains one error (the TM pouch size, see the end of this file).

Sources, in order of authority: the game's own code (`pret/pokeheartgold` at `4e95633`), PKHeX.Core 26.10.18 (`SAV4HGSS.cs`, `SAV4.cs`, `SAV4BlockDetection.cs`, `BlockInfo4.cs`, `PlayerBag4HGSS.cs`), and measurements on the 29-save corpus (`docs/corpus.md`). Each claim names its source. Anything marked *unverified* is carried over from `docs/save-format.md` without an independent check.

Offsets are hexadecimal. "General-relative" means relative to the start of the active general block.

## 1. Physical layout

A raw save is `0x80000` bytes (512 KiB): two partitions of `0x40000`. Each partition holds the same set of blocks at the same relative offsets.

| Block | Partition-relative offset | Size | Source |
|---|---|---|---|
| General | `0x00000` | `0xF628` | `SAV4HGSS.cs:34` |
| Gap | `0x0F628` | `0xD8` | `SAV4HGSS.cs:36` |
| PC storage | `0x0F700` | `0x12310` | `SAV4HGSS.cs:35` |
| Hall of Fame | `0x23000` | `0x2AC0` | `SAV4HGSS.cs:40-48` |
| Battle Hall | `0x26000` | `0x0BB0` | same |
| Battle Video 0-3 | `0x27000`, `0x29000`, `0x2B000`, `0x2D000` | `0x1D60` each | same |

Partition B is the same layout plus `0x40000`.

The two partitions are not "active" and "backup". The game alternates writes, and each block is resolved on its own. The general block can be current in partition A while the storage block is current in partition B. Editing byte `0x1234` in the first half of a file may do nothing because the game reads `0x41234`. A tool must select the active copy per block. `hgss-save inspect` reports the choice as `blocks.active_general` and `blocks.active_storage`. In the corpus, a played in-game save flips the active partition (A to B in the `walk-save-loop` run, `docs/archive/story-state-field-knowledge.md` §8).

## 2. Footers, counters, checksums

### Main blocks (general, storage)

The last `0x14` bytes of each main block:

| From block end | Size | Field |
|---|---|---|
| `-0x14` | 4 | major revision counter |
| `-0x10` | 4 | minor revision counter |
| `-0x0C` | 4 | block size (general: `0xF628`; observed `u32[1]=0xF628` in real saves) |
| `-0x08` | 4 | magic: `0x20060623` (JP/international, which includes US) or `0x20070903` (Korean), `SAV4.cs:117-119` |
| `-0x04` | 2 | metadata (not interpreted) |
| `-0x02` | 2 | CRC16-CCITT |

Absolute positions inside a partition:

| Field | General | Storage |
|---|---|---|
| major counter | `0xF614` | `0x219FC` |
| minor counter | `0xF618` | `0x21A00` |
| size | `0xF61C` | `0x21A04` |
| magic | `0xF620` | `0x21A08` |
| CRC | `0xF626` | `0x21A0E` |

**Which copy is newer** (`SAV4BlockDetection.CompareFooters`, `SAV4.cs:180-184`): compare the major counters; if equal, compare the minors. `0xFFFFFFFF` means uninitialized and loses, except for the rollover case. Equal counters resolve to partition A. Checksums play no part in this choice. A validator should still check each candidate's CRC, size and magic before trusting it; the CLI's strict open does that and refuses anything it cannot verify instead of repairing it.

**CRC**: CRC16-CCITT, polynomial `0x1021`, initial value `0xFFFF`, no final XOR, stored little-endian. It covers the block minus its last `0x10` bytes (`SAV4.cs:113`, `FooterSize = 0x10`), which is `0xF618` bytes for the general block and `0x12300` for storage. The major counter is inside the covered range; the minor counter, size, magic, metadata and the CRC itself are not. Python equivalent: `binascii.crc_hqx(block[:-0x10], 0xFFFF)`.

### Auxiliary blocks (Hall of Fame, Battle Hall, Battle Videos)

These use `BlockInfo4` (`BlockInfo4.cs`) and a different footer in the block's last `0x10` bytes:

| Footer offset | Field |
|---|---|
| `0x00` | magic |
| `0x04` | revision |
| `0x08` | recorded size (`0x10` smaller than the allocated length, `IsSizeValidExtra`) |
| `0x0C` | block id (u16) |
| `0x0E` | CRC16-CCITT over the block except its last 2 bytes |

Selecting the active copy differs from the main blocks. Hall of Fame compares revisions and a copy is valid when its revision is not `0xFFFFFFFF` and the size matches. Battle Hall and Battle Videos start with a u32 key; `0xFFFFFFFF` means uninitialized or deleted. Which partition is checked first comes from bytes in the general block (`SAV4.cs` `GetActiveExtraBlock`, keys at `Extra` = `0x230C`). Do not apply the main-block selection or checksum rules to these.

## 3. General block map

Offsets are general-relative. Start offsets are from `SAV4HGSS.cs` unless another source is given. Gaps between entries hold fields not listed here.

| Offset | Content |
|---|---|
| `0x0000` | adventure/system header: RTC, MAC, timestamps. Start date at `0x34`, first Hall of Fame date at `0x3C` (seconds since 2000-01-01) |
| `0x0064` | trainer profile (section 4) |
| `0x0094` | party count (u8, `Party - 4`) |
| `0x0098` | party, 6 x `0xEC` (236) bytes |
| `0x0644` | bag, 8 pouches (section 7) |
| `0x0DE4` | script variables, 368 x u16 (section 5) |
| `0x10C4` | persistent event flags, 364 bytes (section 5) |
| `0x1234` | `LocalFieldData`: five `Location` structs, music, weather, spawn (section 6) |
| `0x12B8` | Pokédex (`Zukan4`) |
| `0x15FC` | daycare |
| `0x22D4` | rival name |
| `0x230C` | auxiliary block keys |
| `0x236E`, `0x2372`, `0x2376` | PKHeX X2, Y2, Z |
| `0x3F64` | Fashion Case |
| `0x440C` | group data |
| `0x4B3C` | game records (statistics) |
| `0x4E20` | seals |
| `0x4E74` | chatter |
| `0x5BB8` | BP and Battle Tower seed |
| `0x6890` | gift ribbons |
| `0x68B4` | roamer records |
| `0x8D44` | Geonet |
| `0x9D3C` | Mystery Gift, `0x1328` bytes |
| `0xB064` | Lock Capsule PCD slot |
| `0xBAE7` | map unlock state, bits 3-4 |
| `0xC0EC` | Pokégear rolodex |
| `0xC13C` | Safari Zone |
| `0xD9D4` | Pokéathlon |
| `0xE558` | Apricorns |
| `0xE5E0`, `0xE704` | Pokéwalker pairing, counters |

Regions the CLI models: party, bag, variables, flags, trainer, location, Pokédex. Everything else is carried through unchanged. `tools/residual_diff.py` masks the modelled regions and diffs the rest between consecutive saves; the clusters it found are in section 9.

## 4. Trainer profile (`0x64`)

| Offset | Size | Field |
|---|---|---|
| `0x64` | 16 | name, Gen IV character encoding (not UTF-8) |
| `0x74` | u16 | TID |
| `0x76` | u16 | SID |
| `0x78` | u32 | money (PKHeX max 999,999) |
| `0x7C` | u8 | gender |
| `0x7D` | u8 | language |
| `0x7E` | u8 | Johto badge mask |
| `0x7F` | u8 | avatar |
| `0x80` | u8 | game version code |
| `0x83` | u8 | Kanto badge mask |
| `0x84` | u16 | Game Corner coins |
| `0x86` | u16 | hours played |
| `0x88` | u8 | minutes |
| `0x89` | u8 | seconds |

Badges are two separate bytes, not one 16-bit field. Bit order is the gym order within each region. The Johto mask 255 means all eight; the `hg-kanto-4` preset (four Kanto badges) has Kanto mask 60, so the bits are not necessarily the first four (`registry/presets/index.json`).

Trainer name, TID/SID, MAC and clock fields identify a playthrough. Presets and scenes never overwrite them.

## 5. Script variables and event flags

This region is the core of story state.

### Variables

368 variables x u16 at `0x0DE4..0x10C3`. Variable ids run `0x4000..0x416F`.

```
offset = 0xDE4 + 2 * (varId - 0x4000)
```

`SetWork` takes the zero-based slot (`SAV4.cs:552-553`). Example: `VAR_PLAYER_STARTER` = `0x4030`, slot 48, offset `0xE44`.

The id space is partly runtime-only. From `vars.h` and the registry classification (`registry/source-manifest.json`):

| Range | Registry kind | Count | Writable by the CLI |
|---|---|---|---|
| `0x4000-0x401F` | `temporary_var` (`VAR_TEMP_x4000..`) | 32 | no |
| `0x4020-0x402F` | `temporary_var` (object graphics, `VAR_OBJ_*`) | 16 | no |
| `0x4030-0x416F` | `save_work` | 320 | yes |
| `0x8000+` | `special_runtime_var` | 14 | no, not in the save at all |

The temp slots exist in the save array, and saves do contain nonzero values there (temp var `0x4000` takes values 0 to 7 across the HG lineage, `docs/archive/state-log.md` F7). The game clears them at map transitions (`ClearTempFieldEventData`, `src/script_manager.c`), so they are not state to restore. Six slots have no symbol in the registry: `0x4000-0x4002`, `0x4013`, `0x4020`, `0x4021`.

### Flags

2,912 flags (ids `0x000..0xB5F`) as bits, 364 bytes at `0x10C4..0x122F`.

```
byte = 0x10C4 + (flag >> 3)
bit  = flag & 7
```

`SetEventFlag` takes the flag number directly (`SAV4.cs:545-550`). Example: `FLAG_CAUGHT_LUGIA` = `0x117` is byte `0x10E6`, mask `0x80`.

Id ranges (`flags.h`, `docs/archive/state-log.md` F2):

| Range | Meaning |
|---|---|
| `0x001..` (64) | map-temporary flags, reset on map transition (`MAPTEMP_FLAG_BASE`, `flags.h:15-16`) |
| `0x040..0x54F` | named and `FLAG_UNK_*` story flags, `FLAG_HIDE_*` actor visibility, `FLAG_GOT_*`, `FLAG_CAUGHT_*`, hidden items (`800..1030`) |
| `0x550..` | trainer-defeated flags, `FLAG_TRAINER(tr) = 0x550 + trainerId` (`flags.h:1394-1395`) |
| `0xAA0..0xB5F` (192) | daily flags, reset by `ClearDailyFlags` (`DAILY_FLAG_BASE`, `flags.h:2027-2028`) |
| `>= 0x4000` | `FLAG_TEMP_x4000...`: a separate in-memory array, never in the save |

Writable classes in the CLI are `persistent_flag` (2,847 registry symbols) and `save_work`. Map-temp, `FLAG_TEMP_x*`, temp/object vars and special vars are classified and rejected.

Names come from `flags.h`. Many are `FLAG_UNK_xxx`; the label says the decomp has not named it, not that the flag is unused. 1,660 of 9,957 set-flag instances in the corpus were unnamed before the registry derived `FLAG_TRAINER_<NAME>` for the 733 trainer flags that `flags.h` defines only as a range (`docs/archive/state-log.md` F2).

Setting `FLAG_CAUGHT_LUGIA` is not completing the Lugia story. Scripts also read other flags, variables, items, and actor visibility. See `docs/story-state-model.md`.

## 6. Position and field state

PKHeX exposes map/X/Y as `0x1234 / 0x123C / 0x1240` (u16 each, `SAV4HGSS.cs:186-188`); writing X also writes X2 (`0x236E`), Y also writes Y2 (`0x2372`); Z is `0x2376`. That is the visible part of a larger structure.

`LocalFieldData` (`src/save_local_field_data.c:9-30` in the decomp, offsets measured on all 29 corpus saves with `tools/field_locations.py`):

| General-relative | Field |
|---|---|
| `0x1234 + 20*0` | `currentPosition` |
| `0x1234 + 20*1` | `entrancePosition` |
| `0x1234 + 20*2` | `previousPosition` |
| `0x1234 + 20*3` | `dynamicWarp` |
| `0x1234 + 20*4` | `specialSpawn` (the town fly point) |
| `0x1298` | `musicId` (u16) |
| `0x129A` | `weather` (u16) |
| `0x129C` | `lastSpawn` (blackout spawn id) |

Each `Location` is five 32-bit ints: `mapId, warpId, x, y, direction` (20 bytes; `field_types_def.h:10-16`). The save also holds `SAVE_MAP_OBJECTS`: 64 `SavedMapObject` records, the actors of the current map (`save_local_field_data.c:33-35`). The `0x23B8-0x2740` table is a candidate for it but is not yet attributed.

Consequences:

- A position edit that changes only `currentPosition` leaves the entrance, previous warp, dynamic warp, spawn and actors inconsistent.
- Save Z is not the facing direction. Real saves carry Z=4 (Mahogany) and Z=7 (Blackthorn) while directions run 0..3 (`global_fieldmap.h:4-9`).
- Continue has two paths (`FieldTask_ContinueGame_Normal`, `src/field_warp_tasks.c:415-441`). If flag `0x966` is set, the game takes `dynamicWarp`, clears the flag, resolves `warpId` to the warp event's tile from the map's zone events, and builds actors fresh from zone-event data. Otherwise it restores `currentPosition` and the 64 saved map objects. The game uses the first path after the Hall of Fame (`game_clear.c`). `hgss-save location warp` relies on it. Runtime evidence: `docs/archive/state-log.md` F5.

### In emulator RAM

The live `Location` is the same five-int struct in ARM9 main RAM, not a copy of the save bytes. Its address depends on the map: measured at `0x227d498`, `0x227d4d0`, `0x227d4f8` and `0x227d444` on different maps. Mirrors of it exist (for example `0x239aa40`) and go stale when the player moves. See `docs/verification.md`.

## 7. Party, Pokémon, bag, Pokédex

**Party.** Count at `0x94`, slots at `0x98 + i*0xEC`, six slots, `0x588` bytes. A party Pokémon is the 136-byte stored form plus 100 bytes of battle data.

**PK4 layout.** PID (4), flags (2), checksum (2), then four 32-byte substructures A-D, encrypted (128 bytes), then the 100-byte party extension. Encryption: a 32-bit LCG (`s' = 0x41C64E6D * s + 0x6073`) seeded with the Pokémon checksum XORs each 16-bit word with the generator's upper half; the substructures are also shuffled by an order derived from the PID. The party extension is XORed separately, seeded with the PID, with no shuffle. The Pokémon checksum is the 16-bit sum of the 64 decrypted words at `0x08..0x87`. It is unrelated to the block CRC. After any edit both must be valid. The CLI never synthesizes a Pokémon from a species id; it imports a known-good `.pk4` template and verifies the checksum.

**Bag** (`PlayerBag4HGSS.cs:9`, pouch sizes from `pokeheartgold items.h:34-41`, `src/HgssSave.PKHeX/HgssLayout.cs`). Entries are `u16 itemId, u16 quantity`.

| Pouch | Offset | Slots |
|---|---|---|
| Items | `0x0644` | 165 |
| Key Items | `0x08D8` | 50 |
| TMs/HMs | `0x09A0` | 101 |
| Mail | `0x0B34` | 12 |
| Medicine | `0x0B64` | 40 |
| Berries | `0x0C04` | 64 |
| Poké Balls | `0x0D04` | 24 |
| Battle Items | `0x0D64` | 32 |

The pouches end at `0x0DE4`, where the variables start; the offsets above chain exactly with these slot counts. PKHeX's pouch arrays differ from the physical sizes (192 ids for 165 general slots, 13 for 32 battle slots), so the CLI clamps to physical slots. `sav.Inventory` builds a detached copy; changes need `CopyTo` (`PlayerBag4HGSS.cs:26-31`). HMs cap at 1 and over-full writes are errors.

**Pokédex** at `0x12B8` (`Zukan4`): magic (4), then four `0x40`-byte bitfields at `+0x04` caught, `+0x44` seen, `+0x84` and `+0xC4` first/second-seen gender; bit index is `species - 1`. Adding a Pokémon to the party does not update the Pokédex; `party import` and `dex set` do it explicitly.

## 8. PC storage, Mystery Gift, other systems

**Storage block** (partition offset `0xF700`, storage-relative offsets). 18 boxes, each 30 x 136 bytes (`0xFF0`) padded to `0x1000`. A stored Pokémon is at `box*0x1000 + slot*136`. After the boxes: current box at `0x12000`, changed-box bitmask at `0x12004`, box names at `0x12008` (40 bytes each), wallpapers at `0x122D8`, box flags at `0x122EA`.

The changed-box bitmask matters. The game writes only boxes it believes changed. PKHeX sets it to `0x3FFFF` on every export (`SAV4HGSS.cs` `GetFinalData`) so an edited save is fully persisted by the next in-game save. Some Gen IV documents call `0x12004` a stored count; for HGSS write it as a bitmask.

**Mystery Gift** at `0x9D3C`, `0x1328` bytes: received flags `0x100`, eight PGT slots (`0x104` each), three PCD slots (`0x358` each). These flags are separate from the 2,912 event flags.

**Other**: roamers (`0x68B4`: Raikou, Entei, Latias, Latios, tracked outside the party and boxes), map unlock state (bits 3-4 of `0xBAE7`: Johto, JohtoPlus, JohtoKanto), Pokéwalker (step/Watt counters, 32-bit course mask), Safari Zone, Pokéathlon, Apricorn counts. None are modelled by the CLI.

## 9. What PKHeX rewrites, and what the save does not hold

**Export normalization.** `Write()` sets the changed-box bits to `0x3FFFF`, recomputes backup CRCs and extra-block footers. A load-then-save with no edits is therefore not byte-identical. The CLI's byte audit derives that baseline from PKHeX itself and requires every other changed byte to be a requested write or a recomputed checksum (`README.md` safety model).

**Unmodelled general-block areas** (`tools/residual_diff.py`, `docs/archive/state-log.md` F6). Between consecutive saves about 1,000-1,300 bytes change outside party, bag, variables and flags:

| Offsets | Content |
|---|---|
| `0x1C-0x30`, `0x78-0x89` | system header, play time |
| `0x1234-0x12A8` | `LocalFieldData` |
| `0x12B8..0x15F0` | Pokédex |
| `0x2368-0x2376` | X2/Y2/Z, changes every step |
| `0x23B8-0x2740` | regular repeating table, stride about `0x10-0x28`; unidentified (candidate `SAVE_MAP_OBJECTS`) |
| `0x4434-0x4510` | 4-byte entries, stride `0x2C`; unidentified |
| `0x4B3C-0x4CF8` | game records |
| `0x5BB8-0x5BBF` | BP and Battle Tower seed |
| `0x68A4-0x68B4` | swarm seed |
| `0xBAD4-0xBAE7` | map unlock state |
| `0xC0DE` | Pokégear rolodex |

**Not in the save at all.** A running script's program counter and pending commands, battle state, animation progress, `FLAG_TEMP_x*` and special variables. A `.sav` captures the state before and after a cutscene, not during it. Mid-cutscene states need an emulator savestate (`.mcpr`), which is never a `.sav`.

**Structural versus story validity.** A CRC-valid save is not a story-valid one. `hgss-save validate` reports `"semantic": "not-evaluated"` for that reason.

## 10. Containers

Only raw `0x80000` files are editable inputs. The corpus also contained:

- **DeSmuME `.dsv`**: raw save plus a `0x7A`-byte footer ending `|-DESMUME SAVE-|`. `container unwrap` removes it with size and hash checks.
- **NO$GBA `.SAV`**: a container whose data is RLE-compressed (u32 at `0x44` is 1 when compressed, packed size at `0x48`, unpacked size at `0x4C`; the files here declare packed `0x26CDA`, unpacked `0x80000`). It looked like garbage to a block scanner because the footer bytes were visible but no valid block existed at any offset. Decoded with `ndssc` (`docs/archive/story-state-field-knowledge.md` §4, §5 item 3). All 13 converted to byte-exact raws with valid checksums.

Non-HGSS files (Diamond, Pearl, Platinum, White) are rejected with exit 3. `Pokémon HeartGold.sav` in the corpus is actually a SoulSilver save; trust the identity the CLI reports.

## Corrections to `docs/archive/SAV4HGSS.md`

- It lists the TM/HM pouch as 100 slots. The pouch is 101: `0x09A0` to `0x0B34` is `0x194` bytes, 101 four-byte entries, and `HgssLayout.cs` `PhysicalSlots` returns 101.
- Its Pokédex, storage and Mystery Gift sections and the clean block-selection description are consistent with PKHeX as read for this document. Its sections 13-14 (generator pipeline, validation strategy) are proposals that the CLI has since implemented or revised; see `docs/story-state-model.md`.
