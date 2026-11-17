# HGSS story state: what the save holds, how scripts read it, and what broke along the way

This is the field-knowledge companion to `SAV4HGSS.md` (PKHeX-level
format walkthrough) and `hgss_save_cli_engineering_design.md` (CLI
architecture). It records how HeartGold/SoulSilver actually persist
story and script progress, how the game's scripts consume that state,
and every significant problem hit while building SAV4HGSS-cli with the
fix that resolved it. Nothing here is guessed: every claim cites a
source file, a live save read, or an emulator observation.

## 1. Where story lives in the save

A raw save is `0x80000` bytes: two `0x40000` partitions, each with a
general block (`0xF628`, `SAV4HGSS.cs:34`) and a storage block
(`0x12310`, `SAV4HGSS.cs:35`), each closed by a `0x10`-byte footer
(`SAV4HGSS.cs:38`). The footer holds a save counter, the recorded
block size at -0x0C, a magic at -0x08 (`0x20060623` JP/INTL,
`0x20070903` Korean, `SAV4.cs:117-119`), and a CRC16-CCITT. Active vs
backup copy selection uses major/minor revision counters
(`SAV4.cs:180-184`, `SAV4BlockDetection.cs:19-33`) — copies can and do
live in opposite partitions, so tools must select per block, never
assume partition A.

Inside the general block, the story-relevant regions are:

- Trainer block at +0x64 (`SAV4HGSS.cs:70`): name trash bytes,
  TID/SID, money, badges, play time.
- Player position M/X/Y at `0x1234/0x123C/0x1240`
  (`SAV4HGSS.cs:186-188`), with X/Y mirrored into X2/Y2 at
  `0x236E/0x2372`, and Z at `0x2376` (`SAV4HGSS.cs:200`).
- ~2912 persistent event flags and ~368 save-work variables
  (capacities read live from `inspect`; temp/map-temp/special
  ranges are classified and rejected as writes).
- Per-actor hide flags (`FLAG_HIDE_*`), roamer state vars
  (`VAR_ROAMER_*`, `SAV4HGSS.cs:350-352`), scene vars
  (`VAR_SCENE_*`), and the dex via `Zukan4` (`SetSeen`/`SetCaught`).

## 2. How scripts consume that state

`registry/hgss-us.script-refs.json` (9960 refs, 965 symbols, pinned to
pokeheartgold `4e95633`) indexes every flag/var/item read or write in
`files/fielddata/script/scr_seq/*.s`. The recurring gate shapes:

- **Gym ladder via TM-gift flags.** Each Johto gym sets a TM flag
  (Falkner 115 … Clair 218). Our per-gym fixtures assert the observed
  bit pattern live on each base: Vi `10000000`, Az `11000000`,
  Goldenrod `1110`, Ecruteak-4gyn `1111000`, Cianwood `1111100`,
  Ecruteak-6gyn `1111110`, Mahogany `11111110`, Blackthorn `11111111`.
- **Legendary pre-states via caught + hide + scene.** Lugia:
  `FLAG_CAUGHT_LUGIA` false, Silver Wing held, gate at
  `scr_seq_0884_T24R0801.s:46`. Celebi: takeover beaten,
  `VAR_UNK_40FE<4`, fateful Celebi leading, gate at
  `scr_seq_0092_D36R0101.s:1746-1760`, time-travel path `_1F48`
  (line 2348) warping to Route 22. Arceus: fateful Arceus lead, no
  bike, gate at `scr_seq_0037_D24R0101.s:192-212`, Sinjoh path in
  `scr_seq_0131_D51R0201.s`. Kyogre: scene<4, SS version, unbeaten,
  Blue Orb held, gate at `scr_seq_0134_D52R0102.s:12-48`.
- **Event movies via hide-flag pairs.** Burned Tower dogs:
  `scr_seq_0024_D18R0102.s:22-89` sets the three B1F hides (70-72),
  creates roamers (76-77), shows Eusine (79-81). Rotom room init hides
  all appliances then unhides owned forms
  (`scr_seq_0838_T11R0702.s:18-53`); interaction needs
  `PlayerHasSpecies ROTOM` (92-94).
- **Chapter brackets.** E4 pre-challenge (all four
  `FLAG_DEFEATED_*` clear, Mt Silver locked, League entrance) vs
  fresh Champion (flags set) pin both sides of the E4 gate from two
  real bases.

Key-item grants (Silver Wing, Blue Orb) follow one rule: the item's
*only* script touchpoint may be the gate's `HasItem` check
(verified via `script references` for both items — no give-flags, no
takes on the paths we stage). Otherwise a grant would fabricate state
the scripts never produce, and it is refused.

