"""Catalogue every raw HG/SS save in a directory tree through the hgss-save CLI.

Writes one JSON file: per save sha256, game, trainer, badges, map, playtime and the
complete set of persistent flags and non-zero save variables. Read-only on the saves.
Non-HGSS files (exit 3) are recorded as skipped. Duplicate content (same sha256) is
collapsed and every path that carried it is listed.

usage: corpus_census.py --exe hgss-save.exe --saves DIR [--saves DIR ...] --out census.json
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

RAW_SIZE = 0x80000


def run(exe: str, *args: str) -> tuple[int, dict | None]:
    proc = subprocess.run([exe, *args], capture_output=True, text=True, encoding="utf-8")
    try:
        return proc.returncode, json.loads(proc.stdout)
    except json.JSONDecodeError:
        return proc.returncode, None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--saves", action="append", required=True)
    parser.add_argument("--out", required=True)
    opts = parser.parse_args()

    candidates: list[Path] = []
    for root in opts.saves:
        for path in sorted(Path(root).rglob("*")):
            if path.is_file() and path.stat().st_size == RAW_SIZE and path.suffix.lower() in (".sav", ".bin", ".sa"):
                candidates.append(path)

    by_sha: dict[str, dict] = {}
    skipped: list[dict] = []
    for path in candidates:
        code, info = run(opts.exe, "inspect", str(path), "--flags-limit", "0")
        if info is None or code not in (0, 4) or info.get("operation") != "inspect":
            skipped.append({"path": str(path), "exit": code,
                            "reason": (info or {}).get("message", "no json")})
            continue
        sha = info["input"]["sha256"]
        if sha in by_sha:
            by_sha[sha]["paths"].append(str(path))
            continue
        _, flags = run(opts.exe, "flags", "list", str(path), "--limit", "100000")
        _, variables = run(opts.exe, "vars", "list", str(path), "--limit", "100000")
        trainer, loc = info["trainer"], info["location"]
        by_sha[sha] = {
            "sha256": sha,
            "paths": [str(path)],
            "game": info["format"]["game"],
            "structural": info["validation"]["structural"],
            "trainer": trainer["name"], "tid": trainer["tid"], "sid": trainer["sid"],
            "money": trainer["money"],
            "badges_johto": trainer["badges_johto"], "badges_kanto": trainer["badges_kanto"],
            "play_time": trainer["play_time"],
            "map_id": loc["map_id"], "map": loc["map_symbol"], "x": loc["x"], "y": loc["y"], "z": loc["z"],
            "dex_seen": info["dex"]["seen"], "dex_caught": info["dex"]["caught"],
            "party": [s["species_name"] for s in info["party"]["slots"] if s["present"]],
            "flags": [f["id"] for f in flags["flags"]["set"]] if flags else None,
            "vars": {str(v["id"]): v["value"] for v in variables["variables"]["values"]} if variables else None,
        }

    Path(opts.out).write_text(json.dumps(
        {"saves": sorted(by_sha.values(), key=lambda s: s["paths"][0]), "skipped": skipped},
        indent=1), encoding="utf-8")
    print(f"{len(by_sha)} unique HGSS saves, {len(skipped)} skipped, from {len(candidates)} candidates")
    return 0


if __name__ == "__main__":
    sys.exit(main())
