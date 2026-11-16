# SAV4HGSS: Complete technical walkthrough

`SAV4HGSS` is PKHeX's implementation of the Pokémon HeartGold and SoulSilver save format. It is built on the shared Generation IV save system, but HGSS introduces different offsets, additional save blocks, padded PC storage, Johto-specific event state, the Pokéwalker, Pokéathlon, Safari Zone, and other features.

The most important distinction is that the save file is not one continuous, flat structure representing the player's current state. It contains redundant copies of several independently updated blocks. Within those blocks are smaller structures containing everything from Pokémon and inventory to map coordinates, script variables, and event flags.

For Kohjoh, understanding the format gives us three useful capabilities:

1. Save manipulation: Create or modify a valid HGSS save without depending on PKHeX's interface.
2. Story-state generation: Place a player at specific locations with the appropriate story progression, party, inventory, and unlocked events.
3. Regression testing: Generate repeatable save fixtures to exercise rendering, scripts, battles, transitions, and unusual game states.

The walkthrough below follows the current [SAV4HGSS.cs](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4HGSS.cs?utm_source=chatgpt.com) and [SAV4.cs](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4.cs?utm_source=chatgpt.com) implementations, supplemented by documented Generation IV save structures.

## 1. Physical save-file layout

# 512 KiB

Full raw save

# 2

Save partitions

# 0x40000

Bytes per partition

The standard raw HGSS save occupies `0x80000` bytes (524,288 bytes). It is divided into two `0x40000`-byte partitions.

Each partition has room for a general block, a Pokémon storage block, and several auxiliary blocks.

### Physical layout of one partition

General data

0x00000–0x0F627

Gap

0x0F628–0x0F6FF

PC Pokémon storage

0x0F700–0x21A0F

Gap

0x21A10–0x22FFF

Hall of Fame

0x23000–0x25ABF

Battle Hall

0x26000–0x26BAF

Four battle-video blocks

0x27000–0x2ED5F, with gaps

Remaining space

Through 0x3FFFF

Offsets are relative to one partition. The second partition contains corresponding regions at an additional offset of 0x40000.

The important regions are:

| Region              | Relative offset | Size      |
| ------------------- | --------------- | --------- |
| General block       | `0x00000`       | `0xF628`  |
| General/storage gap | `0x0F628`       | `0xD8`    |
| PC storage block    | `0x0F700`       | `0x12310` |
| Hall of Fame        | `0x23000`       | `0x2AC0`  |
| Battle Hall         | `0x26000`       | `0x0BB0`  |
| Battle Video 0      | `0x27000`       | `0x1D60`  |
| Battle Video 1      | `0x29000`       | `0x1D60`  |
| Battle Video 2      | `0x2B000`       | `0x1D60`  |
| Battle Video 3      | `0x2D000`       | `0x1D60`  |

These are the actual block sizes and offsets used by PKHeX, rather than assumptions taken from Diamond/Pearl or Platinum.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



### The two partitions are not necessarily synchronized

It is tempting to treat the first partition as the active save and the second as its backup. That is not reliably correct.

The current general block could be in partition A, while the current storage block is in partition B. Individual auxiliary blocks can also have different active copies.

For example:

| Component    | Partition A | Partition B |
| ------------ | ----------- | ----------- |
| General      | Current     | Previous    |
| Storage      | Previous    | Current     |
| Hall of Fame | Current     | Previous    |
| Battle Video | Previous    | Current     |

Consequently, a save editor must resolve each relevant block independently rather than select one partition and use everything inside it.

This is also why manually modifying `0x1234` in the first half of a save can have no effect: the game might load the corresponding data from `0x41234`.

## 2. Block footers, save counters, and checksums

Each main block has metadata at its end. This metadata establishes which copy is newer and allows the game to verify that the block is intact.

### Main-block footer layout

PKHeX locates the two revision counters starting `0x14` bytes before the end of the block. Its HGSS checksum routine excludes the final `0x10` bytes.

