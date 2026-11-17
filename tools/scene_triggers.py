"""Cutscene / scene-machine trigger table from the decomp.

Two trigger kinds, both "when VAR == VALUE":
  coord  zone_event `coords` entries: stepping on tiles (x, z, w, h) of a map runs a script while var == val
         (files/fielddata/eventdata/zone_event/NNN_TAG.json: scriptId, x, z, w, h, var, val)
  frame  map header on-frame tables: `InitScriptGoToIfEqual VAR, VAL, _EV_scr_seq_TAG_NNN + 1`
         (scr_seq_####_TAG_hdr.s), run when the map loads or a frame passes with var == val
Each trigger records the script file and label and every write that script can reach (all GoTo/Call
paths inside the file): SetVar/AddVar/SubVar/SetFlag/ClearFlag/GiveBadge. Writes to the trigger's own
variable give the scene machine's next values.

usage: scene_triggers.py --decomp DIR --out research/scene-triggers.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

JUMPS = re.compile(r"^(GoTo\w*|CallIf\w*|Call|InitScriptGoTo\w*)$")


def parse_labels(path: Path) -> dict[str, list[tuple[int, str, list[str]]]]:
    labels: dict[str, list[tuple[int, str, list[str]]]] = {}
    cur = None
    for n, raw in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        line = raw.split("@", 1)[0].rstrip()
        m = re.match(r"^([A-Za-z_]\w*):", line)
        if m:
            cur = labels.setdefault(m.group(1), [])
            continue
        if cur is None or not line.startswith("\t") or line.startswith("\t."):
            continue
        parts = line.strip().split(None, 1)
        cur.append((n, parts[0], [a.strip() for a in parts[1].split(",")] if len(parts) > 1 else []))
    return labels


def reach(labels, start: str):
    seen, out, stack = set(), [], [start]
    while stack:
        lb = stack.pop()
        if lb in seen or lb not in labels:
            continue
        seen.add(lb)
        for n, op, args in labels[lb]:
            if op in ("SetVar", "AddVar", "SubVar", "SetFlag", "ClearFlag", "GiveBadge", "SetTrainerFlag"):
                out.append((n, op, args))
            elif JUMPS.match(op) and args:
                stack.append(args[-1])
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--decomp", required=True)
    ap.add_argument("--out", required=True)
    o = ap.parse_args()
    root = Path(o.decomp)
    scr_dir = root / "files/fielddata/script/scr_seq"
    maps = {}
    for line in (root / "include/constants/maps.h").read_text(encoding="utf-8").splitlines():
        m = re.match(r"#define (MAP_\w+)\s+(\d+)\s+// MAP_(\w+)", line)
        if m:
            maps[m.group(3)] = (m.group(1), int(m.group(2)))
    files_by_tag: dict[str, Path] = {}
    for f in scr_dir.glob("scr_seq_*.s"):
        m = re.match(r"scr_seq_\d+_(.+?)(_hdr)?\.s$", f.name)
        if m and not m.group(2):
            files_by_tag[m.group(1)] = f
    label_cache: dict[Path, dict] = {}

    def effects(tag: str, label: str):
        f = files_by_tag.get(tag)
        if f is None:
            return None, []
        labels = label_cache.setdefault(f, parse_labels(f))
        return f.name, [{"at": f"{f.name}:{n}", "op": op, "args": a} for n, op, a in reach(labels, label)]

    triggers = []
    for zf in sorted((root / "files/fielddata/eventdata/zone_event").glob("*.json")):
        tag = zf.stem.split("_", 1)[1]
        d = json.load(open(zf, encoding="utf-8"))
        for c in d.get("coords", []):
            sid = c["scriptId"]
            if not isinstance(sid, str):
                continue
            m = re.match(r"_EV_scr_seq_(.+)_(\d+) \+ 1$", sid)
            if not m:
                continue  # std_* shared scripts (colosseum etc.) have no scene var
            file, eff = effects(m.group(1), f"scr_seq_{m.group(1)}_{m.group(2)}")
            symbol, mid = maps.get(tag, (None, None))
            triggers.append({"kind": "coord", "map": symbol, "map_id": mid, "tag": tag, "var": c["var"], "value": c["val"],
                             "area": {"x": c["x"], "z": c["z"], "w": c["w"], "h": c["h"], "y": c["y"]},
                             "script": f"scr_seq_{m.group(1)}_{m.group(2)}", "file": file, "writes": eff})
    for hf in sorted(scr_dir.glob("scr_seq_*_hdr.s")):
        hm = re.match(r"scr_seq_\d+_(.+)_hdr\.s", hf.name)
        if hm is None:
            continue
        tag = hm.group(1)
        for n, raw in enumerate(hf.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            m = re.match(r"\tInitScriptGoToIfEqual (\w+), (\w+), _EV_scr_seq_(.+)_(\d+) \+ 1", raw)
            if not m:
                continue
            file, eff = effects(m.group(3), f"scr_seq_{m.group(3)}_{m.group(4)}")
            symbol, mid = maps.get(tag, (None, None))
            triggers.append({"kind": "frame", "map": symbol, "map_id": mid, "tag": tag, "var": m.group(1), "value": m.group(2),
                             "at": f"{hf.name}:{n}", "script": f"scr_seq_{m.group(3)}_{m.group(4)}", "file": file, "writes": eff})
    Path(o.out).write_text(json.dumps({"schema": "hgss-save.scene-triggers/v1", "triggers": triggers}, indent=1), encoding="utf-8")
    by_var: dict[str, int] = {}
    for t in triggers:
        by_var[t["var"]] = by_var.get(t["var"], 0) + 1
    print(f"{len(triggers)} triggers ({sum(t['kind']=='coord' for t in triggers)} coord, {sum(t['kind']=='frame' for t in triggers)} frame) over {len(by_var)} vars", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
