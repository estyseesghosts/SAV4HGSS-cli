# Research findings log (answers to `hgss_save_state_research_plan.md`)

Each entry names the plan question, the evidence level from the
plan's §2 hierarchy (SOURCE / SAVE_DIFF / INTERVENTION / RUNTIME /
NEGATIVE), and the exact artifact. Nothing here is inferred without
a cited observation.

## Lake of Rage / Red Gyarados gate (STORY, SYM-02/SYM-03)

**Gate sequence** (SOURCE, `pokeheartgold@4e95633`
`files/fielddata/script/scr_seq/scr_seq_0938_T29.s`):
- `scr_seq_T29_000` (map entry): if `FLAG_GOT_RED_SCALE` is clear,
  set map-temp/UNK bits and end (Gyarados present by default).
- `scr_seq_T29_001` (talk to Gyarados): cry + msg, set
  `FLAG_ENGAGING_STATIC_POKEMON`, `WildBattle SPECIES_GYARADOS, 30`;
  on win: if static-outcome was caught (4), set
  `FLAG_CAUGHT_RED_GYARADOS` (`0x16A`); if `FLAG_GOT_RED_SCALE`
  clear, give `ITEM_RED_SCALE` + set `FLAG_GOT_RED_SCALE`, clear
  `FLAG_HIDE_LAKE_OF_RAGE_ACE_TRAINER_LOIS`.
- `scr_seq_T29_002`: on `FLAG_ENGAGING_STATIC_POKEMON`, set
  `FLAG_HIDE_LAKE_OF_RAGE_RED_GYARADOS` (`0x1E3`), hide Gyarados,
  clear `FLAG_HIDE_LAKE_OF_RAGE_LANCE` (`0x1E4`), show Lance.

**Pre-state pins verified live** (SAVE + RUNTIME, CLI `flag get` on
real saves):
- HG 6gyn raw (`64287599…`): `FLAG_GOT_RED_SCALE` false,
  `FLAG_HIDE_LAKE_OF_RAGE_RED_GYARADOS` false,
  `FLAG_HIDE_LAKE_OF_RAGE_LANCE` true → pre-Gyarados.
- HG 7gyn raw (`ffc51c7b…`): `FLAG_GOT_RED_SCALE` true → post.
- So a Lake pre-Gyarados fixture = 6gyn story state + lake
  coordinates; no flag edits needed (COH-01 pattern, same as the
  Olivine fixture).

**Corpus gap** (SAVE_DIFF-adjacent census): scripted scan of all 71
`0x80000`-byte saves under `3DS/hgss-saves` (built CLI DLL, one
`inspect` per file) shows map ids 67/73/74/76/78/87/89/113/117/51/
59/172/190/288/300/306 — none at `MAP_LAKE_OF_RAGE` (88). The play
base must therefore walk in from HG 6gyn Ecruteak (394,184); no
teleport shortcut exists. (Full table in session context 2026-11-17.)

## Interior Location structs (MAP-02, MAP-04)

**Finding** (RUNTIME + RAM scan): the overworld Location pattern
(map u16 + `FFFFFFFF` warp + x/y/dir) tracks the player outdoors
(Ecruteak 394,184 → 413,184 across legs), but walking north into the
Ecruteak Pokécenter door (~397,182) removes the player struct
entirely: full-pattern scans of map 78 find only two stale
outdoor structs, any-warp map-78 scans show 100+ false positives,
and map-86 scans find nothing. One adjacent array entry
(`0x227d4ac`) reads {map 78, warp 5, (397,183)} — a non-`FFFFFFFF`
warp id, consistent with interiors using entrance-warp ids rather
than the overworld sentinel. Implication (MAP-02): `location.set`
equivalence is proven only for outdoor maps; interiors need their
own pattern work before any indoor spawn is staged. (States:
`leveld-out/lake-leg15..36`, `scanmap2/3.py` in temp.)

## Bag menu input timing (tooling note)

**Finding** (RUNTIME): in the HGSS bag item list, 2-frame d-pad
holds never move the cursor (4× DOWN silently ignored), while 6–8
frame holds step reliably; touchscreen taps are exact (Max Repel
slot tapped at 67,141 → USE → "Master used the Max Repel",
990→989). Menus differ per UI (start menu single-steps on 2-frame
holds, bag needs longer) — always verify cursor movement on
screenshots, never assume. (States `leveld-out/lake-leg0d`
vs `lake-leg22..26`, `lake-leg24` frame_000440.)

## Ecruteak east-road recon (in progress)

- East road at y=184 runs 394→413, then a solid tree wall at
  x=414 spanning y∈[181,188] (RAM coords per leg + screenshots).
- North from (397,188) enters the Pokécenter (door ~(397,182));
  the center block plus garden walls box x∈[403,405] at y~186-188.
- Pokécenter interior exit hunt: red doormat located bottom-left
  of the lobby (enlarged screenshot `lake-leg28/top_big.png`,
  logo/counter/Nurse layout confirms building), but ~15
  step-on attempts from all sides never fired the warp; FLY-out
  and Escape Rope (correctly refused: "time and place", outdoors
  only) also fail. Exit still open — next: exact mat-tile sweep
  or reload outdoor anchor `lake-leg14/trek14.mcpr` (397,188).
- Walkthrough cross-check (websearch): Ecruteak east exit → R42
  gate; R42's through-path needs Surf (two lakes) or Mt. Mortar;
  Suicune + Hiker-Strength script events sit mid-route; R43 to
  the lake. Party (Quilava 34/Pichu 33/Machoke 30/Kadabra 28,
  Mantine Surf-capable, Spearow Fly) + 989 Max Repels staged.

## COH-01 minimum-edit-set evidence (Olivine, staged this session)

- HG 5gyn Cianwood → Olivine pre-Jasmine required ONLY
  `location.set` (story flags identical between base and played
  arrival). Build: 16 bytes changed, 0 unclassified, checksums
  valid. Boot: 2 agreeing RAM copies at (272,258). Runtime
  recorded. (`hg-olivine-pre-jasmine`, commit `ad37954`.)
- Played-arrival diff vs staged showed only time, partition flip,
  transient `FLAG_TEMP_x4000`, and per-map
  `FLAG_HIDE_CAMERON` (set by Olivine's map script — transient
  actor visibility, correctly not asserted).
