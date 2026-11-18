"""Which flags actually move between consecutive HG milestones, named and unnamed.

Answers the open item in docs/archive/state-log.md F6/F2: "identify the FLAG_UNK_* ids that actually
change across milestones". A flag that changes is story state the preset and the scene layer must
carry; a flag that never changes across the 11-save HG lineage cannot be told apart from a constant.

usage: unk_flags.py [--census PATH] [--symbols PATH] [--game HG] [--trainer Master]
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

LINEAGE = ["1gyn", "2gyn", "3gyn", "4gyn", "5gyn", "6gyn", "7gyn", "8gyn", "E4.SAV", "posE4", "Celebi"]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--census", default=str(ROOT / "research" / "corpus-census.json"))
    ap.add_argument("--symbols", default=str(ROOT / "registry" / "hgss-us.symbols.json"))
    ap.add_argument("--game", default="HG")
    ap.add_argument("--trainer", default="Master")
    ap.add_argument("--json", action="store_true")
    opts = ap.parse_args()

    census = json.load(open(opts.census, encoding="utf-8"))["saves"]
    symbols = json.load(open(opts.symbols, encoding="utf-8"))["symbols"]
    name = {s["id"]: s["name"] for s in symbols if s["kind"] == "persistent_flag"}

    milestones = []
    for tag in LINEAGE:
        for save in census:
            if save["game"] != opts.game or save["trainer"] != opts.trainer:
                continue
            if any(tag in p for p in save["paths"]):
                milestones.append((tag, set(save["flags"])))
                break

    steps = []
    for (prev_tag, prev), (tag, cur) in zip(milestones, milestones[1:]):
        gained = cur - prev
        lost = prev - cur
        changed = [(fid, name.get(fid, f"FLAG_UNK_{fid:X}"), fid in gained) for fid in sorted(gained | lost)]
        unnamed = [(fid, sym, on) for fid, sym, on in changed if re.search(r"UNK", sym)]
        steps.append({"from": prev_tag, "to": tag, "gained": len(gained), "lost": len(lost),
                      "changed": len(changed), "unnamed": unnamed})
        if not opts.json:
            print(f"{prev_tag:8} -> {tag:8} {len(gained):3} gained {len(lost):3} lost "
                  f"({len(unnamed)} unnamed)")
            for fid, sym, on in unnamed:
                print(f"    {'+' if on else '-'} {sym} (0x{fid:X})")
            print()

    if opts.json:
        print(json.dumps({"schema": "hgss-save.flag-deltas/v1", "game": opts.game,
                          "trainer": opts.trainer, "milestones": [t for t, _ in milestones],
                          "steps": steps}, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