| Offset from block end | Size | Meaning                |
| --------------------- | ---- | ---------------------- |
| `-0x14`               | 4    | Major revision counter |
| `-0x10`               | 4    | Minor revision counter |
| `-0x0C`               | 4    | Block size             |
| `-0x08`               | 4    | SDK/date magic         |
| `-0x04`               | 2    | Block metadata         |
| `-0x02`               | 2    | CRC16 checksum         |

The actual positions are:

| Field         | General block | Storage block |
| ------------- | ------------- | ------------- |
| Major counter | `0xF614`      | `0x219FC`     |
| Minor counter | `0xF618`      | `0x21A00`     |
| Block size    | `0xF61C`      | `0x21A04`     |
| Magic         | `0xF620`      | `0x21A08`     |
| Checksum      | `0xF626`      | `0x21A0E`     |

These addresses assume partition A. Add `0x40000` for partition B.

### How the active block is selected

PKHeX's [SAV4BlockDetection](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/SAV4BlockDetection.cs?utm_source=chatgpt.com) implements the selection logic.

It compares the major revision counters first. If they match, it compares minor counters. It also treats `0xFFFFFFFF` specially as an uninitialized value, including provisions for counter rollover.

For example:

| Partition | Major | Minor | Selection  |
| --------- | ----- | ----- | ---------- |
| A         | 104   | 20    |            |
| B         | 105   | 3     | Current: B |

The minor counter in A is larger, but B wins because its major counter is newer.

The same operation is performed independently for the general and storage blocks.

Important: PKHeX's block chooser primarily compares the counters. A robust validator should additionally check each candidate's size, magic, and checksum before trusting its contents.

### CRC16-CCITT

The main blocks use CRC16-CCITT with:

| Parameter     | Value               |
| ------------- | ------------------- |
| Polynomial    | `0x1021`            |
| Initial value | `0xFFFF`            |
| Final XOR     | `0x0000`            |
| Stored format | Little-endian `u16` |

A straightforward Python implementation can use `binascii.crc_hqx`:

```
from binascii import crc_hqxfrom struct import pack_intodef update_main_crc(    save: bytearray,    offset: int,    block_size: int,) -> None:    # HGSS excludes the last 0x10 bytes from the CRC.    data_end = offset + block_size - 0x10    checksum_at = offset + block_size - 2    crc = crc_hqx(save[offset:data_end], 0xFFFF)    pack_into("<H", save, checksum_at, crc)
```

For the general block, the covered length is `0xF618` bytes. For storage, it is `0x12300` bytes.

This means that the main-block CRC includes the major revision counter but excludes the remaining footer fields.

### Auxiliary blocks behave differently

The Hall of Fame, Battle Hall, and Battle Videos use `BlockInfo4`, which defines a different footer.

Its final `0x10` bytes contain:

| Relative offset | Field    |
| --------------- | -------- |
| `0x00`          | Magic    |
| `0x04`          | Revision |
| `0x08`          | Size     |
| `0x0C`          | Block ID |
| `0x0E`          | Checksum |

Their CRC covers the entire auxiliary block except its final two bytes.

An additional complication is that the recorded size can be `0x10` smaller than the allocated auxiliary-block length. PKHeX explicitly accounts for this.

For the Hall of Fame, PKHeX compares revision counters. For Battle Hall and Battle Videos, it checks keys against values stored in the active general block, respecting the preferred partition.

Do not apply the main-block checksum or selection algorithm to auxiliary blocks.

Sources: [BlockInfo4](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Blocks/BlockInfo4.cs?utm_source=chatgpt.com) and [Checksums.cs](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Util/Checksums.cs?utm_source=chatgpt.com).

## 3. The general block

The general block is where most of the information relevant to story progression lives.

Unlike the PC storage block, it is a collection of unrelated game structures. It is not simply a list of sequential records.

