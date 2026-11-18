"""Compose and boot every scene in registry/hgss-us.scenes.json.

For each scene: `hgss-save story scene apply` onto the clean base save, then boot the
result headless with tools/leveld_run.py and require the live Location (ARM9 RAM scan)
to be the scene's warp tile. Writes work/summary.json plus one screenshot per scene.

usage: scene_boot_check.py --exe EXE --base SAV --rom NDS --work DIR
                           --library DLL [--mcp-python DIR] [--only ID,ID]
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REG = HERE.parent / "registry"


def main() -> int:
    ap = argparse.ArgumentParser()
    for name in ("exe", "base", "rom", "work", "library"):
        ap.add_argument("--" + name, required=True)
    ap.add_argument("--base-ss", default="",
                    help="SoulSilver base save; scenes whose game is SS are composed onto it.")
    ap.add_argument("--rom-ss", default="",
                    help="SoulSilver ROM; scenes whose game is SS boot against it (a save only loads in its own game).")
    ap.add_argument("--mcp-python", default="")
    ap.add_argument("--only", default="")
    ap.add_argument("--frames", type=int, default=4400)
    ap.add_argument("--inputs", default="1200:start:5,1500:a:10,2000:a:10,2500:a:10")
    opts = ap.parse_args()

    scenes = json.load(open(REG / "hgss-us.scenes.json", encoding="utf-8"))["scenes"]
    only = {s for s in opts.only.split(",") if s}
    work = Path(opts.work)
    saves = work / "saves"
    outs = work / "runs"
    saves.mkdir(parents=True, exist_ok=True)
    outs.mkdir(parents=True, exist_ok=True)

    results = []
    for scene in scenes:
        sid = scene["id"]
        if only and sid not in only:
            continue
        base = opts.base
        rom = opts.rom
        if scene.get("game") == "SS":
            if not opts.base_ss:
                results.append({"id": sid, "scene_ok": False, "stage": "apply",
                                "error": "scene is SS but --base-ss was not given"})
                print(f"{sid:34} NO SS BASE", flush=True)
                continue
            if not opts.rom_ss:
                results.append({"id": sid, "scene_ok": False, "stage": "boot",
                                "error": "scene is SS but --rom-ss was not given"})
                print(f"{sid:34} NO SS ROM", flush=True)
                continue
            base = opts.base_ss
            rom = opts.rom_ss
        expect = scene["expect"]
        save = saves / f"{sid}.sav"
        cmd = [opts.exe, "story", "scene", "apply", sid, base, "--out", str(save), "--force"]
        apply = subprocess.run(cmd, capture_output=True, text=True)
        if apply.returncode != 0:
            results.append({"id": sid, "scene_ok": False, "stage": "apply", "error": apply.stderr.strip()[-400:]})
            print(f"{sid:34} APPLY FAIL", flush=True)
            continue
        report = json.loads(apply.stdout)
        if not report.get("passed"):
            results.append({"id": sid, "scene_ok": False, "stage": "audit",
                            "error": [c for c in report["checks"] if not c["ok"]]})
            print(f"{sid:34} AUDIT FAIL", flush=True)
            continue

        outdir = outs / sid
        assert_x, assert_y = expect["x"], expect["y"]
        extra: list[str] = []
        after = expect.get("spawn_after")
        if after:
            assert_x, assert_y = after["x"], after["y"]
            extra = ["--spawn-primary"]
        boot = [sys.executable, str(HERE / "leveld_run.py"), "--rom", rom, "--save", str(save),
                "--outdir", str(outdir), "--stage", str(work / "stage"), "--label", sid,
                "--frames", str(opts.frames),
                "--shots", str(opts.frames), "--inputs", opts.inputs,
                "--assert-spawn", f"{expect['map']},{assert_x},{assert_y},0,{expect['warp']}"] + extra
        env = dict(os.environ)
        env["MELONDS_MCP_LIB"] = opts.library
        if opts.mcp_python:
            env["MCP_PYTHON"] = opts.mcp_python
        run = subprocess.run(boot, capture_output=True, text=True, env=env, cwd=str(HERE.parent))
        boot_report = {}
        rp = outdir / "report.json"
        if rp.exists():
            boot_report = json.load(open(rp, encoding="utf-8"))
        ok = run.returncode == 0 and boot_report.get("ok", False)
        entry = {
            "id": sid,
            "title": scene["title"],
            "kind": scene["kind"],
            "base_preset": scene["base_preset"],
            "game": scene.get("game"),
            "rom": Path(rom).name,
            "scene_ok": True,
            "boot_ok": ok,
            "map": expect["map"],
            "x": expect["x"],
            "y": expect["y"],
            "spawn_after": expect.get("spawn_after"),
            "warp": expect["warp"],
            "badges_johto": report["badges_johto"],
            "badges_kanto": report["badges_kanto"],
            "violations": report["violation_count"],
            "spawn_hits": len(boot_report.get("spawn_check", {}).get("hits", [])),
            "spawn_matched": len(boot_report.get("spawn_check", {}).get("matched", [])),
            "mini": boot_report.get("milestones", [{}])[-1].get("png", "") if boot_report.get("milestones") else "",
            "error": boot_report.get("error", ""),
        }
        results.append(entry)
        print(f"{sid:34} {'ok' if ok else 'BOOT FAIL':9} map={entry['map']} ({entry['x']},{entry['y']}) warp={entry['warp']}", flush=True)

    summary = {"schema": "hgss-save.scene-boot/v1", "base": opts.base, "rom": opts.rom,
               "base_ss": opts.base_ss, "rom_ss": opts.rom_ss,
               "count": len(results), "passed": sum(1 for r in results if r.get("boot_ok")),
               "results": results}
    (work / "summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print(f'{summary["passed"]}/{summary["count"]} scenes booted at the expected tile', flush=True)
    return 0 if summary["passed"] == summary["count"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
