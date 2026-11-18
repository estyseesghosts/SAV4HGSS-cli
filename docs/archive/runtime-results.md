# Level D runtime results — headless melonDS-MCP

Driver: `tools/leveld_run.py` against the melonDS-MCP native core
(`build/mcp-direct/libmelonds_mcp.dll`, MinGW build, JIT OFF, software
render, FreeBIOS + generated firmware, direct boot). Each run stages a
copy of the retail ROM plus the fixture `.sav` under the same basename
(originals untouched), advances frames with `advance_frames`, captures
PNG milestones, and writes `report.json` (save SHA, frame counts,
liveness color counts, ROM identity).

Mid-run states: `--savestate-at frame:name` captures `.mcpr`
savestates (full emulator state, ~19.7MB) at exact frames;
`--load-state` resumes from one instead of booting fresh. The
menu-time state restored plus one input reaches the identical
overworld as the uninterrupted run — mid-cutscene/menu states are now
capturable, restorable artifacts with save-SHA provenance.

Raw reports/PNGs/states live in `leveld-out/` (local, not committed).

## Boot matrix (frame 700, all fixtures)

Every fixture boots: emulator running, rendering (100+ distinct colors).

| Fixture | ROM | Save SHA (short) | Result |
| --- | --- | --- | --- |
| ss-violet-safe-money | soulsilver.nds (IPGE) | 7cb54e5e | ok, running |
| ss-falkner-gym-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-bugsy-gym-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-azalea-rockets-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-mahogany-takeover-early | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-pryce-gym-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-radio-tower-mid-takeover | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-blackthorn-clair-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-kimono-dance-stage | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-lugia-pre-event-stage | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-ho-oh-rebattle-stage | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-e4-champion-verify | soulsilver.nds (IPGE) | boot-only | ok, running |
| ss-kanto-complete-verify | soulsilver.nds (IPGE) | boot + continue to gatehouse overworld | ok, running |
| hg-dragons-den-shrine | heartgold.nds (POKEMON HG) | boot-only | ok, running |
| hg-celebi-event-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Ilex 117,16,56) | ok, running |
| hg-arceus-event-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Alph 113,441,287) | ok, running |
| hg-pichu-event-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Ilex 117,16,57) | ok, running |
| hg-enigma-stone-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Pewter 51,1048,80) | ok, running |
| hg-whitney-gym-verify | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Goldenrod 76,358,370) | ok, running |
| hg-burned-tower-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Goldenrod 76,358,370) | ok, running |
| hg-rotom-room-prestage | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Saffron 59,1308,231) | ok, running |
| ss-morty-gym-verify | soulsilver.nds (IPGE) | boot + live-spawn assert (Ecruteak 78,381,182) | ok, running |
| hg-chuck-gym-verify | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Cianwood 75,185,371) | ok, running |
| hg-jasmine-gym-verify | heartgold.nds (POKEMON HG) | boot + live-spawn assert (Ecruteak 78,394,184) | ok, running |
| hg-e4-prechallenge | heartgold.nds (POKEMON HG) | boot + live-spawn assert (League 300,8,22) | ok, running |
| ss-kyogre-embedded-prestage | soulsilver.nds (IPGE) | boot + live-spawn assert (gatehouse 172,6,7) | ok, running |

## Continue-to-overworld sequences

Title inputs (`start` @1200, `a` @1800/2400/3000) drive boot → title →
save menu → overworld with screenshots at 1500/2400/3300/4200.

- ss-violet-safe-money: title (Lugia splash) @1500; save menu @2400
  reading PLAYER M@ster / TIME 1:30 / BADGES 1 / POKéDEX 4 — the staged
  fixture values; overworld at Violet + menu @3300/4200. Full pass.
- ss-lugia-pre-event-stage: overworld at Blackthorn @3300 (701 colors)
  with player sprite, following Lugia, and menu open — the staged
  dex/flag state (Lugia uncaught, Silver Wing granted) loads clean.
  Save menu @2400 read the M@ster late-game values. Full pass.