### Key HGSS offsets

All addresses below are relative to the start of the active general block.

| Offset   | Structure or field                   |
| -------- | ------------------------------------ |
| `0x0000` | RTC and adventure information        |
| `0x0064` | Trainer profile                      |
| `0x0094` | Party count                          |
| `0x0098` | Party Pokémon                        |
| `0x0644` | Bag inventory                        |
| `0x0DE4` | Script variables                     |
| `0x10C4` | Persistent event flags               |
| `0x1234` | Current map ID                       |
| `0x123C` | Player X                             |
| `0x1240` | Player Y                             |
| `0x12B8` | Pokédex                              |
| `0x15FC` | Daycare                              |
| `0x22D4` | Rival name                           |
| `0x230C` | Auxiliary block references           |
| `0x236E` | Secondary X                          |
| `0x2372` | Secondary Y                          |
| `0x2376` | Position Z                           |
| `0x3F64` | Fashion Case                         |
| `0x440C` | Group data                           |
| `0x4B3C` | Records                              |
| `0x4E20` | Seals                                |
| `0x4E74` | Chatter                              |
| `0x6890` | Gift ribbons                         |
| `0x68B4` | Roaming Pokémon records              |
| `0x8D44` | Geonet                               |
| `0x9D3C` | Mystery Gift data                    |
| `0xB064` | Lock Capsule                         |
| `0xBAE7` | Map unlock state bits                |
| `0xC0EC` | Pokégear phonebook                   |
| `0xC13C` | Safari Zone                          |
| `0xD9D4` | Pokéathlon                           |
| `0xE558` | Apricorn counts                      |
| `0xE5E0` | Pokéwalker pairing data              |
| `0xE704` | Pokéwalker counters and course flags |

These are structure starts or known field offsets, not necessarily complete boundaries. Some fields not listed here occupy the spaces between them.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



### Trainer profile

The trainer profile begins at `0x64`.

| Offset | Type     | Field             |
| ------ | -------- | ----------------- |
| `0x64` | 16 bytes | Trainer name      |
| `0x74` | `u16`    | Trainer ID        |
| `0x76` | `u16`    | Secret ID         |
| `0x78` | `u32`    | Money             |
| `0x7C` | `u8`     | Gender            |
| `0x7D` | `u8`     | Language          |
| `0x7E` | `u8`     | Johto badges      |
| `0x7F` | `u8`     | Avatar            |
| `0x80` | `u8`     | Game version code |
| `0x83` | `u8`     | Kanto badges      |
| `0x84` | `u16`    | Game Corner coins |
| `0x86` | `u16`    | Hours played      |
| `0x88` | `u8`     | Minutes played    |
| `0x89` | `u8`     | Seconds played    |

The trainer name uses Generation IV's character encoding, not UTF-8. Its maximum ordinary displayed length is seven characters.

The TID and SID form a combined 32-bit identifier:

\\[ ID32 = TID + (SID \ll 16) \\]

HGSS stores its two badge sets separately, rather than as one adjacent 16-bit word.

For save generation, it is usually better to preserve an existing valid trainer profile and modify only fields needed by the fixture.

### Adventure information

The first `0x64` bytes hold system and adventure metadata, including RTC information, the DS MAC address, profile birthday, timestamps, and clock-change information.

PKHeX exposes the start and first Hall of Fame dates as seconds relative to January 1, 2000, using the 32-bit values at `0x34` and `0x3C`.

Clock-related fields matter when testing day/night lighting, weekday events, time-dependent Pokémon encounters, and other behavior tied to the RTC. The save does not, by itself, replace the emulator's clock.

## 4. Script variables and event flags

This is probably the most important part for Kohjoh's automated testing.

HGSS maintains two closely related kinds of story state: variables and flags.

### Script variables

VARIABLE REGION

## 0x0DE4 – 0x10C3

368 variables × 2 bytes = 736 bytes (`0x2E0`)

