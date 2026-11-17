"""Which maps and gyms are reachable from New Bark with a given set of field-move gates.

Model (all from the decomp or the ROM; limits are listed in the output):
  * Overworld = map matrix 0 (EVERYWHERE), 32x32 tiles per block, attributes from land_data (tools/map_data.py).
    Tile attribute bit 15 = blocked, low byte = metatile behaviour.
  * Surfable water needs SURF, whirlpool/waterfall/rock-climb behaviours need their move, SPRITE_TREE objects need CUT,
    SPRITE_BREAKROCK needs ROCK SMASH, SPRITE_ROCK needs STRENGTH. Object blockers only count while their event flag is
    NOT in the starting flag set (objects whose eventFlag is FLAG_NOTHING always count).
  * One-way ledges: a JUMP_* tile is entered only moving in its direction.
  * A warp tile is passable; the warp leads to its destination (zone_event "header" + "anchor" = destination warp index).
    Interior maps (matrix != 0) are treated as fully connected between their own warps.
  * NOT modelled: NPCs that block a path by script (Sudowoodo, guards), time/event gates on warps, HM move availability
    other than the badge gate, Fly, one-way scripted cutscene movement, interior puzzles.
usage: map_reach.py --decomp DIR --rom NDS [--json OUT]
"""
from __future__ import annotations

import argparse
import itertools
import json
import re
import sys
from collections import deque
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import map_data  # noqa: E402

GATES = ["cut", "surf", "strength", "smash", "waterfall", "whirlpool", "climb"]
GYMS = {  # leader room map symbol -> badge id (badge.h)
    "MAP_VIOLET_GYM": 0, "MAP_AZALEA_GYM": 1, "MAP_GOLDENROD_GYM": 2, "MAP_ECRUTEAK_GYM": 3, "MAP_CIANWOOD_GYM": 4,
    "MAP_OLIVINE_GYM": 5, "MAP_MAHOGANY_GYM_LEADER_ROOM": 6, "MAP_BLACKTHORN_GYM": 7,
    "MAP_PEWTER_GYM": 8, "MAP_CERULEAN_GYM": 9, "MAP_VERMILION_GYM": 10, "MAP_CELADON_GYM": 11, "MAP_SAFFRON_GYM": 12,
    "MAP_FUCHSIA_GYM": 13, "MAP_SEAFOAM_ISLANDS_CINNABAR_GYM": 14, "MAP_VIRIDIAN_GYM": 15,
}
START = "MAP_NEW_BARK_PLAYER_HOUSE_2F"