- ss-kanto-complete-verify: overworld inside the Route 38 gatehouse @3300
  (NPCs, menu open) — postgame save loads to overworld. Full pass.
- Mid-run states: menu-time `.mcpr` restored + one input reaches the
  identical Violet overworld as the uninterrupted run (759 colors,
  running). Savestate capture/restore verified end to end.
- In-emulator spawn assertion (`--assert-spawn map,x,y,z`): differential
  of Violet vs Kanto overworld RAM dumps isolated the live decomp
  `Location` struct in ARM9 main RAM (field_types_def.h:10-16: int
  mapId, warpId, x, y, direction — u16 map @+0, u32 0xFFFFFFFF warp
  @+4, u16 x @+8, u16 y @+12, u16 direction @+16). Wanted map/x/y come
  from the staged save's own Location (SAV4HGSS.cs:186-188). Liveness
  proven by holding Up for 120 frames from the Violet overworld state:
  Y 246→244 in place at 0x227d498, map/X unchanged. The z component
  (save Z, SAV4HGSS.cs:200) is recorded, not asserted: real saves carry
  Z=4 (Mahogany) and Z=7 (Blackthorn) while decomp directions are 0..3
  (global_fieldmap.h:4-9 DIR_MAX 4), so they are different quantities.
  - Violet (73,459,246): match at 0x227d498 + mirror 0x239aa40, direction 3.
  - Kanto gatehouse (172,6,7): match at 0x227d498, 0x2377a20, 0x239aa40, direction 1.
  - Lugia/Blackthorn (89,669,180): match at 0x227d498 + mirror 0x239aa40,
    direction 1 (save Z 7 recorded as observed, correctly not gated).

## Scene proofs: composed saves that reach the encounter (2026-11-18)

These are runs of `story scene apply` output, not fixtures: the save the tool
writes is the save that boots.

- **Ho-Oh** (`legendary-ho-oh`, base `hg-08-rising`): booted on
  `heartgold.nds` with the Continue schedule. Frame 4932 is the Bell Tower roof
  with Ho-Oh standing at (15,16), the zone-event position. Holding Up for 340
  frames and pressing A started the static encounter; frames 5674-6182 show
  `HO-OH Lv45` against `Go! CYNDAQUIL!`. The scene's flag writes (hide flag
  clear, `FLAG_ENGAGING_STATIC_POKEMON` clear) are what put the actor there.
- **Pewter Gym** (`gym-09-boulder-before`): booted inside map 473 at the warp
  tile (7,27) with the gym guide's dialogue on screen ("The Trainers of this
  Gym use Rock-type Pokémon."). Brock is the room object at (7,4)
  (`zone_event 048_T03.json`), and the "before" scene cleared his hide flag
  the way the gym's transition script does.
- **Red** (`legendary-red`, base `hg-kanto-4` + Kanto 255): booted on
  `heartgold.nds`; the player stands on the Mt. Silver summit at the saved
  warp tile (30,36). Walking north reaches Red at (30,11), the prompt reads
  `TALK`, and A starts the battle: frames 5916-6504 show the fade and then
  `PKMN Trainer Red sent out PIKACHU!`. The save carries the whole unlock
  chain, not only `FLAG_UNLOCKED_MT_SILVER` (`docs/archive/state-log.md` F18).
- **Lance's room** (`e4-lance`): the map's frame table runs
  `WalkNormalNorth 6` on entry (`scr_seq_0597_T10R0601_hdr.s:10-14`,
  `scr_seq_0824_T10R0601.s:31-33`), so the player ends at (6,22) even though
  the save's warp tile is (6,28). The scene records that as
  `expect.spawn_after`, and the boot check asserts a Location copy that shows
  the moved tile (`leveld_run.py --spawn-primary`; the active copy's address
  is map-dependent, see below) because a moved player leaves the mirrors stale.
