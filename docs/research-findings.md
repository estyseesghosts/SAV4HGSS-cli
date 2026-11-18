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
- Ecruteak east exit pinned (SOURCE + RUNTIME, legs 42-51):
  `075_T27.json` lists `MAP_ROUTE_42_ECRUTEAK_GATEHOUSE` at Ecruteak
  (415,175) and (415,176) — the ONLY east exit. The y=184 road is a
  dead end (x=413→414 tree wall, NEGATIVE). Played path from outdoor
  anchor `lake-leg14/trek14.mcpr` (397,188): west to (384,184,
  trek42), north blocked there, further west then north to (380,176,
  trek44 — the open y=176 corridor), east to (395,176, trek45),
  south-dip around the Mart to (403,176, trek46) → (411,177, trek47)
  → (414,174, trek48), step onto (415,175/176) → gatehouse interior
  (trek50 screenshot), east across to R42 (422,175, trek51, Route 42
  banner). RAM: `0x227d498` {warp 0, (422,175)} — R42 outdoor uses
  warp 0, NOT the Ecruteak `FFFFFFFF` sentinel (MAP-02 evidence:
  warp id varies by map).
- R42 east push (RUNTIME, legs 52-56): (422,175) → (428,173) →
  (440,172) at the water/cave mouth; cave is `MAP_MOUNT_MORTAR_1F_ENTRANCE`
  (`041_R42.json`: entrances at (436,171)/(464,175)/(466,164)/
  (493,171); we used (436,171)). Step from (437,172) west+ north
  into the mouth → Mt Mortar banner (trek56). Interior warps
  (`116_D38R0101.json`): 4 R42 doors + Back/2F/B1F shafts — through-
  route still open, R43/Lake after.

## Move-teach synthesis, first approved case (Surf, Nov 2026)

- Problem: Mt. Mortar 1F Entrance at (20,44) is water-barred east
  (`lake-leg57`: (20,46)→(20,44), east walled; R42 lakes likewise
  need Surf). Party Mantine (slot 5) knew [61,29,97,17], no Surf
  (57); bag holds HM03.
- Method (INTERVENTION + RUNTIME): `party export` slot 5 → set
  Move3 97→57, Move3_PP→15 via PKHeX.Core 26.10.18 (`RefreshChecksum`;
  throwaway helper, not committed) → `party import --expect-species
  226` → `leveld-out/trek-saves/hg-6gyn-surf.sav`
  (`13279a5a…`). Byte diff: 130 requested-data (party slot blob) +
  2 checksum, 0 unclassified, checksums valid. `inspect`: Mantine
  moves [61,29,57,17], location untouched (78/394/184/z3).
  Headless 700-frame boot: `--assert-spawn 78,394,184,3` matches
  with 2 agreeing copies (`0x227d498` + `0x2377a20`).
- Why this is safe to approve: HM teach is a legal play action
  (Mantine learns Surf, HM03 in bag, Fog badge owned at 6gyn so
  field use is unlocked); the edit reproduces exactly what the
  in-game teach would write, with no flag/var/bag side effects.
  Pattern for future synthesis: export → minimal PKHeX-Core patch
  → import with `--expect-species` → inspect/diff/boot-verify.

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

## Lake pre-Gyarados trek continuation (RUNTIME, 2026-11-17, no fixture staged)

- From `hg-6gyn-surf.sav` (HG 6gyn base + approved Surf teach on Mantine), play reached Route 43 `(524,149)` (map 45) with Spencer dialogue mid-battle when the session ended on external `provider.quota`; no in-game save was taken and no `hg-lake-pre-gyarados` fixture/manifest exists.
- Resume anchors: `C:\Users\julie\workdir\leveld-out\surf-fast93..100\states\*.mcpr`, `C:\Users\julie\workdir\leveld-out\surf-battle1\states\sp_a1..sp_a8.mcpr` on `hg-6gyn-surf.sav`. `hg-lake-pre-gyarados` not staged.
- Gate remains as above (SOURCE/SAVE): 6gyn pre, 7gyn post; next concrete step is finish/bypass Spencer, reach Lake of Rage, in-game save + `--extract-save`, `inspect`/`diff`, then stage.

## Mt. Silver / Red unlock (STORY-08)

**Answer** (SOURCE, `pokeheartgold@4e95633`): it is a five-write chain, not one
flag. Badge gating is transitive through Blue, and the scripts at Route 28 and
the summit carry no badge or story check of their own.

1. Beating Blue in the Viridian Gym sets `VAR_UNK_4096 = 1`
   (`files/fielddata/script/scr_seq/scr_seq_0743_T02GYM0101.s:65`).
2. Viridian City's header runs `scr_seq_T02_011` while that variable is 1
   (`scr_seq_0514_T02_hdr.s:13`). That script calls Oak and sets
   `VAR_UNK_4096 = 2` (`scr_seq_0741_T02.s:48-51`); the same script sets
   `FLAG_UNLOCKED_WEST_KANTO` and `VAR_UNK_4121 = 1` (`:42-44`).
3. Talking to Oak with `VAR_UNK_4096 == 2` branches to `_0282`
   (`scr_seq_0740_T01R0301.s:95-96`), which gives `ITEM_HM08` and sets
   `VAR_UNK_4110 = 2`, `FLAG_UNLOCKED_MT_SILVER`, `FLAG_GOT_HM08`
   (`:198-208`). The `FLAG_GOT_HM08` branch at `:198` is why the state has to
   include the flag and the item together.
4. Route 22 holds the physical gate. Each policeman steps aside only for his
   flag (`scr_seq_0213_R22R0101.s:19-23`) and the west guard's coord script
   (14,8) runs while `VAR_UNK_4121 == 0`, the north gate's (8,8) while
   `VAR_UNK_4110 == 1` (`files/fielddata/eventdata/zone_event/270_R22R0101.json`,
   `coords`). `VAR_UNK_4121` has no other reader in the script corpus, so it is
   the west-guard switch, not a scene variable.
5. Red is placed by the summit's zone event with hide flag `FLAG_UNK_25F`
   (`419_D41R0108.json`, `SPRITE_RED` at 30,11). His script has no condition
   between the message and the battle (`scr_seq_0107_D41R0108.s:13-19`);
   winning sets `FLAG_UNK_25F`, `VAR_UNK_40FD = 1`, awards `RIBBON_LEGEND` and
   starts the credits (`:20-40`).

What this changed in the tool: `event-mt-silver` and `legendary-red` used to
write `FLAG_UNK_4096`, i.e. only `FLAG_UNLOCKED_MT_SILVER`. That state cannot
occur in play: `FLAG_UNLOCKED_WEST_KANTO` stays false, both Route 22 guards
still fire, the bag has no HM08, and Oak would not even speak the unlock
dialogue. Both scenes now write the whole chain
(`tools/make_scenes.py`, `MT_SILVER_UNLOCK_EXTRAS`) and audit the flags,
variables and item.

The corpus cannot show the chain: `research/corpus-census.json` has Kanto badge
counts 0 and 255 only. `Pokemon_SoulSilver16gyns.sav` confirms the end state
(`FLAG_UNLOCKED_MT_SILVER`, `FLAG_GOT_HM08`, `FLAG_UNK_25F`, `VAR_UNK_4110 = 2`,
`VAR_UNK_4096 = 0`), so the intermediate values come from the source, not from a
save diff.