def parse_headers(decomp: Path):
    text = (decomp / "src/data/map_headers.h").read_text(encoding="utf-8")
    out = {}
    for m in re.finditer(r"\[(MAP_\w+)\]\s*=\s*\{(.*?)\n\s*\},", text, re.S):
        body = m.group(2)
        mx = re.search(r"\.matrixId\s*=\s*NARC_map_matrix_map_matrix_(\d+)", body)
        ev = re.search(r"\.eventsBank\s*=\s*NARC_zone_event_(\d+)_", body)
        out[m.group(1)] = (int(mx.group(1)) if mx else None, int(ev.group(1)) if ev else None)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--decomp", required=True)
    ap.add_argument("--rom", required=True)
    ap.add_argument("--json")
    ap.add_argument("--open", default="", help="behaviour names whose blocked bit is ignored (calibration)")
    ap.add_argument("--probe", default="", help="x,z of a tile to test reachability for with all gates")
    ap.add_argument("--flags", default="", help="comma list of flag names assumed set (hides objects with that eventFlag)")
    o = ap.parse_args()
    decomp = Path(o.decomp)
    names, surf = map_data.behaviours(decomp)
    inv = {v: k[len("TILE_BEHAVIOR_"):] for k, v in names.items()}
    land = map_data.load_land(o.rom)
    w, h, _hd, models = map_data.load_matrix(decomp, 0)
    grid = map_data.tile_grid(land, w, h, models)
    headers = parse_headers(decomp)
    zdir = decomp / "files/fielddata/eventdata/zone_event"
    zfiles = {int(p.name.split("_", 1)[0]): p for p in zdir.glob("*.json")}
    hidden = set(filter(None, o.flags.split(",")))
    open_b = set(filter(None, o.open.split(",")))

    zones = {}
    for sym, (mx, ev) in headers.items():
        if ev in zfiles:
            zones[sym] = json.load(open(zfiles[ev], encoding="utf-8"))
    overworld = {s for s, (mx, _e) in headers.items() if mx == 0 and s in zones}

    # warp tables: (map, index) -> (x, z)
    warp_at = {}   # overworld (x, z) -> (dest sym, anchor)
    interior_warps = {}  # interior map -> list of (dest sym, anchor)
    arrival = {}   # (sym, idx) -> (x, z)
    for sym, z in zones.items():
        for i, wp in enumerate(z.get("warps", [])):
            arrival[(sym, i)] = (wp["x"], wp["z"])
            if sym in overworld:
                warp_at[(wp["x"], wp["z"])] = (wp["header"], wp["anchor"])
            else:
                interior_warps.setdefault(sym, []).append((wp["header"], wp["anchor"]))

    # object blockers on the overworld
    need_obj = {}  # (x, z) -> gate
    kind = {"SPRITE_TREE": "cut", "SPRITE_BREAKROCK": "smash", "SPRITE_ROCK": "strength"}
    for sym in overworld:
        for ob in zones[sym].get("objects", []):
            g = kind.get(ob.get("spriteId"))
            if g and (ob["eventFlag"] == "FLAG_NOTHING" or ob["eventFlag"] not in hidden):
                need_obj[(ob["x"], ob["z"])] = g

    jump = {"JUMP_NORTH": (0, -1), "JUMP_SOUTH": (0, 1), "JUMP_EAST": (1, 0), "JUMP_WEST": (-1, 0)}
    move_gate = {"WHIRLPOOL": "whirlpool", "WATERFALL": "waterfall", "ROCK_CLIMB_NORTH_SOUTH": "climb", "ROCK_CLIMB_EAST_WEST": "climb"}

    def tile_gate(attr: int):
        b = attr & 0xFF
        name = inv.get(b, str(b))
        if name in move_gate:
            return move_gate[name], True
        if b in surf:
            return "surf", True
        return None, (not (attr & 0x8000)) or name in open_b

    def entry_of(sym_dest: str, anchor: int):
        if sym_dest in overworld:
            return ("tile", arrival[(sym_dest, anchor)]) if (sym_dest, anchor) in arrival else None
        return ("map", sym_dest)

    def reach(gates: frozenset[str]):
        seen_t, seen_m = set(), set()
        q = deque()
        start_map = START

        def push_map(m):
            if m in seen_m:
                return
            seen_m.add(m)
            for dest, anchor in interior_warps.get(m, []):
                e = entry_of(dest, anchor)
                if e is None:
                    continue
                if e[0] == "tile":
                    push_tile(e[1])
                else:
                    push_map(e[1])

        def push_tile(t):
            if t not in seen_t:
                seen_t.add(t)
                q.append(t)

        push_map(start_map)
        while q:
            x, z = q.popleft()
            wp = warp_at.get((x, z))
            if wp:
                e = entry_of(*wp)
                if e:
                    push_tile(e[1]) if e[0] == "tile" else push_map(e[1])
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (x + dx, z + dz)
                a = grid.get(n)
                if a is None:
                    continue
                nm = inv.get(a & 0xFF, "")
                if nm in jump:
                    if (dx, dz) != jump[nm]:
                        continue
                    n = (n[0] + dx, n[1] + dz)
                    if n not in grid:
                        continue
                    a = grid[n]
                if n in warp_at:
                    push_tile(n)
                    continue
                g, ok = tile_gate(a)
                if g is not None and g not in gates:
                    continue
                if g is None and not ok:
                    continue
                og = need_obj.get(n)
                if og and og not in gates:
                    continue
                push_tile(n)
        return seen_t, seen_m

    if o.probe:
        px, pz = map(int, o.probe.split(","))
        t, _m = reach(frozenset(GATES))
        print("probe", (px, pz), (px, pz) in t, "tiles", len(t))
        return 0
    report = {"schema": "hgss-save.reachability/v1", "start": START, "gates": GATES, "results": []}
    first = {}
    for k in range(len(GATES) + 1):
        for combo in itertools.combinations(GATES, k):
            gs = frozenset(combo)
            _t, ms = reach(gs)
            got = sorted(m for m in GYMS if m in ms)
            report["results"].append({"gates": sorted(gs), "gyms": got, "tiles": len(_t)})
            for m in got:
                first.setdefault(m, []).append(sorted(gs))
    minimal = {}
    for m, sets in first.items():
        mins = [s for s in sets if not any(set(t) < set(s) for t in sets)]
        minimal[m] = mins
    report["minimal_gates_to_reach_gym"] = {f"{m} (badge {GYMS[m]})": v for m, v in sorted(minimal.items(), key=lambda kv: GYMS[kv[0]])}
    report["unreachable_gyms"] = sorted(set(GYMS) - set(minimal))
    if o.json:
        Path(o.json).write_text(json.dumps(report, indent=1), encoding="utf-8")
    for k, v in report["minimal_gates_to_reach_gym"].items():
        print(k, v)
    print("never reachable:", report["unreachable_gyms"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