- **Kyogre** (`legendary-kyogre`, base `hg-kanto-4`): booted on
  `heartgold.nds`. The Continue screen (frame 4646) reads PLAYER Ash, TIME
  0:48, BADGES 16, POKéDEX 256. The live Location is map 525 at (16,29), the
  room's only warp tile (`zone_event 477_D52R0102.json`: (16,29) ->
  `MAP_ROUTE_47`). Holding Up for 340 frames walks north to (16,12), one tile
  short of the actor at (16,11). A starts the encounter script: frame 6495
  reads `KYOGRE: Gyaro roooah!`, frame 7015 is the battle screen
  `KYOGRE Lv50`, frame 7105 reads `Go! CYNDAQUIL!`.
- **Groudon** (`legendary-groudon`, base `ss-kanto-8`): the same recipe on
  `soulsilver.nds`. Continue (frame 4606) reads PLAYER NotKris, TIME 0:50,
  BADGES 16, POKéDEX 256. Live Location is map 524 at (16,29)
  (`zone_event 476_D52R0101.json`, same tile), Up for 340 frames reaches
  (16,12), A gives `GROUDON: Grrgggrrrrah!` (frame 5588) and the battle screen
  `GROUDON Lv50` (frame 6108). Both orb items the scripts check (Blue Orb
  item 535 for Kyogre, Red Orb 534 for Groudon,
  `scr_seq_0134_D52R0102.s:35-38` / `scr_seq_0133_D52R0101.s:35-38`) come from
  the scene's own inventory writes, not from a fixture.

### Free-spawn runs: every legendary fought (2026-11-18, later session)

`location.warp` with `warp: -1` writes a dynamic warp whose coordinates the
game keeps, because `src/field_warp_tasks.c:206-209` only replaces x/y when
`warpId != -1`; the game itself uses that path for the Union Room exit
(`:380-382`). Each save below is `story scene apply`, then a one-op patch
putting the player one tile south of the actor, then Continue.

- **Lugia** (`legendary-lugia`, plus a Shellder with `MOVE_SURF` imported into
  party slot 2): booted at the chamber entrance (16,38). The room is a water
  crossing, so the walk is (16,38) -> (16,23), where the facing metatile
  behaviour is 0x15 `TILE_BEHAVIOR_WATER_SEA` (`src/metatile_behavior.c:29`).
  A gives "The water is a deep blue... Would you like to surf on it?", a second
  A gives `SHELLDER used Surf!`, then Up twice reaches (16,18) below Lugia at
  (16,17). A gives `LUGIA: Gyaaas!` at frame 9263 and frame 10093 is
  `LUGIA Lv70` / `Go! CYNDAQUIL!`. The earlier "no Surf prompt" attempts were
  all at tiles whose facing behaviour is 0 or 8, measured with a breakpoint on
  `Field_PlayerCanSurfOnTile` (`0x021F20C0`, `asm/overlay_01_021F1AFC.s:763`):
  0 at (16,32), 8 at (16,34), 8 at (18,32), 0 at (20,32), 0 at (16,25).
- **Suicune** (`legendary-suicune`): free spawn at (1421,38) on map 29, one
  tile south of the actor at (1421,37). Continue reads Ash / 0:48 / 16 badges;
  A gives `SUICUNE Lv40` at frame 5812.
- **Articuno** (`legendary-articuno`): free spawn at (27,23) on map 456.
  Frame 6022 is `ARTICUNO Lv50` / `Go! CYNDAQUIL!`.
- **Zapdos** (`legendary-zapdos`): free spawn at (1419,185) on map 18.
  Frame 5972 is `ZAPDOS Lv50`.
- **Moltres** (`legendary-moltres`): free spawn at (10,10) on map 462.
  Frame 5972 is `MOLTRES Lv50`.
- **Mewtwo** (`legendary-mewtwo`): free spawn at (56,25) on map 451 in the
  dark Cerulean Cave room. Frame 6072 is `MEWTWO Lv70`.
- **Latios** (`legendary-latios`, base `hg-kanto-4`): free spawn at (1039,84)
  on map 51. A gives `LATIOS: Shuaaaann!`, frame 6102 is `LATIOS ♂Lv40`,
  frame 6282 `Go! CYNDAQUIL!`.
