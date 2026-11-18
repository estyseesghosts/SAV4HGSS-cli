"""Do a scene's map-load scripts override the actor visibility the scene writes?

A scene clears an actor's zone-event hide flag in the save. The map's own init scripts
(InitScriptEntry_OnLoad / OnTransition / OnFrameTable in scr_seq_####_<TAG>_hdr.s) run when
the player enters, so any SetFlag of that hide flag there can undo the scene. This tool reads
those entries from the decomp, walks GoTo/Call inside the same script file, and reports every
write to the actor's flag plus the conditions that guard it (Compare/GoToIf/CheckBadge lines
in the same label block).

The registry is the plan; this check is the source-level counterpart to
tools/scene_boot_check.py (which only proves the live Location).

usage: scene_actor_check.py [--scenes PATH] [--warps PATH] [--decomp DIR] [--out PATH]
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
DEFAULT_DECOMP = ROOT.parent / "pokeheartgold"

LABEL = re.compile(r"^([A-Za-z_]\w*):")
EV_LABEL = re.compile(r"_EV_scr_seq_(.+?)_(\d+) \+ 1")
JUMP = re.compile(r"^(GoTo\w*|CallIf\w*|Call)$")
WRITE = re.compile(r"^(SetFlag|ClearFlag|SetVar|AddVar|SubVar)$")
GUARD = re.compile(r"^(Compare|GoToIf\w*|CheckBadge|GetGameVersion|GoTo)$")


def parse_labels(path: Path) -> dict[str, list[tuple[int, str, list[str]]]]:
    labels: dict[str, list[tuple[int, str, list[str]]]] = {}
    cur = None
    for n, raw in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        line = raw.split("@", 1)[0].rstrip()
        m = LABEL.match(line)
        if m:
            cur = m.group(1)
            labels.setdefault(cur, [])
            continue
        if cur is None or not line.startswith("\t") or line.startswith("\t."):
            continue
        parts = line.strip().split(None, 1)
        args = [a.strip() for a in parts[1].split(",")] if len(parts) > 1 else []
        labels[cur].append((n, parts[0], args))
    return labels


def header_entries(hdr: Path, tag: str) -> list[dict]:
    """The OnLoad/OnTransition/OnFrameTable entries of a map header."""
    entries = []
    frame_table = None
    for n, raw in enumerate(hdr.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        line = raw.split("@", 1)[0].strip()
        m = re.match(r"^InitScriptEntry_(OnLoad|OnTransition) (.+)$", line)
        if m:
            entries.append({"kind": m.group(1).lower(), "target": m.group(2), "at": f"{hdr.name}:{n}"})
            continue
        m = re.match(r"^InitScriptEntry_OnFrameTable (\w+)$", line)
        if m:
            frame_table = m.group(1)
            continue
        m = re.match(r"^InitScriptGoToIfEqual (\w+), (\d+), (.+)$", line)
        if m and frame_table:
            entries.append({"kind": "frame", "target": m.group(3), "at": f"{hdr.name}:{n}",
                            "when": f"{m.group(1)} == {m.group(2)}"})
    return entries


def entry_label(target: str) -> str | None:
    m = EV_LABEL.search(target)
    return f"scr_seq_{m.group(1)}_{m.group(2)}" if m else None


def walk(labels, start: str):
    """Yield (label, line, op, args, path) for every instruction reachable from `start`.

    Walks the script the way the interpreter does: sequential fall-through, GoTo as a jump,
    Call as a jump that returns. A conditional jump pushes the pending Compare/CheckBadge test
    onto the branch path, so instructions reached only through `GoToIfSet X, ...` report X.
    """
    visited: set[tuple[str, int]] = set()
    stack: list[tuple[str, int, tuple[str, ...]]] = [(start, 0, ())]
    while stack:
        name, index, conds = stack.pop()
        block = labels.get(name)
        if block is None:
            continue
        pending: str | None = None
        for idx in range(index, len(block)):
            if (name, idx) in visited:
                break
            visited.add((name, idx))
            n, op, args = block[idx]
            text = f"{op} {', '.join(args)}".strip()
            yield name, n, op, args, conds
            if op in ("Compare", "CheckBadge", "GetGameVersion"):
                pending = text
            elif op == "End":
                break
            elif op.startswith("GoTo") or op == "Call" or op.startswith("CallIf"):
                target = args[-1] if args else None
                branch = conds + tuple(x for x in (pending,) if x) + (text,)
                if target:
                    stack.append((target, 0, branch))
                if op.startswith("GoToIf") or op.startswith("CallIf"):
                    continue  # fall-through of a conditional jump
                break


def reach_writes(labels, start: str, flag: str) -> list[dict]:
    """Every write to `flag` reachable from `start`, with the path conditions that lead there."""
    out: list[dict] = []
    for name, n, op, args, conds in walk(labels, start):
        if op in ("SetFlag", "ClearFlag") and flag in args:
            out.append({"label": name, "at": n, "op": op, "flag": flag,
                        "path": list(conds)})
    return out


def reach_player_moves(labels, start: str) -> list[dict]:
    """Every reachable instruction that acts on obj_player (the map's own walk-in cutscenes)."""
    out: list[dict] = []
    for name, n, op, args, conds in walk(labels, start):
        if args and args[0] == "obj_player":
            out.append({"label": name, "at": n, "op": op, "args": args, "path": list(conds)})
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenes", default=str(ROOT / "registry" / "hgss-us.scenes.json"))
    ap.add_argument("--warps", default=str(ROOT / "registry" / "hgss-us.warp-spawns.json"))
    ap.add_argument("--decomp", default=str(DEFAULT_DECOMP))
    ap.add_argument("--out", default=str(ROOT / "research" / "scene-actor-gates.json"))
    opts = ap.parse_args()

    scen = Path(opts.scenes)
    warps = json.load(open(opts.warps, encoding="utf-8"))["maps"]
    by_id = {m["map_id"]: m for m in warps}
    scr = Path(opts.decomp) / "files" / "fielddata" / "script" / "scr_seq"
    scenes = json.load(open(scen, encoding="utf-8"))["scenes"]

    rows = []
    for scene in scenes:
        actor = scene.get("actor") or {}
        flag = actor.get("hide_flag")
        m = by_id.get(scene["expect"]["map"])
        tag = m["zone_event_file"].split("_", 1)[1].rsplit(".", 1)[0] if m else None
        hdrs = sorted(scr.glob(f"scr_seq_*_{tag}_hdr.s")) if tag else []
        script = sorted(scr.glob(f"scr_seq_*_{tag}.s")) if tag else []
        row = {"id": scene["id"], "kind": scene.get("kind"), "map": scene["expect"]["map"],
               "map_symbol": m["map_symbol"] if m else None, "tag": tag, "hide_flag": flag,
               "header": hdrs[0].name if hdrs else None,
               "script": script[0].name if script else None,
               "entries": [], "writes": [], "player_moves": []}
        if hdrs and script:
            labels = parse_labels(script[0])
            row["entries"] = header_entries(hdrs[0], tag)
            for e in row["entries"]:
                name = entry_label(e["target"])
                if not name:
                    continue
                if flag:
                    for w in reach_writes(labels, name, flag):
                        w["entry"] = e["kind"]
                        if "when" in e:
                            w["when"] = e["when"]
                        row["writes"].append(w)
                for mv in reach_player_moves(labels, name):
                    key = (mv["at"], mv["op"], tuple(mv["args"]))
                    if any((x["at"], x["op"], tuple(x["args"])) == key for x in row["player_moves"]):
                        continue
                    mv["entry"] = e["kind"]
                    if "when" in e:
                        mv["when"] = e["when"]
                    row["player_moves"].append(mv)
        rows.append(row)

    with_actor = [r for r in rows if r["hide_flag"]]
    identical = [r for r in with_actor if r["writes"]]
    movers = [r for r in rows if r["player_moves"]]
    Path(opts.out).write_text(json.dumps({"schema": "hgss-save.scene-actor-gates/v1",
                                          "decomp": str(Path(opts.decomp)),
                                          "scenes": rows}, indent=1), encoding="utf-8")

    print(f"{len(rows)} scenes checked; {len(with_actor)} with an actor, "
          f"{len(identical)} whose map init writes the hide flag, "
          f"{len(movers)} whose map init scripts can move the player "
          f"(each is gated; the boot matrix shows which gates fire)")
    for r in identical:
        for w in r["writes"]:
            print(f"  {r['id']:26} {r['tag']:12} {w['entry']:6} {w['at']:5} {w['op']:9} {r['hide_flag']}")
            for g in w["path"]:
                print(f"      guard: {g}")
    for r in movers:
        for mv in r["player_moves"]:
            print(f"  MOVE {r['id']:26} {r['tag']:12} {mv['entry']:6} {mv['at']:5} "
                  f"{mv['op']} {', '.join(mv['args'])}"
                  + (f"  when {mv['when']}" if mv.get("when") else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

