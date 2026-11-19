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
bullet above). The story-battle scenes had boot rows only at this point;
fight evidence for most of them is recorded in the sections below.

### Story-battle scenes: fight evidence added 2026-11-18

The nine story battles added in `2ac4acd` had boot rows only at this point in
the record; two now have fight evidence:

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

The remaining seven listed here had boot rows only at this point in the
record; the later sections below close six of them (Azalea, Goldenrod Tunnel,
Petrel Radio Tower, Archer, and all five Kimono sisters as one chain), leaving
Burned Tower, Victory Road 3F, Rocket HQ Petrel and the League-entrance rival
without fight evidence.

## Gym leaders fought from composed saves (16 of 16, 2026-11-18)

All sixteen leaders have a real battle started from the composed save, not a
boot-only row. Each save is `gym-0N-*-before` from
`registry/hgss-us.scenes.json`, composed by `tools/make_scenes.py`, and each
keeps the leader's own `FLAG_HIDE_*` cleared so the actor exists. A fight
counts only when the leader's name or party is on screen. Chuck needed the
winch turned in the same session; the route is recorded below the table.

| Scene | Map | Spawn | Proof |
| --- | --- | --- | --- |
| `gym-01-zephyr-before` | 135 | free spawn (15,5) | frame 6698 `Leader Falk`, frame 6998 `PIDGEY Lv9` vs `CYNDAQUIL` |
| `gym-02-hive-before` | 180 | free spawn (9,6) | frame 6698 `Leader Bugsy sent out SCYTHER!` |
| `gym-03-plain-before` | 137 | free spawn (13,5) | frame 6698 `You are challenged by Leader Whitney!` |
| `gym-04-fog-before` | 80 | free spawn (10,9) | frame 7782 `GASTLY Lv21` |
| `gym-05-storm-before` | 139 | free spawn (10,3), winch turned in-session | 26556 `Chuck: Oomph!`, 29006 VS Chuck, 29226 `You are challenged by Leader Chuck!`, 29666 PRIMEAPE Lv29, 31426 `Leader Chuck sent out POLIWRATH!` (Lv31), 34506 `The foe's POLIWRATH fainted!`, 34726 `We...lost...`, 34946 `Ash got ₽3720 for winning!`, 35606 `Ash received the Storm Badge from Chuck!` |
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

`gym-05-storm-before` is the one gym that cannot be fought from a free spawn alone. Chuck's
script branches to the "doesn't notice you" message while `VAR_TEMP_x4000 == 0`
(`scr_seq_0877_T24GYM0101.s:24-27`), and only the winch script writes it
(`CianwoodGymTurnWinch VAR_TEMP_x4000`, `:121`; `src/scrcmd_c.c:3119-3123`). That var cannot be
baked into the save: `ScriptManager` zeroes `NUM_TEMP_VARS * 2` bytes at `TEMP_VAR_BASE` on map
load (`src/script_manager.c:406-407`), and the gym's init script calls `CianwoodGymInit`
(`scr_seq_0877_T24GYM0101.s:13-16`, `src/gymmick_init.c:49-54`). Writing `0x4000` with the
numeric patch form applies, reads back as 1, and still produces the "doesn't notice you" line
in game.

So this proof turns the winch in the session it fights in:

1. Free spawn at the winch talk tile (10,3), face north, A (`There is a large winch. Will you
   turn it?`), then A again (`Ash turned the winch with pure might!`). The message needs its own
   A press; until it is dismissed every d-pad press is swallowed.
2. Wait out the camera task (~1,500 frames; `Task_CianwoodGymmick_LiftWinchCutscene` walks
   through six states in `src/field/gymmick/cianwood.c:87-130`). The rebind is visual only:
   `renderObj_173` gets animation set 174 and `renderObj_175` is animated, no collision change.
3. Walk the room. Confirmed route: (10,3) -> (6,3) -> (6,4) -> (6,5) -> (4,5) [Black Belt Nob]
   -> (4,6) -> (4,7) -> (10,7) -> (10,8) -> (10,9) -> (7,9) [Black Belt Yoshi] -> (7,10) ->
   (7,11) -> (13,11), then face north and press A on Chuck.