- **Latias** (`legendary-latias`, base `ss-kanto-8`): the same tile on
  `soulsilver.nds` with the SS save (NotKris / 0:50 / 16 badges). A gives
  `LATIAS: Huaaaaann!`, frame 6202 is `LATIAS ♀Lv40`, frame 6402
  `Go! TODODILE!`.

Latios and Latias needed a scene fix, not just a spawn. The talk script
`scr_seq_T03_010` opens with `ScrCmd_546 0, VAR_TEMP_x4000` and returns to
`_0649` (release and end, no message) when it is zero
(`scr_seq_0750_T03.s:360-362`). That command is
`BOOL sub_02066BC0(state, 0)`, which compares `VAR_UNK_4043` against
`_020FE4A4[0] = 0x06F2` (`src/sys_vars.c:10-13,117-121`); the only writer is
the Enigma Stone mystery gift (`src/scrcmd_mystery_gift.c:347-348`). The
scenes already granted `ITEM_ENIGMA_STONE` (536, `include/constants/items.h:957`)
and set `VAR_UNK_4083 = 3`, which is why the actor was visible but silent;
adding `VAR_UNK_4043 = 1778` made A start the battle. Both scenes re-boot at
their warp tile (1048,78) with the var written.

Legendary coverage is now 13/13 fought from composed saves: Ho-Oh, Lugia,
Articuno, Zapdos, Moltres, Mewtwo, Suicune, Latios, Latias, Kyogre, Groudon,
Rayquaza and Red (Red is the trainer fight, not a legendary, and has its own
bullet above). The nine story-battle scenes still have boot rows only.

### Story-battle scenes: two fought, seven still boot-only

The nine story battles added in `2ac4acd` had boot rows only. Two now have
fight evidence:

- **Mt. Moon rival** (`event-rival-mount-moon`, free spawn (10,5) on map 107):
  the encounter is a coord event, so it fires on arrival without an input -
  the first frame after Continue reads `Barry: Hey, Ash! You're here in
  Kanto, too?`. Six A presses reach the `VS Barry` screen (frame 6070), and
  frame 6370 is `You are challenged by Rival Barry!` with six enemy balls;
  frame 6590 is `Rival Barry sent out SNESEL!`.
- **Kimono battles** (`event-kimono-battles`, base spawn (7,17) on map 86, not
  a free spawn): the free spawn at (2,7) put the camera off the room's loaded
  area and the 3D layer came up blank, so the save was rebuilt at its own warp
  tile and the player walked north onto the stage. A gives `Welcome. My name
  is Zuki.`, the speech runs, and frame 7696 is `UMBREON Lv38` against
  `Go! CYNDAQUIL!` - the first of the five.

- **Burned Tower rival** (`event-rival-burned-tower`): free spawn at (23,15)
  puts the player on the floor below the rival; A does nothing because the
  actor at (23,14) is on the raised level. It needs the walk from the scene's
  own (22,16) spawn, not a free spawn.

The remaining seven (`rival-azalea`, `rival-goldenrod-tunnel`,
`rival-victory-road`, `petrel-radio-tower`, `archer-radio-tower`,
`petrel-rocket-hq`, plus the second through fifth Kimono battles) still have
boot rows only.

## Gym leaders fought from composed saves (15 of 16, 2026-11-18)

Fifteen of the sixteen leaders have a real battle started from the composed save,
not a boot-only row. Each save is `gym-0N-*-before` from
`registry/hgss-us.scenes.json`, composed by `tools/make_scenes.py`, and each
keeps the leader's own `FLAG_HIDE_*` cleared so the actor exists. A fight
counts only when the leader's name or party is on screen. Chuck is the
exception and is covered below.