## 3. The live RAM Location struct

The game does not keep the raw general block verbatim in ARM9 RAM —
a trash-byte/ASCII/UTF-16 scan for the trainer name finds zero hits,
and the packed M/X/Y/Z save pattern never appears either. Differencing
two overworld RAM dumps (Violet 73,459,246 vs Kanto gatehouse
172,6,7) isolated the live decomp `Location` struct
(`field_types_def.h:10-16`: `mapId, warpId, x, y, direction`):
u16 map @+0, u32 `0xFFFFFFFF` warp @+4, u16 x @+8, u16 y @+12, u16
direction @+16, with zero padding (int upper halves). Wanted map/x/y
come from the staged save's own M/X/Y (`SAV4HGSS.cs:186-188`).
`--assert-spawn` scans `0x02000000-0x02400000` for that pattern.

Proven live, not stale: holding Up for 60 frames from the Violet
overworld state moved Y 246→244 in the primary struct at `0x227d498`,
map/X unchanged; holding Down for 30 frames in a later run moved
246→248 in the same struct. The native input path was verified
round-trip (`keypad_get` echoes the held mask, `KEYINPUT` at
`0x04000130` reads `0x37F` with bit 7 cleared for Down, persisting
across frames). The HGSS bottom screen permanently shows the menu
buttons during overworld play, so menu-looking screenshots are normal
overworld UI, not a paused menu — an earlier "cursor doesn't move"
reading mistook that static UI for a menu state.

Two corrections from Nov 2026 d-pad testing:

- The per-map mirror goes stale: after the Down×30 walk the primary
  at `0x227d498` read (459,248) while the copy at `0x239aa40` still
  read (459,246). The old assert keyed on the full map/x/y pattern
  with any-hit matching, so it kept passing on the stale copy.
  `--assert-spawn` now requires at least two agreeing full-pattern
  copies and lists every same-map struct with coordinates.
- Same-map structs are not all the player: a Violet fresh boot shows
  NPC structs (470,260) at `0x2377a20` and (459,255) at `0x2377a70`;
  a Saffron fresh boot shows the player triplicated
  (`0x227d498/0x2377a20/0x239aa40` at (1308,231)) plus an NPC
  triplicated nearby. Map-only unanimity would false-fail on NPCs.
- The u16 at +16 did not track facing here: it read 3 (`DIR_EAST`)
  before and after the south walk. Report it as observed; do not
  read it as avatar facing, and do not gate on it. (One later
  end-of-run read showed 1/`DIR_SOUTH` after the same south walk, so
  the field may lag rather than lie — either way, never gate on it.)

Save Z is *not* live direction and is recorded, never asserted: real
saves carry Z=4 (Mahogany) and Z=7 (Blackthorn) while decomp
directions are 0..3 (`global_fieldmap.h:4-9` `DIR_MAX 4`). The first
`--assert-spawn` gated the full 20-byte struct including Z and falsely
failed Blackthorn (save Z 7 vs live direction 1); Violet and Kanto
only passed by coincidence (Z equaled direction there).

## 4. Containers: what else the saves come in

- **DeSmuME `.dsv`**: raw `0x80000` plus a 122-byte (`0x7A`) footer
  ending `|-DESMUME SAVE-|`. Unwrap with `container unwrap` (never by
  hand-strip in a script; the CLI verifies size and hash). This is how
  the five Lyra event-distribution bases entered the tree.
- **NO$GBA `.SAV`**: `NocashGbaBackupMedia` container holding
  RLE-compressed data (`u32` at `0x44` = 1 means compressed, packed
  size at `0x48`, unpacked size at `0x4C`). Decoded with
  `3DS/ndssc` (public-domain, after the DeSmuME wiki format doc):
  our files declare packed `0x26CDA`, unpacked `0x80000`. All 13
  converted to byte-exact raws with structural pass and valid
  checksums; raws live in `hgss-saves/raw-from-noexotic/`, `.SAV`
  originals retained as canonical, recipe bases point at the raws.

## 5. Issue log

1. **RAM mirror miss.** Trainer-name-anchored scan of ARM9 RAM found
   nothing (see §3). Fixed by differential dump analysis; footer
   magic/size offsets that guided the first attempt were verified
   against real-save bytes (`u32[1]=0xF628`, `u32[2]=0x20060623`) so
   the failure was localized to the anchor hypothesis, not the
   offsets.
2. **Save-Z vs direction conflation.** Full-struct match failed
   Blackthorn. Fixed by checking `DIR_MAX` and observed Z=4/7 values;
   assert map/x/y, report direction and save Z separately.
