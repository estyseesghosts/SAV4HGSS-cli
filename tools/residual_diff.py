"""Byte-diff the active general block of consecutive lineage saves with every region the
CLI already models masked out, to find state the tool (and PKHeX) does not understand.

Masked (general-relative; src/HgssSave.PKHeX/HgssLayout.cs): party 0x98..0x620, bag
0x644..0xDE4, script vars 0xDE4..0x10C4, persistent flags 0x10C4..0x1230. The general block
is 0xF628 bytes; the last 0x14 bytes hold the revision counters/footer and are ignored.
Active partition per save comes from `hgss-save inspect` (blocks.active_general).

usage: residual_diff.py --exe EXE --census census.json --game HG --trainer Master
       [--merge N] [--summary-only]
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

GENERAL_SIZE = 0xF628
MASKED = [(0x98, 0x620), (0x644, 0x10C4 + 0x16C)]  # party; bag+vars+flags (flags end 0x1230)
PARTITION = {"A": 0, "B": 0x40000}


def hms(text: str) -> int:
    h, m, s = (int(p) for p in text.split(":"))
    return h * 3600 + m * 60 + s


def active_general(exe: str, path: str) -> bytes:
    out = subprocess.run([exe, "inspect", path, "--flags-limit", "0"], capture_output=True, text=True, encoding="utf-8").stdout
    part = json.loads(out)["blocks"]["active_general"]
    data = Path(path).read_bytes()
    base = PARTITION[part]
    return data[base:base + GENERAL_SIZE]


def residual_ranges(a: bytes, b: bytes, merge: int) -> list[tuple[int, int]]:
    limit = GENERAL_SIZE - 0x14
    offsets = [i for i in range(limit)
               if a[i] != b[i] and not any(lo <= i < hi for lo, hi in MASKED)]
    ranges: list[list[int]] = []
    for i in offsets:
        if ranges and i - ranges[-1][1] <= merge:
            ranges[-1][1] = i
        else:
            ranges.append([i, i])
    return [(lo, hi) for lo, hi in ranges]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--census", required=True)
    parser.add_argument("--game", required=True)
    parser.add_argument("--trainer", required=True)
    parser.add_argument("--merge", type=int, default=8)
    parser.add_argument("--summary-only", action="store_true")
    opts = parser.parse_args()

    census = json.load(open(opts.census, encoding="utf-8"))
    chain = sorted((s for s in census["saves"] if s["game"] == opts.game and s["trainer"] == opts.trainer),
                   key=lambda s: hms(s["play_time"]))
    blocks = [active_general(opts.exe, s["paths"][0]) for s in chain]
    for (sa, ba), (sb, bb) in zip(zip(chain, blocks), zip(chain[1:], blocks[1:])):
        ranges = residual_ranges(ba, bb, opts.merge)
        total = sum(hi - lo + 1 for lo, hi in ranges)
        print(f"{sa['play_time']} -> {sb['play_time']}: {len(ranges)} ranges, {total} bytes in unmodelled area")
        if not opts.summary_only:
            for lo, hi in ranges:
                print(f"   0x{lo:05X}-0x{hi:05X} ({hi - lo + 1})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