Every variable is a little-endian unsigned 16-bit value.

The decompilation identifies the script-variable ID range as `0x4000` through `0x416F`.

The mapping is:

\\[ \text{SaveOffset} = 0xDE4 + 2(\text{VarID} - 0x4000) \\]

For example, `VAR_PLAYER_STARTER` is defined as `0x4030`.

Its position is:

\\[ 0xDE4 + 2(0x4030-0x4000)=0xE44 \\]

A variable can represent a choice, a count, a stage of progression, or another value that the scripts need to retain.

Be careful with the first 32 script variables, which are conventionally designated temporary variables. Although the underlying save structure allocates 368 values, script execution can handle temporary state differently from long-lived progression state.

### Event flags

EVENT FLAG REGION

## 0x10C4 – 0x122F

2,912 flags = 364 bytes (`0x16C`)

Flags are individual bits, so eight flags fit into one byte.

Given flag number `n`:

\\[ \begin{aligned} \text{ByteOffset} &= 0x10C4 + (n \gg 3)\\\ \text{BitIndex} &= n\ \&\ 7 \end{aligned} \\]

Setting a flag means setting the corresponding bit. Clearing it means clearing the bit without disturbing the other seven.

The [pokeheartgold flag definitions](https://github.com/pret/pokeheartgold/blob/master/include/constants/flags.h?utm_source=chatgpt.com) provide meaningful names for many flags.

Some useful examples:

| Flag                      | ID      | Purpose              |
| ------------------------- | ------- | -------------------- |
| `FLAG_DEFEATED_WILL`      | `0xE4`  | Will defeated        |
| `FLAG_DEFEATED_KOGA`      | `0xE5`  | Koga defeated        |
| `FLAG_DEFEATED_BRUNO`     | `0xE6`  | Bruno defeated       |
| `FLAG_DEFEATED_KAREN`     | `0xE7`  | Karen defeated       |
| `FLAG_CAUGHT_HO_OH`       | `0x116` | Ho-Oh caught         |
| `FLAG_CAUGHT_LUGIA`       | `0x117` | Lugia caught         |
| `FLAG_RESTORED_POWER`     | `0x118` | Power Plant restored |
| `FLAG_UNLOCKED_MT_SILVER` | `0x12B` | Mt. Silver unlocked  |

For `FLAG_CAUGHT_LUGIA`, the byte offset is `0x10E6` and the mask is `0x80`.

```
def set_event_flag(    general: bytearray,    flag_id: int,    enabled: bool,) -> None:    if not 0 <= flag_id < 0xB60:        raise ValueError("Invalid HGSS event flag")    offset = 0x10C4 + (flag_id >> 3)    mask = 1 << (flag_id & 7)    if enabled:        general[offset] |= mask    else:        general[offset] &= ~mask & 0xFF
```

There is a crucial distinction between a flag identifying an event's outcome and the collection of state needed to reach that event.

Setting `FLAG_CAUGHT_LUGIA` is not equivalent to completing the Lugia storyline correctly. The game may also check other flags, variables, inventory, map-specific objects, and script conditions.

The relevant decompilation files are [save_vars_flags.c](https://github.com/pret/pokeheartgold/blob/master/src/save_vars_flags.c?utm_source=chatgpt.com), [vars.h](https://github.com/pret/pokeheartgold/blob/master/include/constants/vars.h?utm_source=chatgpt.com), and [flags.h](https://github.com/pret/pokeheartgold/blob/master/include/constants/flags.h?utm_source=chatgpt.com).

### Persistent versus temporary flags

The decompilation's flag access code distinguishes ordinary flags from temporary flags.

Persistent flags live in the save's flag array. Temporary flags use a separate in-memory array.

Therefore, not every script flag can be preserved simply by editing the `.sav`.

This is particularly important for cutscenes or transitions where scripts use temporary values during an event. A normal save file may represent the state before or after the sequence, without retaining every intermediate execution detail.

## 5. Player location and overworld state

The player position is stored in several fields, not just one coordinate pair.

| Offset   | PKHeX property | Type  |
| -------- | -------------- | ----- |
| `0x1234` | `M`            | `u16` |
| `0x123C` | `X`            | `u16` |
| `0x1240` | `Y`            | `u16` |
| `0x236E` | `X2`           | `u16` |
| `0x2372` | `Y2`           | `u16` |
| `0x2376` | `Z`            | `u16` |

`M` is the map identifier. `X`, `Y`, and `Z` relate to the stored position; the additional coordinate fields are maintained as well.

In PKHeX, writing `X` also writes `X2`, and writing `Y` also writes `Y2`.

That behavior is worth reproducing in a save editor.

However, a valid location is more than a map ID and coordinates. Loading a particular area also depends on its map header, object state, scripts, and potentially saved warp information.

For Kohjoh, a location fixture should therefore consist of a known-valid save position, with changes made to nearby coordinates only after confirming that the resulting tiles are accessible.

This avoids mistaking invalid game state for a renderer failure.

## 6. Party Pokémon and the PK4 format

The party starts at `0x98`.

The party count is stored at `0x94`, and the game supports up to six Pokémon.

Each party slot occupies `0xEC` bytes (236 bytes).

Thus:

\\[ \text{PartySlotOffset}(i) = 0x98 + i \cdot 0xEC \\]

The six slots collectively occupy `0x588` bytes.

### Pokémon storage structure

Each Pokémon has a core 136-byte structure. Party Pokémon add another 100 bytes for battle-related information.

| Region                    | Offset | Size |
| ------------------------- | ------ | ---- |
| Personality value         | `0x00` | 4    |
| Flags                     | `0x04` | 2    |
| Pokémon checksum          | `0x06` | 2    |
| Encrypted core data       | `0x08` | 128  |
| Encrypted party extension | `0x88` | 100  |

The core data consists of four 32-byte substructures conventionally named A, B, C, and D.

| Block | Main content                                                     |
| ----- | ---------------------------------------------------------------- |
| A     | Species, item, trainer IDs, experience, friendship, ability, EVs |
| B     | Moves, PP, IVs, ribbons, forms, encounter details                |
| C     | Nickname and related metadata                                    |
| D     | Original trainer, dates, locations, Poké Ball, walking mood      |

The party extension includes current status, level, HP, calculated stats, mail, and seal information.

### Pokémon encryption

Generation IV does not store the four core blocks in plain, fixed order.

The encryption procedure uses the Pokémon's checksum as the seed for a 32-bit linear congruential generator:

\\[ s\_{n+1}=(0x41C64E6D \cdot s_n+0x6073)\bmod 2^{32} \\]

Each 16-bit word is XORed with the upper 16 bits of the next generator state.

The substructures are also shuffled according to the personality value. PKHeX uses a 32-entry shuffle lookup indexed from bits of the personality value.

Decryption proceeds by XOR-decrypting the core data and restoring its canonical block order.

The party extension is independently XOR-encrypted using the personality value as its seed, without that four-block shuffling.

### Pokémon checksum versus save-block checksum

These are different checksums.

A Pokémon's internal checksum is the 16-bit sum of the 64 decrypted words from `0x08` through `0x87`.

The main save-block checksum is CRC16-CCITT over the encompassing save block.

If you modify a Pokémon's data, both layers must remain valid after re-encryption and save serialization.

HGSS also adds data fields for its walking Pokémon system and special Poké Balls.

Source: [PokeCrypto](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/PKM/Util/PokeCrypto.cs?utm_source=chatgpt.com) and [Generation IV Pokémon structure](https://bulbapedia.bulbagarden.net/wiki/Pok%C3%A9mon_data_structure_\(Generation_IV\)?utm_source=chatgpt.com).&#x20;

[image](https://www.google.com/s2/favicons?domain=https://bulbapedia.bulbagarden.net\&sz=32)

bulbapedia.bulbagarden.net



## 7. Inventory and key items

HGSS's bag starts at `0x644` in the general block.

Unlike the Pokémon structures, bag entries are straightforward little-endian pairs:

```
struct BagItem {
    uint16_t item_id;
    uint16_t quantity;
};
```

The pouch layout in PKHeX is:

| Pouch        | Offset   | Slots |
| ------------ | -------- | ----- |
| Items        | `0x0644` | 165   |
| Key Items    | `0x08D8` | 50    |
| TMs / HMs    | `0x09A0` | 100   |
| Mail         | `0x0B34` | 12    |
| Medicine     | `0x0B64` | 40    |
| Berries      | `0x0C04` | 64    |
| Poké Balls   | `0x0D04` | 24    |
| Battle Items | `0x0D64` | 32    |

Every entry takes four bytes. The pouch structures end immediately before the script-variable region.

PKHeX also applies item-specific quantity limits. For example, HMs are treated differently from ordinary TMs.

For story testing, inventory is important because possessing an item does not always mean the game considers its associated event completed.

The Silver Wing, Rainbow Wing, Clear Bell, Tidal Bell, and other key items should be considered alongside flags and script variables.

Source: [PlayerBag4HGSS.cs](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Items/Bags/PlayerBag4HGSS.cs?utm_source=chatgpt.com).

## 8. The Pokédex

The Pokédex begins at `0x12B8`.

PKHeX represents it with `Zukan4`, a structure shared with other Generation IV titles but containing HGSS-specific handling for additional forms.

It begins with a 32-bit magic value, followed by four `0x40`-byte bitfield regions.

| Relative offset | Content            |
| --------------- | ------------------ |
| `0x00`          | Magic              |
| `0x04`          | Caught species     |
| `0x44`          | Seen species       |
| `0x84`          | First-seen gender  |
| `0xC4`          | Second-seen gender |

Every species is represented by a bit at index `species - 1`.

The structure continues with a Spinda personality value, form records, language information, and Pokédex-related unlock state.

This separation matters when generating Pokémon. Adding a Pokémon to the party or a PC box is not the same operation as marking that species seen or caught in the Pokédex.

For normal game behavior, the appropriate Pokédex records should be updated consistently.

Source: [Zukan4.cs](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/PokeDex/Zukan4.cs?utm_source=chatgpt.com).

## 9. PC storage block

HGSS stores 18 PC boxes, each holding 30 Pokémon, for a total capacity of 540.

Each stored Pokémon uses 136 bytes.

A single box therefore contains:

\\[ 30 \times 136=4080=0xFF0\text{ bytes} \\]

However, HGSS pads each box to `0x1000` bytes.

### One PC box: 0x1000 bytes

30 × PK4 (0xFF0)

The final 0x10 bytes are padding. This pattern repeats 18 times.

A boxed Pokémon's address is:

\\[ 0xF700 + 0x1000b + 136s \\]

where `b` is the zero-based box index and `s` is the zero-based slot index.

The second partition uses the same calculation plus `0x40000`.

After the box data, the storage block contains metadata:

| Storage-relative offset | Content             |
| ----------------------- | ------------------- |
| `0x12000`               | Current box index   |
| `0x12004`               | Changed-box bitmask |
| `0x12008`               | Box names           |
| `0x122D8`               | Wallpapers          |
| `0x122EA`               | Box flags           |
| Near `0x12300`          | Storage footer      |

A particularly important detail is the changed-box bitmask at `0x12004`.

PKHeX sets this to `0x3FFFF` when exporting HGSS saves, marking all 18 boxes as changed.

Why? The original game can skip writing boxes it believes have not changed. If an external editor modifies storage without updating the bookkeeping, a subsequent in-game save may fail to preserve all edited boxes.

This is why PKHeX forces all boxes to be considered changed.

Also note that some general Generation IV documentation describes `0x12004` as a stored Pokémon count. PKHeX's HGSS implementation instead treats it as the changed-box bitfield. For writing HGSS saves, follow the latter behavior.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



## 10. Mystery Gifts

HGSS Mystery Gift data starts at `0x9D3C`, occupying a total of `0x1328` bytes.

Its structure is:

| Relative offset | Size    | Content                     |
| --------------- | ------- | --------------------------- |
| `0x000`         | `0x100` | Received-gift flags         |
| `0x100`         | `0x820` | Eight PGT gift slots        |
| `0x920`         | `0xA08` | Three PCD Wonder Card slots |

A PGT is `0x104` bytes; a PCD is `0x358` bytes.

The received-gift flags are separate from the game's ordinary 2,912 event flags.

PKHeX also recognizes a delivery-man flag at index 2047 and the Mystery Gift menu unlock state.

Immediately after the Mystery Gift region, at `0xB064`, HGSS has a dedicated Lock Capsule PCD slot.

This structure is useful when testing gift-related events, but it cannot substitute for the ordinary script flags and item state involved in an event.

## 11. HGSS-specific systems

Beyond the main trainer, Pokémon, and script state, HGSS includes a significant amount of game-specific data.

| System              | Offset   | Relevant contents                |
| ------------------- | -------- | -------------------------------- |
| Roamers             | `0x68B4` | Raikou, Entei, Latias, Latios    |
| Pokégear            | `0xC0EC` | Registered telephone callers     |
| Safari Zone         | `0xC13C` | Safari Zone configuration/state  |
| Pokéathlon          | `0xD9D4` | Pokéathlon records and state     |
| Apricorns           | `0xE558` | Apricorn quantities              |
| Pokéwalker          | `0xE5E0` | Pairing and related data         |
| Pokéwalker counters | `0xE704` | Steps, Watts, course unlock bits |

The roaming Pokémon records are not ordinary PC or party Pokémon. Their position and status must be tracked separately.

The map unlock state is stored in bits 3–4 at `0xBAE7`. PKHeX recognizes `Johto`, `JohtoPlus`, and `JohtoKanto`.

The Pokéwalker records include step and Watt counters and a 32-bit course unlock mask.

These areas illustrate why HGSS's save format cannot safely be treated as Platinum with a few changed offsets.

## 12. What a save file cannot represent

This distinction is particularly important for regression testing.

A save contains persistent game state, but not necessarily the complete state of a script or animation currently executing.

For example, imagine a legendary encounter:

1. Player enters the area with the required story flags and key items.
2. An overworld script starts and temporarily controls the player.
3. The script changes map objects, plays animations, and starts the battle.
4. The battle ends, and the script updates the persistent event flags.
5. The player regains control and can save again.

A conventional save can reproduce the persistent state at the beginning or end.

It does not necessarily capture an exact moment between the third and fourth steps, including the current program counter, pending commands, battle runtime state, active render effects, or animation progress.

That requires an emulator savestate or another runtime checkpoint mechanism.

For Kohjoh, the two mechanisms serve different purposes:

| Mechanism              | Best for                                                            |
| ---------------------- | ------------------------------------------------------------------- |
| HGSS `.sav`            | Progression, player location, inventory, world state                |
| Emulator savestate     | Exact execution point, mid-cutscene behavior, timing-sensitive bugs |
| Script/event injection | Triggering particular transitions and encounters                    |
| Input automation       | Exercising a real path through the game's scripts                   |

## 13. Generating saves for Kohjoh

I would build the regression tooling around a small, explicit save-state model.

## Proposed save generator pipeline

1

Load known-valid save

Read raw bytes and detect the active blocks

2

Apply story-state preset

Set variables, flags, map state, and prerequisites

3

Apply gameplay state

Build party, inventory, Pokédex, and supporting records

4

Finalize storage

Set changed-box flags and maintain block metadata

5

Recalculate checksums

Validate the main and initialized auxiliary blocks

6

Run game validation

Load in Lotus or on hardware, then verify expected behavior

### Use valid saves as the starting point

For a generator, I would initially avoid creating a completely new save from zero.

Instead, collect valid saves at known points in the game and modify copies.

That preserves less-understood structures, initialization data, and relationships between fields.

A preset would describe its intended state declaratively, for example:

```

id: whirl-islands-lugia-pre-event
game: soulsilver

base_save: checkpoints/pre-lugia.sav

position:
  map: WHIRL_ISLANDS_EVENT_AREA
  spawn: VERIFIED_PRE_EVENT_TILE

progression:
  preset: lugia_prerequisites_met
  encounter_completed: false

party:
  preset: story_test_party

inventory:
  preset: lugia_event_items

test:
  expect:
    - area_loads
    - scripted_event_starts
    - battle_transition_completes
    - overworld_restores_after_battle

```

The symbolic map, preset, and spawn names are illustrative; the actual values should be derived from the decompilation and verified save fixtures.

### Build the progression model from game scripts

This is the main remaining research task.

For every significant story event, identify the flags and variables it reads, the ones it writes, the required items, the relevant map scripts, and any other state it assumes.

The [pokeheartgold decompilation](https://github.com/pret/pokeheartgold?utm_source=chatgpt.com) is the strongest starting point for this.

For the Whirl Islands crash, for example, the correct fixture is not merely one with the player teleported to Lugia's chamber. It is a save immediately before the relevant event, with all the conditions necessary for the game to follow its intended script path.

That difference will be essential when distinguishing Kohjoh rendering bugs, script bugs, and malformed-save bugs.

### Validation strategy

I would require each generated fixture to pass three checks: structural validation against the HGSS format, semantic validation against the intended story-state preset, and runtime validation in the game.

For structural validation, use PKHeX as an independent reference implementation. For semantic validation, compare the generated state against decompilation-defined script conditions. For runtime validation, confirm that loading and saving again does not silently undo the changes.

The strongest automated test is a round trip:

`valid original → modified save → game load → game save → parse again`

If the final state differs unexpectedly, the generator may have missed some bookkeeping or dependent field.

## 14. Source hierarchy and limitations

The principal references for implementing this format are:

| Source                                                                                                                                         | What it provides                              |
| ---------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| [PKHeX SAV4HGSS](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4HGSS.cs?utm_source=chatgpt.com)                               | HGSS-specific offsets and behavior            |
| [PKHeX SAV4](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/SAV4.cs?utm_source=chatgpt.com)                                       | Shared Generation IV serialization            |
| [Block detection](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen4/SAV4BlockDetection.cs?utm_source=chatgpt.com) | Active-block selection                        |
| [pokeheartgold](https://github.com/pret/pokeheartgold?utm_source=chatgpt.com)                                                                  | Original game structures and script semantics |
| [Generation IV save documentation](https://bulbapedia.bulbagarden.net/wiki/Save_data_structure_\(Generation_IV\)?utm_source=chatgpt.com)       | Additional format descriptions                |

PKHeX is particularly valuable because it is a working serializer and editor. The decompilation is particularly valuable because it explains how the game actually consumes the data.

Neither source alone gives us a complete, verified mapping from every possible story event to a safe, independently constructible save state.

## Conclusion

The mechanical part of HGSS save editing is reasonably well understood: block selection, checksums, trainer data, Pokémon, inventory, Pokédex, and the principal persistent script regions.

The difficult part is semantic consistency: ensuring that a generated save describes a world state the game's scripts actually support.

For Kohjoh, the most useful next step is to construct a machine-readable database mapping every major Johto and Kanto progression event to its script prerequisites and saved-state effects. With that in place, `SAV4HGSS` becomes a practical foundation for an automated regression corpus, rather than merely a way to edit individual fields.
