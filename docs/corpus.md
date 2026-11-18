# The save corpus, presets, and coverage

All research findings rest on a small set of real saves. This file says what they are, what each covers, and what is missing. Raw saves are never committed and never modified; `tools/corpus_census.py` reads them through the CLI and writes `research/corpus-census.json`. The saves live in `3DS/hgss-saves` on the author's machine; each is identified in the census by SHA-256.

## What is in the folder

`hgss-saves` holds 65 entries (files and subfolders). Among them:

- 42 are `0x80000` bytes. 29 are unique US HG/SS saves; the rest are duplicate content or other games (12 rejected with exit 3: 9 report game version 88, 3 report version 0; they are Pearl, Platinum and White saves).
- The remainder are `.dsv` copies (DeSmuME) and NO$GBA `.SAV` originals. 13 NO$GBA files were decoded to raw images under `raw-from-noexotic/`; the `.SAV` originals stay canonical.
- File names can lie. `Pokémon HeartGold.sav` is a SoulSilver save. Always use the identity the CLI reports.

The earlier figure "71 saves" counted every file, including copies and other games.

## The 29 unique saves

| Group | Game | Saves | Covers |
|---|---|---|---|
| Master lineage | HG | 11 | one playthrough, TID 7142: 1, 2, 3, 4, 5, 6, 7 and 8 badges, League entrance, Hall of Fame, Kanto (4 badges). Ethan, Cyndaquil |
| M@ster lineage | SS | 10 | TID 59274: 1, 2, 4, 7 (several, including three at the Radio Tower deck), 8 badges, a postgame save at New Bark, 52 h 45 min at the Route 38 gatehouse with 8 Johto and 8 Kanto badges. Ethan, Cyndaquil |
| Lyra event saves | HG | 5 | Celebi (Ilex Forest), Pichu (Ilex Forest), Arceus (Ruins of Alph), Enigma Stone (Pewter), Rotom (Saffron). Distribution saves, Totodile |
| Ash | HG | 1 | new game at Cherrygrove, 0 badges |
| NotKris | SS | 1 | new game at Cherrygrove, 0 badges, Lyra-type hero |
| Desmond | HG | 1 | Dragon's Den shrine, 8 badges |

An earlier note said the HG Master lineage has 18 saves; the census has 11 unique ones (the 18 counted duplicate files). All Master and M@ster saves carry bag quantities near 990 and 14 TMs, so they are not clean item samples. Flags and variables are unaffected.

Limits that matter for interpretation:

- One save per milestone per lineage. Anything inferred from one pair is a hypothesis.
- Only one HG playthrough. Choices that varied between playthroughs cannot be separated from story (`docs/archive/state-log.md` open item 3).
- Kanto badge counts in the corpus are 0 and 255 on SS and 0 and 60 on HG. Individual Kanto badges and Blue/Red pre-states do not exist as real saves.
- No save sits at Lake of Rage, in a gym-puzzle interior, or mid-cutscene. Mid-cutscene saves cannot exist; savestates cover those.

## Axes covered (from `docs/archive/coverage-matrix.md`)

| Axis | State |
|---|---|
| Game and hero | HG and SS; Ethan-type (gender 0) and Lyra-type (gender 1) |
| Starter | Cyndaquil (155) and Totodile (158). No Chikorita (152) save; starter cannot be edited in because the rival's team follows it. Needs a played intro |
| 16 gyms before and after | Johto done on both lineages; Kanto composed on a Champion base (see scenes) |
| Elite Four, Blue, Red | E4 both sides present; pre-Blue and pre-Red states do not exist as saves; composed through scenes |
| Legendaries | Pre-state fixtures for Lugia, Ho-Oh, Kyogre, Celebi, Arceus; Mewtwo and Groudon pre-states composed by scene |

Fill methods, cheapest first: **pin** (assert observed values on a base already at the point), **grant** (give a key item whose only script touchpoint is the gate), **play** (drive the emulator, save in-game, extract), **synth** (edit what play cannot reach; one case approved, the Surf teach).

## Presets: whole played milestones

`registry/presets/` holds 18 full-state presets built by `tools/make_presets.py`. A preset overwrites every persistent flag (true for those set in the source save, false for the rest) and every writable variable, plus badges, the Key Items and TM/HM pockets, and a spawn warp at a Pokémon Center door. It leaves alone the daily flags, map-temp flags, flag `0x966` (owned by `location warp`), `VAR_PLAYER_STARTER` and a few per-session counters. A preset is absolute, so the result does not depend on the input save's progress.