3. **NO$GBA looked undecodable.** Magic-adjacent sizes were real
   (0xF628/0x12310) but a CRC-validated scan over every offset found
   zero blocks and the sizes implied negative-start geometry —
   consistent with compressed payload, confirmed by `ndssc.c`
   (RLE opcodes at `0x50+`, size fields at `0x44/0x48/0x4C`).
4. **63-char SHA.** A hand-transcribed base hash failed
   `fixture-base-sha` (must be 64 hex). Fixed by hashing the file and
   pasting the digest; SHAs are now always computed, never typed.
5. **`peek_block` 4096-byte cap.** RAM-dump helpers must chunk at
   `0x1000`; larger reads raise. The driver scans in 0x1000 windows
   with overlap carry for cross-boundary patterns.
6. **Footer field mis-split.** During manual container analysis the
   counter/size/magic words were misaligned by one u32, producing a
   nonsense counter. Fixed by re-deriving from the known-good raw
   footer layout before concluding anything.
7. **Shell quoting.** PowerShell `\"` escapes inside
   double-quoted tool args produced literal backslashes in paths;
   use `Join-Path` or single quotes. Environment-only, noted so the
   next person doesn't re-learn it.
8. **Stray-comma recipe edits.** A draft artifact (lone `,` line)
   broke recipe JSON twice. Fixed by validating with a JSON parse
   immediately after writing, before building.
9. **Spawn-assert false-pass on stale mirror + static-menu misread.**
   D-pad was suspected broken because menu-looking screenshots never
   changed and `--assert-spawn` kept passing after held inputs. Both
   premises were wrong: the HGSS bottom screen always shows the menu
   buttons in overworld play (static UI, not a paused menu), and a
   RAM round-trip proved Down×30 moves the primary struct 246→248
   while its mirror stays behind. Fixed by requiring ≥2 agreeing
   full-pattern copies and listing every same-map struct; verified
   with a fresh-boot pass (2 copies) and a post-move loud fail
   (primary moved, mirror stale, matched=1).

## 6. Deliberate refusals (not bugs)

- **Backward flag-resets** ("un-beat Clair to restage Ecruteak")
  fabricate states scripts never produce from that base.
- **Arbitrary teleports**: `location.set` only writes exact
  `fixtures/safe-spawns.json` entries (19 tiles, each a real-save
  observation); anything else exits 8 `location-unverified`.
- **Movement-grid walkability check**: permissions live in compiled
  `mapmatrix_*.bin`/`maptable` blobs with no decompiled per-tile
  source (`metatile_behavior.h` has only flag bits). No verdict will
  be shipped until a decoder validates against every known player
  tile first.
- **Mid-cutscene `.sav` states** don't exist — cutscenes are
  transient; `.mcpr` savestates capture them, raw saves can't.
- **Event movies are observed, not asserted**: the driver proves the
  game reaches the trigger with the gate conditions held; nobody has
  watched the movie frame-by-frame in headless runs.

## 7. Open gaps and what unblocks each

- Lake pre-Gyarados, gym-puzzle interiors: no base near any of
  them; backward resets refused. Unblocked by legitimately played raw
  saves at those points. (Olivine pre-Jasmine was here until Nov 2026;
  filled by play as `hg-olivine-pre-jasmine`, see §9.)
- Lake trek status (Nov 2026): gate decoded (`scr_seq_0938_T29.s`;
  RED_SCALE/CAUGHT/HIDE_GYARADOS/HIDE_LANCE pins), bracket verified
  (6gyn pre, 7gyn post), 71-save census confirms no lake base exists.
  Ecruteak maze partially mapped (y=184 road to x=413 tree wall,
  Pokécenter door ~(397,182), interiors drop the Location pattern —
  see `docs/research-findings.md`). Route: R42 Surf-or-Mortar +
  R43 north. Repel + party staged; exit/road still open.
- HG 7gyn, HG posE4, HG Celebi postgame: same story points as
  existing fixtures (SS 7gyn Pryce, SS pose4 Champion, Lyra Celebi
  pre-state) — checked live, skipped as duplicates, not oversights.

## 8. Play-to-fill loop, proven end-to-end (Nov 2026)

Driver additions in `tools/leveld_run.py`: `--touches
'frame:x+y:frames'` (touch verified live: tapping POKEDEX opens the
dex, SEEN 014; POKEMON opens the party screen) and `--extract-save
auto` (exports the in-game battery via `melonds_backup_export`; the
headless core keeps saves in memory with no auto-flush, so without
this an in-game save never reaches disk).

