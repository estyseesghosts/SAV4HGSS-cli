# Coverage gaps and what unblocks them

## Spawns are allowlisted, not arbitrary

`location.set` only writes exact `fixtures/safe-spawns.json` entries
(12 verified tiles). This is deliberate: story validity needs actors,
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

## NO$GBA `.SAV` gym-era files are not decodable here

The `Pokemon_HeartGold1-8gyn.SAV`, `Pokemon_SoulSilver4gyn.SAV`,
`Celebi/E4/posE4` files (NO$GBA `NocashGbaBackupMedia` container,
~159KB) do not contain PKHeX-valid general/storage blocks at any file
offset: a CRC16-CCITT scan (implementation verified against the
`PokemonSS1gyn.sav` stored checksums) finds zero valid blocks, and the
magic-adjacent size fields (0xF628/0x12310) imply block starts at
negative file offsets, so no contiguous-block placement fits. They are
stale/partial dumps, not usable bases. What unblocks the Whitney /
Ecruteak / Chuck / Jasmine-era coverage they would give: re-dump those
saves as raw `0x80000` (the format every working save uses) and the
existing fixture pattern applies directly.

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
