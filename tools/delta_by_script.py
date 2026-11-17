"""Group a lineage step's state change by the decomp script files that write each symbol.
usage: delta_by_script.py --census C --symbols S --graph G --from SUBSTR --to SUBSTR
"""
import argparse, json
from collections import defaultdict
ap = argparse.ArgumentParser()
for a in ("census", "symbols", "graph", "from", "to"): ap.add_argument("--" + a, required=True)
o = ap.parse_args()
cen = json.load(open(o.census))["saves"]
sym = json.load(open(o.symbols))["symbols"]
g = json.load(open(o.graph))
fn = defaultdict(list); vn = defaultdict(list)
for s in sym:
    (fn if s["kind"] == "persistent_flag" else vn if s["kind"] == "save_work" else {}).__getitem__(s["id"]).append(s["name"]) if s["kind"] in ("persistent_flag", "save_work") else None
pick = lambda n: [s for s in cen if any(n in p for p in s["paths"])][0]
a, b = pick(o.__dict__["from"]), pick(o.to)
fa, fb = set(a["flags"]), set(b["flags"])
va, vb = {int(k): v for k, v in a["vars"].items()}, {int(k): v for k, v in b["vars"].items()}
groups = defaultdict(list); orphan = []
def writers(name): return sorted({w["script"] for w in g["writers"].get(name, [])})
for i in sorted(fa ^ fb):
    name = fn[i][0] if fn[i] else f"flag#{i}"
    ws = writers(name)
    key = ws[0] if len(ws) == 1 else ("multi(%d)" % len(ws) if ws else "no-writer")
    groups[key].append(("+" if i in fb else "-") + name)
for i in sorted(set(va) | set(vb)):
    if va.get(i, 0) != vb.get(i, 0):
        name = vn[i][0] if vn[i] else f"var#{i:X}"
        ws = writers(name); key = ws[0] if len(ws) == 1 else ("multi(%d)" % len(ws) if ws else "no-writer")
        groups[key].append(f"{name}:{va.get(i,0)}->{vb.get(i,0)}")
ka, kb = a["bag"], b["bag"]
for k in sorted(set(ka) | set(kb)):
    if ka.get(k, [0, 0])[1] != kb.get(k, [0, 0])[1]:
        groups["bag"].append(f"{(kb.get(k) or ka.get(k))[0]}:{ka.get(k,[0,0])[1]}->{kb.get(k,[0,0])[1]}")
for k, v in sorted(groups.items()):
    print(f"[{k}] " + "; ".join(v))