| Scene | Map | Spawn | Proof |
| --- | --- | --- | --- |
| `gym-01-zephyr-before` | 135 | free spawn (15,5) | frame 6698 `Leader Falk`, frame 6998 `PIDGEY Lv9` vs `CYNDAQUIL` |
| `gym-02-hive-before` | 180 | free spawn (9,6) | frame 6698 `Leader Bugsy sent out SCYTHER!` |
| `gym-03-plain-before` | 137 | free spawn (13,5) | frame 6698 `You are challenged by Leader Whitney!` |
| `gym-04-fog-before` | 80 | free spawn (10,9) | frame 7782 `GASTLY Lv21` |
| `gym-06-mineral-before` | 138 | free spawn (6,6) | frame 7298 `Leader Jasmine sent out MAGNEMITE!`, frame 7758 `Go! CYNDAQUIL!` (MAGNEMITE Lv30) |
| `gym-07-glacier-before` | 140 | free spawn (7,5) | frame 7352 `Leader Pryce sent out SEEL!`, frame 7582 SEEL Lv30 vs CYNDAQUIL |
| `gym-08-rising-before` | 141 | free spawn (12,4) | frame 7352 `Leader Clair sent out GYARADOS!`, frame 7582 GYARADOS Lv38 vs CYNDAQUIL |
| `gym-09-boulder-before` | 473 | free spawn (7,5) | frame 7372 `Leader Brock sent out GRAVELER!`, frame 7622 GRAVELER Lv51 |
| `gym-10-cascade-before` | 427 | free spawn (10,2) | frame 6912 `You are challenged by Leader Misty!`, frame 7372 GOLDUCK Lv49 |
| `gym-11-thunder-before` | 365 | free spawn (6,5) | frame 6912 Surge's battle pose, frame 7372 RAICHU Lv51 |
| `gym-12-rainbow-before` | 395 | free spawn (7,4) | frame 8422 JUMPLUFF Lv51, frame 8882 move prompt |
| `gym-13-soul-before` | 480 | free spawn (7,17) | frame 7332 CROBAT Lv47, frame 7792 `The foe's CROBAT used Wing Attack!` |
| `gym-14-marsh-before` | 410 | free spawn (15,14) | frame 7332 `You are challenged by Leader Sabrina!`, frame 7792 ESPEON Lv53 |
| `gym-15-volcano-before` | 457 | free spawn (13,13) | frame 7332 `You are challenged by Leader Blaine!`, frame 7792 MAGCARGO Lv54 |
| `gym-16-earth-before` | 496 | free spawn (5,5) | frame 7792 `You are challenged by Leader Blue!`, frame 8062 EXEGGUTOR Lv55 |

None of the three needed a gate fix. Jasmine's script `000` gates only on `BADGE_MINERAL`, and her
OnTransition script (`scr_seq_0913_T26GYM0101.s:22-32`) only hides her when
`GetPhoneBookRematch PHONE_CONTACT_JASMINE` is non-zero, which a mid-story save does not set.
Pryce's gates only on `BADGE_GLACIER` (`scr_seq_0932_T28GYM0101.s`, `_000`). Clair checks
`FLAG_UNK_0EA` and `FLAG_UNK_0D1` (`scr_seq_0943_T30GYM0101.s`, `_000`); `0xD1` is set by her own
victory branch, so it is clear in a before-save. The ice puzzle and the Blackthorn walkway are
walkability problems, not script gates, so a free spawn on the leader's platform skips them
without producing an impossible state: the player standing next to the leader with the badge
unearned is exactly what the walk produces.

`gym-04-fog-before` is the first scene that cannot reuse the previous badge
preset. Burned Tower is mandatory between badges 3 and 4 and is not a badge, so
the scene takes `hg-04-fog` and walks the Morty rewards back: Fog badge masked
to 7 (three badges), `flag.set 166 false` and `inventory.quantity.set 357 0`
(`GYM_BEFORE_OVERRIDES` in `tools/make_scenes.py`). The previous attempt was
ejected from the gym because `VAR_UNK_4079 == 0` sends the player to the
Ecruteak exit (`scr_seq_0695_T27GYM0101_hdr.s`), and the override writes the
Burned Tower completion value 2 (`scr_seq_0024_D18R0102.s:133`).

`gym-05-storm-before` is not in this table. Chuck is gated on the winch, which
is save-block state that the map initialiser wipes and a script variable that
the tool refuses to write; the scene is a correct pre-badge state (entrance
warp, Chuck present, badge unearned) but the fight needs the puzzle solved in
session. See F21 in `docs/archive/state-log.md`.

