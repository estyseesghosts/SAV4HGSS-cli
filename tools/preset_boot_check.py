"""Apply every preset to a clean base save and boot it headless; check the live spawn.

For each preset in registry/presets/index.json: `hgss-save preset apply` onto the matching base save,
boot through Continue with tools/leveld_run.py, and require the live Location (ARM9 RAM, found by
--assert-spawn) to equal the warp table tile for the preset's spawn warp (state-log F5). Prints one
line per preset. Base saves and ROMs are only read; outputs go under --work.

usage: preset_boot_check.py --exe EXE --hg-base SAV --ss-base SAV --hg-rom NDS --ss-rom NDS --work DIR
                            --lib DLL --mcp-python DIR [--only NAME,NAME]
"""
import argparse, json, subprocess, sys
from pathlib import Path
HERE = Path(__file__).parent
ap = argparse.ArgumentParser()
for a in ("exe", "hg-base", "ss-base", "hg-rom", "ss-rom", "work", "lib", "mcp-python"): ap.add_argument("--" + a, required=True)
ap.add_argument("--only", default="")
o = ap.parse_args()
reg = HERE.parent / "registry"
idx = json.load(open(reg / "presets" / "index.json"))["presets"]
warps = {(m["map_id"], a["warp_index"]): a for m in json.load(open(reg / "hgss-us.warp-spawns.json"))["maps"] for a in m["warp_arrivals"]}
work = Path(o.work); work.mkdir(parents=True, exist_ok=True)
only = set(filter(None, o.only.split(",")))
fails = 0
for p in idx:
    if only and p["name"] not in only: continue
    base, rom = (o.hg_base, o.hg_rom) if p["game"] == "HG" else (o.ss_base, o.ss_rom)
    sav = work / f"{p['name']}.sav"
    sav.unlink(missing_ok=True)
    r = subprocess.run([o.exe, "preset", "apply", p["name"], base, "--out", str(sav)], capture_output=True, text=True)
    if r.returncode: print(f"{p['name']:14} APPLY FAILED exit {r.returncode}"); fails += 1; continue
    sp = p["spawn"]; w = warps[(sp["map"], sp["warp"])]
    run = work / f"run-{p['name']}"
    subprocess.run([sys.executable, str(HERE / "leveld_run.py"), "--rom", rom, "--save", str(sav), "--outdir", str(run),
                    "--label", p["name"], "--frames", "4200", "--shots", "4200", "--mcp-python", o.mcp_python, "--library", o.lib,
                    "--inputs", "1200:start:8,1800:a:8,2400:a:8,3000:a:8",
                    "--assert-spawn", f"{sp['map']},{w['x']},{w['y']},{w.get('elevation', 0)},{sp['warp']}"],
                   capture_output=True, text=True)
    sc = json.load(open(run / "report.json")).get("spawn_check") or {}
    got = [(h["x"], h["y"]) for h in sc.get("matched", [])]
    ok = (w["x"], w["y"]) in got
    fails += not ok
    print(f"{p['name']:14} spawn map {sp['map']} warp {sp['warp']} want ({w['x']},{w['y']}) live {got} {'OK' if ok else 'MISMATCH'}")
sys.exit(1 if fails else 0)
