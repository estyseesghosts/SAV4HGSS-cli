"""Print the LocalFieldData of every save in a census: five Locations, music, weather, spawn.

Layout (general-relative, docs/archive/state-log.md F4): Location {i32 mapId, warpId, x, y, direction}
at 0x1234 + 20*n for n = 0 current, 1 entrance, 2 previous, 3 dynamicWarp, 4 specialSpawn;
u16 musicId 0x1298, u16 weather 0x129A, u16 lastSpawn 0x129C. Also reports flag 0x966.

usage: field_locations.py --exe EXE --census census.json --symbols registry.json
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import residual_diff as rd  # noqa: E402

NAMES = ["CUR", "ENTR", "PREV", "DYN", "SPAWN"]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--census", required=True)
    parser.add_argument("--symbols", required=True)
    opts = parser.parse_args()
    census = json.load(open(opts.census, encoding="utf-8"))
    maps = {s["id"]: s["name"] for s in json.load(open(opts.symbols, encoding="utf-8"))["symbols"] if s["kind"] == "map"}
    for save in sorted(census["saves"], key=lambda s: (s["game"], s["trainer"], rd.hms(s["play_time"]))):
        g = rd.active_general(opts.exe, save["paths"][0])
        parts = []
        for n, label in enumerate(NAMES):
            m, w, x, y, d = struct.unpack_from("<iiiii", g, 0x1234 + 20 * n)
            parts.append(f"{label} {maps.get(m, m)[4:22]} w{w} ({x},{y}) d{d}")
        music, weather, last = struct.unpack_from("<HHH", g, 0x1298)
        flag = " F966" if 0x966 in save["flags"] else ""
        print(f"{save['game']} {save['trainer'][:8]:8} {save['play_time']:>8}{flag} | " + " | ".join(parts) + f" | mus {music} wx {weather} last {last}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
