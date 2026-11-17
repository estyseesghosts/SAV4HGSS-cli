"""Build the warp-arrival spawn registry from the pinned decomp tree.

Why this exists: `location.set` refused every tile except the hand-verified
`fixtures/safe-spawns.json` entries because per-tile walkability lives in
compiled binaries with no decompiled source (docs/coverage-gaps.md). A warp
arrival needs no walkability grid: the game itself places the player on a
destination map's warp tile, so that tile is walkable by construction.

Evidence chain, all from pret/pokeheartgold@4e95633 (local checkout):
  * `include/map_header.h:13-42`  MapHeader, including `eventsBank`,
    `flyAllowed`, `mapType`, `regionNo`, `matrixId`.
  * `src/map_header.c:69-105`     MapHeader_GetEventsBank et al. read the
    static sMapHeaders table generated from `src/data/map_headers.h`.
  * `include/constants/maps.h`    numeric MAP_* ids (the save's map field).
  * `files/fielddata/eventdata/zone_event/<bank>_<tag>.json` the per-bank
    event list, keyed by the same numeric bank as `eventsBank`.
  * warp reciprocity, `src/map_object.c` + `include/field/fieldmap.h`
    WarpEvent semantics: warp i of map A names {header: B, anchor: j}, and
    warp j of map B names {header: A, anchor: i}; entering B through warp j
    puts the player on B's warp j tile (json `x`,`z`; `z` is the tile row,
    matching the decomp MapPos convention).

The registry is a candidate list, not a verdict: `fixture verify` and the
runtime driver still have to prove each spawn boots and lands. Emitters
record provenance so a reviewer can trace any entry to a file and line.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
from pathlib import Path

MAP_DEFINE = re.compile(r"#define\s+(MAP_[A-Z0-9_]+)\s+(\d+)\s*//\s*(\S+)")
HEADER_BLOCK = re.compile(r"\[([A-Z0-9_]+)\]\s*=\s*\{")
FIELD = re.compile(r"\.(areaIcon|areaDataBank|battleBg|bikeAllowed|cameraType|"
                   r"dayMusicId|eventsBank|escapeRopeAllowed|followMode|flyAllowed|"
                   r"incomingCalls|mapType|mapsec|matrixId|momCallIntroParam|"
                   r"moveModelBank|msgBank|nightMusicId|outgoingCalls|radioSignal|"
                   r"regionNo|runningAllowed_Unused|scriptHeaderBank|scriptsBank|"
                   r"weather|wildEncounterBank|worldMapX|worldMapY)\s*=\s*([^,\n]+)")


def parse_map_ids(decomp: Path) -> dict[str, int]:
    ids = {}
    for name, num, _tag in MAP_DEFINE.findall(
            read(decomp / "include/constants/maps.h")):
        ids[name] = int(num)
    return ids


def read(path: Path) -> str:
    with open(path, encoding="utf-8", errors="replace") as handle:
        return handle.read()


ZONE_EVENT_NARC = re.compile(r"NARC_zone_event_(\d+)_\w+_bin")
TRUE_FALSE = {"TRUE": True, "FALSE": False}
MAP_TYPE = {
    "MAP_TYPE_INVALID": 0, "MAP_TYPE_CITY_TOWN": 1, "MAP_TYPE_ROUTE": 2,
    "MAP_TYPE_CAVE": 3, "MAP_TYPE_INTERIOR": 4, "MAP_TYPE_POKEMON_CENTER": 5,
    "MAP_TYPE_UNDERGROUND": 6,
}
MAP_REGION = {"MAP_REGION_JOHTO": 0, "MAP_REGION_KANTO": 1}
BOOL_FIELDS = {"bikeAllowed", "escapeRopeAllowed", "flyAllowed", "incomingCalls",
               "outgoingCalls", "radioSignal", "regionNo", "runningAllowed_Unused"}


def scalar(value: str):
    """Resolve the header literal: bare int, TRUE/FALSE, MAP_TYPE_*, or the
    numeric bank embedded in a NARC_* symbol name."""
    value = value.strip()
    if value in TRUE_FALSE:
        return TRUE_FALSE[value]
    if value in MAP_TYPE:
        return MAP_TYPE[value]
    if value in MAP_REGION:
        return MAP_REGION[value]
    if re.fullmatch(r"-?\d+", value):
        return int(value)
    match = ZONE_EVENT_NARC.fullmatch(value)
    if match:
        return int(match.group(1))
    digits = re.search(r"_(\d{3,4})_", value)
    return int(digits.group(1)) if digits else value


def parse_map_headers(decomp: Path, ids: dict[str, int]) -> dict[int, dict]:
    """Return numeric map id -> header fields, in table order."""
    src = read(decomp / "src/data/map_headers.h")
    headers: dict[int, dict] = {}
    for match in HEADER_BLOCK.finditer(src):
        name = match.group(1)
        if name not in ids:
            continue
        start = match.end()
        end = src.find("},", start)
        body = src[start:end if end > 0 else start + 4000]
        fields = {k: scalar(v) for k, v in FIELD.findall(body)}
        headers[ids[name]] = {"map_symbol": name, **fields}
    return headers


def bank_tag(path: Path) -> tuple[int, str]:
    stem = path.stem
    bank, _, tag = stem.partition("_")
    return int(bank), tag


def load_zone_events(decomp: Path) -> dict[int, dict]:
    events: dict[int, dict] = {}
    for path in sorted(glob.glob(str(decomp / "files/fielddata/eventdata/zone_event/*.json"))):
        p = Path(path)
        bank, tag = bank_tag(p)
        try:
            doc = json.loads(read(p))
        except json.JSONDecodeError:
            continue
        events[bank] = {"file": p.name, "tag": tag, "doc": doc}
    return events


def facing_for(warp_entry: dict, dest: dict, dest_symbol: str) -> str | None:
    """Direction the player faces on arrival is not encoded in the warp pair,
    so it is deliberately not invented; callers must treat it as unknown."""
    return None


def build(decomp: Path) -> dict:
    ids = parse_map_ids(decomp)
    headers = parse_map_headers(decomp, ids)
    events = load_zone_events(decomp)

    maps: dict[int, dict] = {}
    for map_id, header in sorted(headers.items()):
        bank = header.get("eventsBank")
        entry = {
            "map_id": map_id,
            "map_symbol": header["map_symbol"],
            "events_bank": bank,
            "map_type": header.get("mapType"),
            "region": header.get("regionNo"),
            "fly_allowed": header.get("flyAllowed"),
            "escape_rope_allowed": header.get("escapeRopeAllowed"),
            "bike_allowed": header.get("bikeAllowed"),
            "zone_event_file": None,
            "warp_arrivals": [],
            "door_arrivals": [],
        }
        if bank is not None and str(bank).isdigit():
            ev = events.get(int(bank))
            if ev is not None:
                entry["zone_event_file"] = ev["file"]
                for idx, warp in enumerate(ev["doc"].get("warps", []) or []):
                    dest = warp.get("header")
                    dest_id = ids.get(dest) if isinstance(dest, str) else None
                    entry["warp_arrivals"].append({
                        "warp_index": idx,
                        "x": warp.get("x"),
                        "y": warp.get("z"),
                        "elevation": warp.get("y"),
                        "destination_symbol": dest if isinstance(dest, str) else None,
                        "destination_map_id": dest_id,
                        "destination_anchor": warp.get("anchor"),
                        "evidence": f"{ev['file']} warps[{idx}]",
                    })
                # bgs type 1 is a scripted door tile: standing on it warps the
                # player, so it is a tile the player legitimately occupies.
                for idx, bg in enumerate(ev["doc"].get("bgs", []) or []):
                    if bg.get("type") == 1:
                        entry["door_arrivals"].append({
                            "bg_index": idx,
                            "x": bg.get("x"),
                            "y": bg.get("z"),
                            "script": bg.get("scriptId"),
                            "evidence": f"{ev['file']} bgs[{idx}] type 1",
                        })
        maps[map_id] = entry
    return {
        "schema": "hgss-save.warp-spawns/v1",
        "source": "pret/pokeheartgold",
        "commit": os.environ.get("HGSS_DECOMP_COMMIT", "4e9563397ab4e5c407271472935f9e270ac6a804"),
        "semantics": {
            "y": "the zone-event json key 'z' is the tile row the player stands on; json 'y' is elevation",
            "warp_arrival": "entering this map through warp_index lands the player on (x, y); reciprocal pair per WarpEvent semantics",
            "door_arrival": "a scripted door tile (bgs type 1) the player can stand on",
            "facing": "not encoded in warp pairs; never invented",
        },
        "maps": [maps[k] for k in sorted(maps)],
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--decomp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--commit", default="4e9563397ab4e5c407271472935f9e270ac6a804")
    args = ap.parse_args()
    os.environ["HGSS_DECOMP_COMMIT"] = args.commit
    doc = build(Path(args.decomp))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)
    with_warp = sum(1 for m in doc["maps"] if m["warp_arrivals"])
    total = sum(len(m["warp_arrivals"]) for m in doc["maps"])
    doors = sum(len(m["door_arrivals"]) for m in doc["maps"])
    print(f"maps with headers: {len(doc['maps'])}")
    print(f"maps with zone events: {sum(1 for m in doc['maps'] if m['zone_event_file'])}")
    print(f"maps with warp arrivals: {with_warp}, warp arrival tiles: {total}")
    print(f"door arrival tiles: {doors}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