4. Both Black Belts sit on the route with sight lines (`xRange` 3 and 4 on the other two; Nob and
   Yoshi engage on the crossing), so their battles are part of the walk. A single-member party is
   what keeps this runnable: with three members, the second Pokémon's send-out raises the Shift
   prompt, and an empty message box there wedged the run.

Two harness corrections came out of this and are worth keeping: HGSS movement is `up` = z-1 and
`down` = z+1 (I had it inverted for a while and kept walking into the north wall), and the
pause menu is toggled by `X` — an accidental `X` press looks exactly like "the geometry is
blocked" because the d-pad then moves the menu cursor while the live Location stays put.

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

## Story battles fought from composed saves (2026-11-18)

| Scene | Map | Entry | Proof |
| --- | --- | --- | --- |
| `event-rival-azalea` | 74 (Azalea) | scene warp 0 -> (392,462), walk east to the coord trigger at (395,462..466) | frame 5726 trigger fires and the rival walks in, 7196 VS Barry, 7406 `Rival Barry sent out GASTLY!` (Lv14) |
| `event-rival-goldenrod-tunnel` | 201 | scene warp 0 -> (27,4), walk to the coord trigger at (19,5) | frame 5838 rival walks in and pushes the player back to (20,5), 7518 VS Barry, 8418 GOLBAT Lv30 |
| `event-petrel-radio-tower` | 189 | scene warp 0 -> (3,5), walk to (4,13) beside the seated director | frame 6722 `Ahem, do you hear me?`, 8192 `Petrel: No, no, no!` reveal, 9062 `You are challenged by Executive Petrel!`, 9282 KOFFING Lv30 |
| `event-archer-radio-tower` | 190 | free spawn (8,4), A on Archer at (8,3) | frame 5716 `Archer: Oh? You managed to get this far...?`, 7396 `You are challenged by Executive Archer!`, 8086 HOUNDOUR Lv35 |
| `event-rival-burned-tower` | 7 (Burned Tower 1F) | scene warp 1 -> (22,16), four `down` steps to the coord trigger at (22,19) | frame 5401 `Barry: ...` fires, 6003 VS screen, 6407 `Go! TYPHLOSION!` into GASTLY Lv20, 22901 `Player defeated Rival Barry!` |
| `event-rival-victory-road` | 179 (Victory Road 3F) | scene warp 0 -> (38,14), walk to the coord trigger at x 37..39, z 21 | frame 5451 `Hold it.`, battle vs `TRAINER_RIVAL_SILVER_13` SNEASEL Lv36, win ~20130, in-game save `B7795FAF` with `VAR_UNK_40C5` 0 -> 1 |
| `event-petrel-rocket-hq` | 249 (Team Rocket HQ B3F) | scene warp 1 -> (10,10), east to x 16, south to z 17, east to x 23, north to the corridor door at (23,15), A to open it, north to the coord trigger at (23,12) | cutscene runs (Giovanni, then Petrel's `...Huh? I don't sound anything like Giovanni?`), frame 8457 `Executive Petrel sent out ZUBAT!`, battle won, in-game save `E0B56B9C` with `VAR_UNK_40AC` 3 -> 4 |

Burned Tower is the first of these whose win was saved in-game and diffed, so it carries more than
a message string. The composed input was `47FFCB20` (scene warp 1 plus a Lv100 Typhlosion in party
slot 0); melonDS wrote `heartgold.sav` at 7:15:21 PM, SHA-256 `829CB64D`. The game-written delta:

| Field | Before | After | Script line |
| --- | --- | --- | --- |
| `FLAG_HIDE_BURNED_TOWER_1F_RIVAL` (454) | off | on | `scr_seq_0023_D18R0101.s:55` |
| `VAR_UNK_40A6` (0x40A6) | 0 | 1 | `scr_seq_0023_D18R0101.s:56` |
| `VAR_BATTLE_RESULT` (0x4013) | 0 | 1 | the encounter result, written by `Encounter_GetResult` (`src/encounter.c:106`) from the battle's win flag (1 = won); `ScrCmd_CheckBattleWon` only reads it (`scrcmd_battle.c:218`) |
| money | 3,444 | 4,852 | 1,408 = 22 x 4 x 16: last party member's level x 4 x the rival class multiplier (`battle_command.c:2176,2218`, `sPrizeMoneyTbl` `TRAINERCLASS_RIVAL, 16` at `asm/overlay_12_battle_command.s:216`) |
| `VAR_FRIENDSHIP_STEPS` (0x404B) | 88 | 90 | +1 per step event, wrapping to 0 at 128 (`FieldSystem_UpdateFriendship`, `src/field/field_control.c:766-777`); the two steps are the walk onto the trigger |
| `FLAG_UNK_966` (0x966) | on | off | `location warp`'s dynamic-warp flag, cleared on load |

The saved tile is also independent proof: the script's own `_00E8` movement at `:75-79` is
`WalkNormalSouth 2` then `WalkNormalEast`, so from the trigger at (22,19) the player ends on
(23,21). The written save has `location.map_id` 7 at x 23, y 21 - exactly that.

`VAR_BATTLE_RESULT` printing as `"name": null` in the first diff is what exposed a second instance
of the temp-var bug: `SaveDiff` asked the registry for `SymbolKind.SaveWork` only, but `0x4013` is
registered as `temporary_var`. Fixed alongside this recording.

### Victory Road 3F rival: won on the third attempt

Same shape as Burned Tower - scene warp 0 lands at (38,14), the battle is the coord trigger over
x 37..39 at z 21 while `VAR_UNK_40C5 == 0` (`scr_seq_0110_D43R0103.s:16`), so it is a walk, not an
A-press. Trigger fired at frame 5451 (`Hold it.`), VS screen, battle against SNEASEL Lv36.

The opponent is `TRAINER_RIVAL_SILVER_13` (id 272), not `SILVER_5`. `:50-63` branches on
`GetStarterChoice`: 152 -> `SILVER_9`, 155 -> `SILVER_13`, anything else -> `SILVER_5`. This save
takes the 155 branch. Two independent checks agree: `trainers.json[272]` is Sneasel 36, Golbat 38,
Magneton 37, Haunter 37, Kadabra 37, Feraligatr 40, and the prize money below is 40 x 4 x 16, which
only fits the Lv40 Feraligatr in the last array slot.

Won at frame ~20130 (the rival's loss line `I'm going to find out why I can't win and become
stronger...`), saved in-game, SHA-256 `B7795FAF`. The input actually loaded was `vr3f-won.sav`
(`7F9FD2C8`, the mid-approach save at (38,18) left by the aborted second attempt), so that is the
diff baseline:

| Field | Before | After | Justification |
| --- | --- | --- | --- |
| `VAR_UNK_40C5` | 0 | 1 | `scr_seq_0110_D43R0103.s:90` (the win write) |
| `VAR_BATTLE_RESULT` (0x4013) | 0 | 1 | `Encounter_GetResult`, `src/encounter.c:106` |
| `VAR_TEMP_x4000` | 0 | 38 | `GetPlayerCoords VAR_TEMP_x4000, VAR_TEMP_x4001` at `:71` |
| `VAR_TEMP_x4001` | 0 | 22 | same call |
| money | 3,444 | 6,004 | 2,560 = 40 x 4 x 16 (last array member Lv40) |
| `VAR_FRIENDSHIP_STEPS` | 92 | 94 | two step events: the walk (3 tiles) plus the scripted `WalkSlowSouth` only counted twice |
| `location.y` | 18 | 22 | three walked tiles plus the script's one-tile slow step |

`FLAG_UNK_966` was already cleared by the previous load, so it does not appear here.

The saved tile is (38,22), one south of the trigger tile, which is the script's own doing:
`ApplyMovement obj_player, _0178` at `:42` runs before the battle and `_0178` is `WalkSlowSouth`
(`:109-111`). The post-battle `GetPlayerCoords` at `:71` then reads (38,22) into the two temp vars,
and the save stores map 179 at x 38, y 22 - all three agree.

Two earlier attempts at this scene failed and both failures are worth keeping:

- **A-spam picks move slot 1.** The lead had Earthquake first, and Ground is immune on Haunter
  (Ghost) and Golbat (Flying). Four wasted turns let Haunter's Curse chip the lead 298 -> 0. The
  fix was ordering moves by coverage for the actual roster, not by power: Flamethrower has no
  immunity on this team.
- **Victory Road has wild encounters.** A seven-step walk stopped after four tiles at (38,18)
  because a wild Golbat battle ate the rest of the presses. `VAR_FRIENDSHIP_STEPS` 88 -> 92 counted
  the four steps and exposed it. Walking the remaining tiles one press at a time, checking the
  screen between, is the working method.

Three entries needed correction from what the earlier free spawns assumed:

- **Azalea.** The rival sprite at (384,448) in `071_T23.json` is a different cutscene object
  (`scriptId 0`, `FLAG_NOTHING`). The battle is the coord trigger over x=395, z=462..466 while
  `VAR_UNK_4075 == 1` (`registry/hgss-us.scene-triggers.json`, `scr_seq_0866_T23.s` `_002`).
  The scene's own warp 0 already lands at (392,462); three real steps east fire it. A free spawn
  at the actor sprite's tile is a dead end.
- **Goldenrod Tunnel.** Same shape: the trigger is (19,5) while `VAR_UNK_40A0 == 0`, and the
  rival sprite sits at (29,29). Walking west from warp 0's (27,4) works; the cutscene moves the
  player to (20,5) itself.
- **Radio Tower 5F.** Warp 0's arrival tile (3,5) is fine, but the desk blocks the direct line
  south at (5,10). The director sits at (5,13) behind the counter, and the talk range is the
  column beside him: walking down the west side to (4,13) and pressing A starts the script.
- **Radio Tower observation deck.** Warp 0's tile (8,12) is the elevator landing and the next
  `up` press sends the player to `MAP_GOLDENROD_RADIO_TOWER_ELEVATOR` (447). Archer is a plain
  A-triggered object, so the free spawn at (8,4) is the correct entry for this room.

### Rocket HQ B3F Petrel: the corridor door gates the checkpoint

`event-petrel-rocket-hq` composes `hg-06-mineral` with `VAR_UNK_40AC = 3` and warps to 249's warp 1
tile (10,10). The room's geometry forces the long way round: a barrier at x 17-18 runs from z 4 to
z 12, so the x 23-24 corridor is reached from the south (z 17) and entered northward. The coord
trigger is the strip (23..24, 12) while `VAR_UNK_40AC == 3` (`235_D35R0104.json` coords,
`scr_seq_0091_D35R0104.s:246`).

The corridor's row z 15 is a closed door: two objects at (23,15) and (24,15) both run
`_EV_scr_seq_D35R0104_004` (`event_D35R0104.h:8`), and `_004:427-428` is
`GoToIfNotDefeated TRAINER_TEAM_ROCKET_F_GRUNT_5 / TRAINER_TEAM_ROCKET_GRUNT_19 -> _0561`, where
`_0561` prints `The door's closed...`. With both flags set the script falls through, plays
`msg_0114_D35R0104_00012`, and walks both door objects three tiles west (`_056C`).

The first version of the scene carried the checkpoint variable without those two flags, which is a
state the game never produces: `_006:18-25` only raises `VAR_UNK_40AC` to 2 after both grunts are
beaten, and `_002:198` raises it to 3. The scene now sets `FLAG_TRAINER(404)` and `FLAG_TRAINER(222)`
alongside the variable (`tools/make_scenes.py`), so the composed state carries the trainers' own
defeat flags.

One measurement does not fit the source and is recorded rather than explained: **the first A press
on that door in a session takes the closed branch and the second opens it**, on the flags-set save.
Two presses 40 frames apart cannot differ in flag state, because `TrainerFlagSet` is only called
from a won trainer battle (`src/scrcmd_battle.c:249`) and from `ScrCmd_SetTrainerFlag`
(`src/scrcmd_c.c:524`); nothing clears them on load either. The win proof below does not rest on
this, but anyone re-running the scene will see it.

The win was saved in-game and diffed against the composed save (`4560CAAA`), which the game wrote
as `E0B56B9C`:

| Field | Before | After | Script line |
| --- | --- | --- | --- |
| `VAR_UNK_40AC` (0x40AC) | 3 | 4 | `scr_seq_0091_D35R0104.s:310` |
| `FLAG_HIDE_ROCKET_HIDEOUT_B3F_GIOVANNI` (640) | off | on | `:279` |
| `FLAG_HIDE_ROCKET_HIDEOUT_B3F_MURKROW_1` (586) | off | on | `:312` |
| `FLAG_HIDE_ROCKET_HIDEOUT_B2F_MURKROW_1` (587) | on | off | `:313` |
| `VAR_TEMP_x4000` / `x4001` | 0 / 0 | 23 / 12 | `GetPlayerCoords` at `:254` (the trigger tile) |
| `VAR_BATTLE_RESULT` (0x4013) | 0 | 1 | `Encounter_GetResult`, `src/encounter.c:106` |
| money | 3,444 | 4,324 | 880 = 22 x 4 x 10: Koffing is Petrel's last member and `sPrizeMoneyTbl[TRAINERCLASS_EXECUTIVE_PETREL]` is 10 (`asm/overlay_12_battle_command.s`, class 118 at `include/constants/trainer_class.h:122`) |
| `VAR_FRIENDSHIP_STEPS` | 88 | 112 | +1 per step event |
| `FLAG_UNK_966` (0x966) | on | off | `location warp`'s dynamic-warp flag, consumed on load |

Two independent checks agree on where the trigger fired: the two temp vars hold (23,12), the coord
event's own rectangle, and the saved location is (22,10), which is where `_040C`/`_041C` put the
player before Giovanni's dialogue (`:257-261`).

The staged party carried a second member (Hoothoot Lv3) and the opponent has three Pokémon, so the
Shift prompt appeared after each KO; `down` + `A` declines it and keeps the Lv100 lead in.

## Kimono Girls: five fights in one cutscene (2026-11-18)

`event-kimono-battles` composed from the base save, party led by a Lv100 Typhlosion with
Earthquake in slot 1 (harness rig: the chain gates each fight on winning). One boot, one A press
per turn, no move-menu navigation:

| Fight | Frame | Proof |
| --- | --- | --- |
| Zuki | 9052 | `You are challenged by Kimono Girl Zuki!`, UMBREON Lv38 |
| Naoko | 13652 | `You are challenged by Kimono Girl Naoko!`, ESPEON Lv38 |
| Miki | 18252 | `You are challenged by Kimono Girl Miki!`, FLAREON Lv38 |
| Sayo | 29108 | `You are challenged by Kimono Girl Sayo!`, JOLTEON Lv38 |
| Kuni | 33708 | `You are challenged by Kimono Girl Kuni!`, VAPOREON Lv38 |

Ending: `Player defeated Kimono Girl Kuni!`, `Ash got ₽4560 for winning!`, `Ash put the Clear Bell
in the KEY ITEMS Pocket.` The intermediate tabs are the script's own: `scr_seq_0928_T27R0501.s`
`_016` calls all five `TrainerBattle`s in one run and only whiteouts on a loss.

Then saved in-game (`X` -> right -> down -> SAVE -> YES -> overwrite) and diffed against the
composed save. melonDS wrote `heartgold.sav` at 6:07:10 PM, SHA-256 `C58D4B23…`; the composed input
was `232D2643…`. The game-written delta, all of it accounted for in the script:

| Field | Before | After |
| --- | --- | --- |
| `VAR_UNK_410C` | 4 | 6 |
| `VAR_UNK_40FA` | 0 | 1 |
| `VAR_BATTLE_RESULT` (0x4013) | 0 | 1 |
| `ITEM_CLEAR_BELL` | 0 | 1 |
| `FLAG_UNK_103` | off | on |
| `FLAG_HIDE_DANCE_STUDIO_KIMONO_GIRLS` | off | on |
| `FLAG_HIDE_BELL_TOWER_SUMMIT_KIMONO_GIRLS` | on | off |
| money | 3,444 | 26,244 |
| `FLAG_UNK_966` | on | off |

`FLAG_UNK_966` is `location warp`'s own dynamic-warp flag, cleared by the game on load; the money
delta is 5 x 4,560, an independent count of the five wins. Details and the temp-var storage
correction are F23 in `state-log.md`.

## How to reproduce

```powershell
$env:MINGW_BIN="<WinLibs>\mingw64\bin"  # for MinGW runtime DLLs (ctypes)
<Player>/.venv/Scripts/python.exe tools/leveld_run.py --rom <soulsilver.nds> `
  --save fixture-out/<id>.sav --outdir leveld-out/<id> --label <id> `
  --frames 700 --shots 700
```

Continue variant adds `--frames 4200 --shots 1500,2400,3300,4200
--inputs '1200:start:8,1800:a:8,2400:a:8,3000:a:8'`.