## Elite Four rooms fought from composed saves (5 of 5, 2026-11-18)

All five rooms battle from an `hg-e4-gate` base plus a free spawn. Four rooms use a plain object
script (A on the leader); Lance's is a coordinate trigger, not an object script.

| Scene | Map | Spawn | Proof |
| --- | --- | --- | --- |
| `e4-will` | 301 | free spawn (6,11) | frame 7744 `Welcome to the Pokémon League!`, 9724 `You are challenged by Elite Four Will!`, 9944 XATU Lv40 |
| `e4-koga` | 302 | free spawn (6,12) | frame 6166 `Fwahahahaha!`, 7706 VS Koga, 8396 ARIADOS Lv40 |
| `e4-bruno` | 303 | free spawn (6,11) | frame 7726 VS Bruno, 8416 HITMONTOP Lv42 |
| `e4-karen` | 304 | free spawn (6,11) | frame 7266 VS Karen, 7726 `Elite Four Karen sent out UMBREON!` |
| `e4-lance` | 305 | free spawn (6,16) | frame 7144 `Lance: I've been waiting for you.`, 8904 `You are challenged by Champion Lance!`, 9124 `Champion Lance sent out GYARADOS!`, 9344 GYARADOS Lv46 |

Two room behaviours had to be handled. The four object-script rooms run an intro from the frame
table on load (`InitScriptEntry_OnFrameTable` -> `_EV_scr_seq_..._000`), which walks the player six
tiles north with `ApplyMovement obj_player` before releasing. A free spawn therefore lands the player
*past* the leader: Will's room put the live Location at (6,5) with Will still at (6,10), so the run
walks back to (6,9) and presses A. Lance's room is the opposite case: his object has `script: 0`, and
the battle comes from a coordinate trigger at (5..7, 10) gated on `VAR_UNK_40C7 == 0`
(`registry/hgss-us.scene-triggers.json`, `scr_seq_0824_T10R0601.s` `_000`). Spawning at (6,16) makes
the intro's six-tile walk end on that tile, but the scripted walk does **not** satisfy the trigger;
stepping off to (6,13) and back onto (6,8) does, and the battle starts.

### The "stuck start menu" was a stale screen capture, not a save problem

The earlier Kyogre attempt concluded the pause menu was open and would not
close. The bottom-screen capture in the MCP session kept showing the pause
menu for hundreds of frames while the top screen advanced and the field kept
running. It was not actually open: pressing Down moved the player, which a
modal menu would have swallowed. That Down press is what ended the run - the
player at (16,29) took the room's exit warp and the live Location became
`MAP_ROUTE_47` (87,390); walking Up onto the Route 47 door tile (87,389)
returned to map 525. The working input schedule is: wait for the title logo
(frame ~4400; the intro movie is long), Start -> Continue, A to load, then hold
Up to the actor. Do not press Down or A at the arrival tile.

Measured caveat for the harness: the active player Location copy is not at a
fixed address. It was `0x227d4f8` on HG map 525 and `0x227d4d0` on SS map 524,
while the e4-lance run matched `0x227d498`. All three carry
`map, 0, x, 0, y, 0, direction` in the layout of `field_types_def.h:10-16`.

The full per-scene matrix is `docs/scene-boot-matrix.md`, generated by
`tools/scene_boot_matrix.py` from the run summaries; it names each scene's
base preset, map, saved tile, asserted tile and result.

## How to reproduce

```powershell
$env:MINGW_BIN="<WinLibs>\mingw64\bin"  # for MinGW runtime DLLs (ctypes)
<Player>/.venv/Scripts/python.exe tools/leveld_run.py --rom <soulsilver.nds> `
  --save fixture-out/<id>.sav --outdir leveld-out/<id> --label <id> `
  --frames 700 --shots 700
```

Continue variant adds `--frames 4200 --shots 1500,2400,3300,4200
--inputs '1200:start:8,1800:a:8,2400:a:8,3000:a:8'`.
