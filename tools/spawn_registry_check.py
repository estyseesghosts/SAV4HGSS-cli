"""Cross-check the warp-arrival registry against the hand-verified safe spawns.

Why: fixtures/safe-spawns.json tiles are positions real played saves stood on,
so they are ground truth collected independently of the decomp zone-event
files. If the registry (tools/zone_spawn_index.py) reproduces them, the
derivation is right; if it disagrees on elevation or misses a tile, the
registry can't be trusted as a spawn verdict and `location set` must keep
refusing it.

Reads only, writes nothing. Exit 0 only when every safe spawn is explained.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def load(path: Path) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    fixtures = load(repo / "fixtures" / "safe-spawns.json")
    registry_path = repo / "registry" / "hgss-us.warp-spawns.json"
    if not registry_path.exists():
        print(f"missing registry: {registry_path}; run tools/zone_spawn_index.py")
        return 2
    registry = load(registry_path)
    maps = {m["map_id"]: m for m in registry["maps"]}

    explained = 0
    problems: list[str] = []
    for spawn in fixtures["spawns"]:
        map_id = spawn["map_id"]
        x, y, z = spawn["x"], spawn["y"], spawn.get("z", 0)
        entry = maps.get(map_id)
        if entry is None:
            problems.append(f"{map_id} ({x},{y},z{z}): map absent from registry headers")
            continue

        warps = [w for w in entry["warp_arrivals"] if w["x"] == x and w["y"] == y]
        doors = [d for d in entry["door_arrivals"] if d["x"] == x and d["y"] == y]
        hits = warps + doors
        if not hits:
            problems.append(
                f"{spawn.get('map_symbol', map_id)} {map_id} ({x},{y},z{z}): "
                f"not a warp or door arrival in {entry['zone_event_file']}")
            continue

        elevations = sorted({w.get("elevation") for w in warps})
        if z not in elevations:
            problems.append(
                f"{spawn.get('map_symbol', map_id)} {map_id} ({x},{y}): tile is a registry arrival "
                f"but save z={z} disagrees with zone-event elevation {elevations}")
            continue
        explained += 1
        kinds = " ".join(h["evidence"] for h in hits)
        print(f"ok  {spawn.get('map_symbol', map_id):38} map {map_id:3} ({x},{y}) z{z}  <- {kinds}")

    print(f"\nsafe spawns explained by registry: {explained}/{len(fixtures['spawns'])}")
    for problem in problems:
        print(f"miss {problem}")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
