"""Runtime-test the flag-0x966 + dynamicWarp spawn mechanism (docs/state-log.md F5).

For each target (map, warp index) from registry/hgss-us.warp-spawns.json: copy the base
save with tools/save_edit.py (dynamic warp = {map, warp, x=0, y=0, dir=1}, flag 0x966 set;
x/y are deliberately wrong so only the game's own warp resolution can produce the right
tile), boot it headless through Continue with tools/leveld_run.py, and look for the live
Location {map, warp, x, y} in RAM. A target passes when exactly the player copy shows the
registry tile.

usage: dynwarp_probe.py --exe hgss-save.exe --base BASE.sav --rom X.nds --work DIR
       --lib libmelonds_mcp.dll --mcp-python DIR [--targets 73:2,75:0 | --sample N]
Runs on copies under --work; the base save and ROM are only read.
"""

from __future__ import annotations

import argparse
import json
import random
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).parent
INPUTS = "1200:start:8,1800:a:8,2400:a:8,3000:a:8"


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--exe", required=True)
    p.add_argument("--base", required=True)
    p.add_argument("--rom", required=True)
    p.add_argument("--work", required=True)
    p.add_argument("--lib", required=True)
    p.add_argument("--mcp-python", required=True)
    p.add_argument("--registry", default=str(HERE.parent / "registry" / "hgss-us.warp-spawns.json"))
    p.add_argument("--targets", default="")
    p.add_argument("--sample", type=int, default=0)
    p.add_argument("--seed", type=int, default=1)
    o = p.parse_args()

    maps = {m["map_id"]: m for m in json.load(open(o.registry, encoding="utf-8"))["maps"]}
    if o.targets:
        targets = [tuple(int(v) for v in t.split(":")) for t in o.targets.split(",")]
    else:
        pool = [(m["map_id"], a["warp_index"]) for m in maps.values() for a in m["warp_arrivals"]]
        targets = random.Random(o.seed).sample(pool, o.sample)

    work = Path(o.work)
    work.mkdir(parents=True, exist_ok=True)
    results = []
    for map_id, warp in targets:
        entry = maps[map_id]
        arrival = next(a for a in entry["warp_arrivals"] if a["warp_index"] == warp)
        want = (arrival["x"], arrival["y"])
        save = work / f"probe-{map_id}-{warp}.sav"
        if save.exists():
            save.unlink()
        subprocess.run([sys.executable, str(HERE / "save_edit.py"), o.base, str(save), "--exe", o.exe,
                        "--dynwarp", str(map_id), str(warp), "0", "0", "1", "--flag-966"], check=True, capture_output=True)
        out = work / f"run-{map_id}-{warp}"
        subprocess.run([sys.executable, str(HERE / "leveld_run.py"), "--rom", o.rom, "--save", str(save), "--outdir", str(out),
                        "--label", save.stem, "--frames", "4200", "--shots", "4200", "--inputs", INPUTS,
                        "--assert-spawn", f"{map_id},{want[0]},{want[1]},0,{warp}",
                        "--mcp-python", o.mcp_python, "--library", o.lib], capture_output=True, env={**__import__('os').environ})
        report = json.load(open(out / "report.json", encoding="utf-8"))
        check = report.get("spawn_check", {})
        live = [h for h in check.get("hits", []) if (h["x"], h["y"]) != (0, 0)]
        ok = len(check.get("matched", [])) == 1 and live and (live[0]["x"], live[0]["y"]) == want
        row = {"map": map_id, "symbol": entry["map_symbol"], "map_type": entry["map_type"], "warp": warp,
               "expected": want, "live_hits": live, "pass": bool(ok)}
        results.append(row)
        print(json.dumps(row))
    print(f"{sum(r['pass'] for r in results)}/{len(results)} passed")
    return 0 if all(r["pass"] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
