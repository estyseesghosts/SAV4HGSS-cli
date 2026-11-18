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
  chain, not only `FLAG_UNLOCKED_MT_SILVER` (`docs/state-log.md` F18).
- **Lance's room** (`e4-lance`): the map's frame table runs
  `WalkNormalNorth 6` on entry (`scr_seq_0597_T10R0601_hdr.s:10-14`,
  `scr_seq_0824_T10R0601.s:31-33`), so the player ends at (6,22) even though
  the save's warp tile is (6,28). The scene records that as
  `expect.spawn_after`, and the boot check asserts the primary Location copy
  (`leveld_run.py --spawn-primary`, address `0x227d498`) because a moved
  player leaves the mirrors stale.

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
