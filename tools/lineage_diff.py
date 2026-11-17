"""Diff consecutive saves of one trainer lineage from corpus-census.json.

Per step: flags turned on, flags turned off (these matter: a progress edit that only
adds flags is wrong if the game also clears some), and save-variable changes, all with
registry names where known. Lineage = saves sharing game + trainer name + TID/SID,
ordered by play time.

usage: lineage_diff.py --census census.json --symbols registry.json --game HG --trainer Master [--out file.md]
"""

from __future__ import annotations

import argparse
import json
import sys


def hms(text: str) -> int:
    h, m, s = (int(p) for p in text.split(":"))
    return h * 3600 + m * 60 + s


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--census", required=True)
    parser.add_argument("--symbols", required=True)
    parser.add_argument("--game", required=True)
    parser.add_argument("--trainer", required=True)
    parser.add_argument("--out")
    opts = parser.parse_args()

    census = json.load(open(opts.census, encoding="utf-8"))
    symbols = json.load(open(opts.symbols, encoding="utf-8"))["symbols"]
    flag_names: dict[int, list[str]] = {}
    var_names: dict[int, list[str]] = {}
    for s in symbols:
        target = flag_names if s["kind"] == "persistent_flag" else var_names if s["kind"] == "save_work" else None
        if target is not None:
            target.setdefault(s["id"], []).append(s["name"])

    def fname(i: int) -> str:
        return "/".join(flag_names.get(i, [f"flag#{i}"]))

    def vname(i: int) -> str:
        return "/".join(var_names.get(i, [f"var#0x{i:X}"]))

    chain = sorted(
        (s for s in census["saves"] if s["game"] == opts.game and s["trainer"] == opts.trainer),
        key=lambda s: hms(s["play_time"]))
    lines: list[str] = []
    for before, after in zip(chain, chain[1:]):
        fb, fa = set(before["flags"]), set(after["flags"])
        vb, va = {int(k): v for k, v in before["vars"].items()}, {int(k): v for k, v in after["vars"].items()}
        on, off = sorted(fa - fb), sorted(fb - fa)
        changed = sorted(k for k in set(vb) | set(va) if vb.get(k, 0) != va.get(k, 0))
        lines.append(f"## {before['play_time']} {before['map']} (badges {before['badges_johto']:#06b}/{before['badges_kanto']}) -> "
                     f"{after['play_time']} {after['map']} (badges {after['badges_johto']:#06b}/{after['badges_kanto']})")
        lines.append(f"flags on {len(on)}, off {len(off)}, vars changed {len(changed)}")
        if off:
            lines.append("OFF: " + ", ".join(fname(i) for i in off))
        lines.append("ON: " + ", ".join(fname(i) for i in on))
        lines.append("VARS: " + ", ".join(f"{vname(k)} {vb.get(k, 0)}->{va.get(k, 0)}" for k in changed))
        lines.append("")
    text = "\n".join(lines)
    if opts.out:
        open(opts.out, "w", encoding="utf-8").write(text)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
