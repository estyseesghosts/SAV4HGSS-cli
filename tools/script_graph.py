"""Flag/var data-flow graph over all decomp scripts.

For every persistent flag and save-work variable: which scripts write it (SetFlag, ClearFlag,
SetVar, AddVar, SubVar, SetTrainerFlag) and which read it (GoToIfSet/Unset, CallIfSet/Unset,
Compare VAR, InitScriptGoToIfEqual, CheckBadge). Script X depends on script Y when X reads a
symbol Y writes. Output research/script-graph.json; `--query SUBSTR` prints one script's inputs
and who writes each.
usage: script_graph.py --scripts DIR --symbols registry.json [--out F] [--query T22GYM0101]
"""
import argparse, json, re, sys
from collections import defaultdict
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument("--scripts", required=True); ap.add_argument("--symbols", required=True)
ap.add_argument("--out"); ap.add_argument("--graph"); ap.add_argument("--query")
o = ap.parse_args()
syms = json.load(open(o.symbols, encoding="utf-8"))["symbols"]
kinds = {s["name"]: s["kind"] for s in syms if s["kind"] in ("persistent_flag", "save_work")}
W = {"SetFlag": 0, "ClearFlag": 0, "SetVar": 0, "AddVar": 0, "SubVar": 0, "SetOrCopyVar": 0, "CopyVar": 1}
R_FLAG = ("GoToIfSet", "GoToIfUnset", "CallIfSet", "CallIfUnset")
if o.graph:
    g = json.load(open(o.graph, encoding="utf-8"))
else:
    writers, readers = defaultdict(list), defaultdict(list)
    for f in sorted(Path(o.scripts).glob("scr_seq_*.s")):
        tag = f.stem.removeprefix("scr_seq_")
        for n, raw in enumerate(f.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            if not raw.startswith("\t") or raw.startswith("\t."):
                continue
            parts = raw.split("@", 1)[0].strip().split(None, 1)
            if not parts: continue
            op, args = parts[0], [a.strip() for a in parts[1].split(",")] if len(parts) > 1 else []
            if op in W and args and args[W[op]] in kinds:
                writers[args[W[op]]].append({"script": tag, "line": n, "op": op, "args": args[1:] if op != "CopyVar" else args})
            elif op in R_FLAG and args and args[0] in kinds:
                readers[args[0]].append({"script": tag, "line": n, "op": op})
            elif op in ("Compare", "InitScriptGoToIfEqual", "CompareVarToValue") and args and args[0] in kinds:
                readers[args[0]].append({"script": tag, "line": n, "op": op, "args": args[1:2]})
            elif op == "CopyVar" and len(args) > 1 and args[1] in kinds:
                readers[args[1]].append({"script": tag, "line": n, "op": op})
    g = {"writers": writers, "readers": readers}
    if o.out: Path(o.out).write_text(json.dumps(g, indent=0), encoding="utf-8")
if o.query:
    q = o.query
    ins = defaultdict(list)
    for sym, rs in g["readers"].items():
        for r in rs:
            if q in r["script"]: ins[sym].append(r)
    for sym, rs in sorted(ins.items()):
        ws = sorted({w["script"] for w in g["writers"].get(sym, []) if q not in w["script"]})
        vals = sorted({a for r in rs for a in r.get("args", [])})
        print(f"{sym:44} reads {len(rs):2} {vals if vals else ''}  written elsewhere by: {', '.join(ws[:6]) or '-'}{' ...' if len(ws) > 6 else ''}")
print(f"{len(g['writers'])} written, {len(g['readers'])} read symbols", file=sys.stderr)