Button-only save flow from overworld (all observed on screenshots):
X opens the button cursor (POKEDEX highlighted), RIGHT moves it to
M@ster, DOWN to SAVE, A opens the save panel, A confirms "Would you
like to save", A confirms the overwrite YES, "Saving a lot of
data..." writes for several hundred frames, then back to overworld.
Touch taps on SAVE at three coords did nothing while POKEDEX/POKEMON
taps worked — cause unknown, button path used instead.

Proof run `leveld-out/walk-save-loop` (SS Violet base): DOWNx30
walked 246→248 live in RAM, full save flow, export. `inspect`:
valid SS save, checksums pass, location (459,248,3). `diff` vs the
staged file: `location.y` 246→248, play time 30:51→31:21,
`VAR_FRIENDSHIP_STEPS` 68→70 (+2 steps, a genuine played delta),
active partition A→B, 925 changed bytes accepted; 0 flags, 0 bag,
0 party changes.

Two corrections this work forced:

- Fresh 700-frame boots reach the GAME FREAK title screen, not the
  overworld (two boot screenshots confirm, SS and HG). The
  fixture-boot spawn matches come from title-screen RAM copies —
  still real evidence the save loads and the game builds Location
  structs from the staged M/X/Y (likely for the Continue screen),
  but not overworld reachability. Overworld needs ~4200 frames plus
  title navigation (the `ss-violet-states` input pattern).
- The `walk-save-loop` assert failure (`matched`=1, exit 1) was the
  new rule working, not a bug: after the walk only the primary copy
  sits on the new tile. Asserting a walked-to tile can never show
  ≥2; the rule is for fresh-boot agreement.
- Red is beaten on the postgame base (`FLAG_UNK_25F` set by
  `scr_seq_0107_D41R0108.s:26`); Mt Silver pre-Red needs a base from
  before that fight.

## 9. Cianwood trek and fly-map navigation (in progress)

Goal was Olivine pre-Jasmine by play: HG 5gyn base (Chuck beaten,
Jasmine TM flag clear, `FLAG_SYS_FLYPOINT_OLIVINE` set) → surf east
or fly. Surf failed first: the east beach (185,371)→(191,371)→south
(191,379) is a rock-edged dead end (cliff south, building north,
water east never prompts despite a conscious Goldeen with
MOVE_SURF=57); town-south ends at an unwalkable cliff face (185,374).
Fly works mechanically: party menu opens by touch, d-pad drives the
party cursor (DOWN,DOWN,RIGHT lands on Spearow), A opens its submenu,
FLY tap opens the Johto map. MOVE_FLY=19 confirmed on Spearow.
Probed cursor graph from the Cianwood map cursor (description text
as readout, white-frame highlight tracked by screenshot diffing):
LEFT→Route 47 (dead end), RIGHT→Route 41→Whirl Islands→blank→
Goldenrod (sticks); UP/DOWN from Cianwood do nothing short, blank
when held; A on routes/blank safely does nothing. States banked in
`leveld-out/`: `fly-party-state/party.mcpr`, `fly-map-state/flymap.mcpr`,
`fly-blank-a/blank.mcpr`.
RESOLVED by reading decomp instead of guessing
(`pokeheartgold@4e95633`: `overlay_101_021F79B4.c` fly rects,
`overlay_101_021E9270.c:739-747` fly check at `(playerX, playerY-2)`
vs description at `(playerX, playerY)`, `fly_map.c:115-116` cursor
starts at the player's world-matrix tile). Cianwood description is
(5,12-13) but its fly rect is (5,10-11); Olivine needs cursor (8,10):
description (8-9,9-10) plus fly rect (8-9,7-8) at Y-2=8. Path from
Cianwood: UP,UP (through blanks at y11/10) then RIGHT,RIGHT,RIGHT
along the blank corridor (Route 40 at (6,11), blanks, no stop) then
UP once → "Olivine City" readout. A opens the fly context menu, A
confirms (fade + fly animation over the Olivine PokeCenter), landing
in front of the PokeCenter. In-game save needs X,RIGHT,A,A,A + one
more A on the YES/NO overwrite prompt (the start menu remembers its
cursor: this base opens on POKEMON, so RIGHT lands on SAVE with no
DOWN; the SS proof needed DOWN because its base opened on POKEDEX).
Played save `leveld-out/fly-oli-save5/played.sav` (SHA
`ff87a6e5...`): valid HG, location Olivine 77/272/258/z1, Jasmine +
Pryce TM flags clear, Morty/Chuck set, only played deltas vs staged
(time, partition flip, transient `FLAG_TEMP_x4000`, per-map
`FLAG_HIDE_CAMERON` set by Olivine's map script). Staged as fixture
`hg-olivine-pre-jasmine` (recipe + safe-spawn #20, boot-verified with
2 agreeing RAM copies, runtime recorded).
