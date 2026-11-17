# Level D runtime results — headless melonDS-MCP

Driver: `tools/leveld_run.py` against the melonDS-MCP native core
(`build/mcp-direct/libmelonds_mcp.dll`, MinGW build, JIT OFF, software
render, FreeBIOS + generated firmware, direct boot). Each run stages a
copy of the retail ROM plus the fixture `.sav` under the same basename
(originals untouched), advances frames with `advance_frames`, captures
PNG milestones, and writes `report.json` (save SHA, frame counts,
liveness color counts, ROM identity).

Raw reports/PNGs live in `leveld-out/` (local, not committed).

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

## How to reproduce

```powershell
$env:MINGW_BIN="<WinLibs>\mingw64\bin"  # for MinGW runtime DLLs (ctypes)
<Player>/.venv/Scripts/python.exe tools/leveld_run.py --rom <soulsilver.nds> `
  --save fixture-out/<id>.sav --outdir leveld-out/<id> --label <id> `
  --frames 700 --shots 700
```

Continue variant adds `--frames 4200 --shots 1500,2400,3300,4200
--inputs '1200:start:8,1800:a:8,2400:a:8,3000:a:8'`.