| Preset | Game | Johto mask | Kanto mask | Source map | Flags set |
|---|---|---|---|---|---|
| `hg-00-cherrygrove` | HG | 0 | 0 | Cherrygrove | 165 |
| `hg-01-zephyr` | HG | 1 | 0 | Violet | 172 |
| `hg-02-hive` | HG | 3 | 0 | Azalea | 199 |
| `hg-03-plain` | HG | 7 | 0 | Goldenrod | 230 |
| `hg-04-fog` | HG | 15 | 0 | Ecruteak | 268 |
| `hg-05-storm` | HG | 31 | 0 | Cianwood | 300 |
| `hg-06-mineral` | HG | 63 | 0 | Ecruteak (takeover phase 1) | 305 |
| `hg-07-glacier` | HG | 127 | 0 | Mahogany | 367 |
| `hg-08-rising` | HG | 255 | 0 | Blackthorn | 427 |
| `hg-e4-gate` | HG | 255 | 0 | League entrance | 446 |
| `hg-champion` | HG | 255 | 0 | Hall of Fame | 450 |
| `hg-kanto-4` | HG | 255 | 60 | Pewter | 515 |
| `ss-01-badges` | SS | 1 | 0 | Violet | 192 |
| `ss-02-badges` | SS | 3 | 0 | Azalea | 215 |
| `ss-04-badges` | SS | 15 | 0 | Ecruteak | 284 |
| `ss-07-badges` | SS | 127 | 0 | Mahogany | 373 |
| `ss-08-badges` | SS | 255 | 0 | Blackthorn | 438 |
| `ss-kanto-8` | SS | 255 | 255 | Route 38 gatehouse | 777 |

`hg-00-cherrygrove` carries a Pokédex of 493 from its donor save, not from play. Each preset applied to a clean base boots through Continue and puts the live Location on the preset's warp tile (17 of 17 at the time of F9; `hg-00` was added later).

## Scenes: a named story point

`registry/hgss-us.scenes.json` has 80 scenes: 32 gym (before and after for each of 16 gyms), 7 Elite Four and champion, 13 legendary and Red, 28 events (including the rival and executive battles). `story scene apply ID INPUT --out OUT` composes: base preset, then badge set with consequences, then the scene's own flag, variable and item writes, then a warp. It audits the result (badges, saved warp, every expected flag/variable/item, the badge rules) and exits 6 if anything fails.

- "Before gym N" is the previous milestone plus a clear of that leader's own hide flag, never a subtracted badge. Backward resets are refused.
- Kanto gyms 9-16 are composed on `hg-champion` plus Kanto badge bits, because the corpus has no per-badge Kanto saves. The order (Boulder to Earth) is canonical, not enforced by the game.
- Scenes are version-locked where the game is: Groudon and Latias build on `ss-kanto-8` and need an SS input; Kyogre, Latios and the rest use HG.
- 78 scenes are HG, 2 are SS. All 80 reach their tile; per-scene results are in `docs/scene-boot-matrix.md`.

## Fixtures

`fixtures/recipes/` holds 30 recipes that stage curated states from played bases (`fixture build` / `verify` / `record-runtime`). `fixtures/safe-spawns.json` lists 20 map/x/y tiles taken from real saves; `location set` accepts only those. `fixtures/event-prereqs.json` records, for 13 story beats, the script reads and writes with file and line and the values observed in saves. Fixtures predate presets and scenes; scenes are the more general mechanism and fixtures remain the evidence trail for individual beats.

## Gaps and how to fill them

| Missing | Why | How |
|---|---|---|
| Chikorita start | Starter shapes the rival's team | Play the intro headless, save at Cherrygrove |
| Second HG playthrough | Separates story from choice (F1, open item 3) | Dump a second run at the same milestones |
| Lake of Rage pre-Gyarados | No base; trek stopped at Route 43 | Finish the trek from `hg-6gyn-surf.sav` (anchors are local, in `leveld-out/`) |
| Pre-Blue, pre-Red real saves | Corpus has Kanto badges 0 and 255 only | Play, or rely on composed scenes |
| Gym-puzzle interiors | No nearby bases | Played saves |
| Mid-cutscene points | Not representable in `.sav` | `.mcpr` savestates |

Deliberately skipped as duplicates: HG 7 badges (same point as SS 7), HG post-Elite-Four, HG Celebi postgame, HG 8 badges.
