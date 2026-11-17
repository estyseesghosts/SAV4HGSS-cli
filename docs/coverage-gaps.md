# Coverage gaps and what unblocks them

## Spawns are allowlisted, not arbitrary

`location.set` only writes exact `fixtures/safe-spawns.json` entries
(15 verified tiles). This is deliberate: story validity needs actors,
warps, and script context the save cannot encode, so teleports stay
refused (exit 8 `location-unverified`).

A proposed "tile-walkability check" was investigated and rejected for
now. Movement permissions live in compiled binaries
(`pokeheartgold/files/fielddata/mapmatrix/map_matrix_*.bin`,
`maptable/mapname.bin`) with no decompiled per-tile source in this
checkout; `include/constants/metatile_behavior.h` defines only behavior
flag bits, not the per-map grids. Shipping a walkability verdict from
undecoded binaries would be a guess, not a check. The real path is:
decode the movement grids, then validate every verdict against known
player tiles from real saves (including all 10 registered spawns)
before any patch op trusts it.

## NO$GBA `.SAV` gym-era files: decoded via ndssc (was blocked)

`3DS/ndssc` (No$GBA NDS Save Converter, after the DeSmuME wiki
No-gba-save-format doc) decodes the container: 32-byte magic, an
`SRAM` chunk whose `u32` at `0x44` is a compressed flag, packed size at
`0x48`, and unpacked size at `0x4C`. Our files are RLE-compressed
(`0x44=1`, packed `0x26CDA`, unpacked `0x80000`) — which is why raw
footer bytes were visible but no PKHeX-valid block existed at any
offset. All 13 files convert to byte-exact `0x80000` images with
structural pass and valid checksums; raws live in
`hgss-saves/raw-from-noexotic/` (`.SAV` originals retained as
canonical). This unlocked the full HG Master gym lineage (Violet,
Azalea, Goldenrod, Ecruteak ×2, Cianwood, Mahogany, Blackthorn),
Celebi/E4/posE4 files, and the M@ster SS 4gyn Ecruteak base. Still
missing: saves near Olivine pre-Jasmine, Lake pre-Gyarados, and
gym-puzzle interiors.

## Beats with no nearby real base

No raw save in `hgss-saves` stands near: pre-dance Ecruteak, Olivine
pre/post-Jasmine (covered only by TM-flag asserts on the Mahogany
base), Lake of Rage pre-Gyarados (the lineage's TM10 flag stays false;
only the postgame base carries it), gym-puzzle interiors, or any
mid-cutscene point. Backward flag-resets ("un-beat Clair to restage
Ecruteak") are refused: they fabricate story states the scripts never
produce from that base.

What unblocks these: legitimately played saves at those points
(boot the retail carts, play to the event door, dump the raw
`0x80000` save). Each new base yields a diffable pair, a registered
spawn, and a fixture following the existing recipe pattern.
