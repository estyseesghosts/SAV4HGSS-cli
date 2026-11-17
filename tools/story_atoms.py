"""Split every consecutive step of a census lineage into atoms and link atoms by script data-flow.

Atom = the symbols (flags on/off, vars) that changed in one step and are written by one decomp script
file (scripts with the same area tag prefix, e.g. 0869_T23GYM0102, stay separate). Symbols with several
writer scripts form a 'shared' atom; symbols no script writes (item balls, trainer flags: set by the
engine) form 'engine' atoms keyed by name family.
Edge A -> B ("A needs B") when a script in A reads a symbol that an earlier-step atom B changed and
that script's read is a flag test or a var compare (script_graph readers).
usage: story_atoms.py --census C --symbols S --graph G --game HG --trainer Master --out atoms.json
"""
import argparse, json, re, sys
from collections import defaultdict
ap = argparse.ArgumentParser()
for a in ("census", "symbols", "graph", "game", "trainer", "out"): ap.add_argument("--" + a, required=True)
o = ap.parse_args()
cen = json.load(open(o.census))["saves"]
sym = json.load(open(o.symbols))["symbols"]
g = json.load(open(o.graph))
fname = {s["id"]: s["name"] for s in sym if s["kind"] == "persistent_flag"}
vname = {s["id"]: s["name"] for s in sym if s["kind"] == "save_work"}
NOISE = {"VAR_LOTO_NUMBER_LO", "VAR_FRIENDSHIP_STEPS", "FLAG_UNK_966"}
def t(s):
    h, m, x = s["play_time"].split(":"); return int(h) * 3600 + int(m) * 60 + int(x)
chain = sorted((s for s in cen if s["game"] == o.game and s["trainer"] == o.trainer), key=t)
def fam(name):
    m = re.match(r"FLAG_(HIDE_ITEMBALL|TRAINER|HIDDEN_ITEM|GOT_TM|HIDE)_?", name)
    return "engine:" + (m.group(1) if m else "other")
atoms = []
for k, (a, b) in enumerate(zip(chain, chain[1:])):
    groups = defaultdict(lambda: {"flags_on": [], "flags_off": [], "vars": {}})
    fa, fb = set(a["flags"]), set(b["flags"])
    va, vb = {int(x): y for x, y in a["vars"].items()}, {int(x): y for x, y in b["vars"].items()}
    def key(name):
        ws = sorted({w["script"] for w in g["writers"].get(name, [])})
        return ws[0] if len(ws) == 1 else (f"shared:{ws[0]}+{len(ws)-1}" if ws else fam(name))
    for i in sorted(fa ^ fb):
        n = fname.get(i, f"flag#{i}")
        if n in NOISE: continue
        groups[key(n)]["flags_on" if i in fb else "flags_off"].append(n)
    for i in sorted(set(va) | set(vb)):
        if va.get(i, 0) != vb.get(i, 0):
            n = vname.get(i, f"var#{i:X}")
            if n in NOISE or i < 0x4030: continue
            groups[key(n)]["vars"][n] = [va.get(i, 0), vb.get(i, 0)]
    for kname, v in groups.items():
        atoms.append({"step": k, "from": a["play_time"], "to": b["play_time"], "badges": f"{b['badges_johto']:08b}/{b['badges_kanto']:08b}",
                      "script": kname, **v})
# edges
changed_by = defaultdict(list)  # symbol -> [atom index]
for idx, at in enumerate(atoms):
    for n in at["flags_on"] + at["flags_off"] + list(at["vars"]): changed_by[n].append(idx)
for idx, at in enumerate(atoms):
    needs = defaultdict(set)
    if at["script"].startswith(("shared:", "engine:")): at["needs"] = {}; continue
    tag = at["script"]
    for sname, rs in g["readers"].items():
        if any(r["script"] == tag for r in rs) and sname in changed_by:
            for j in changed_by[sname]:
                if atoms[j]["step"] < at["step"]:
                    needs[atoms[j]["step"], atoms[j]["script"]].add(sname)
    at["needs"] = {f"step{s}:{sc}": sorted(v) for (s, sc), v in needs.items()}
json.dump(atoms, open(o.out, "w"), indent=1)
named = [a for a in atoms if not a["script"].startswith(("shared:", "engine:"))]
print(f"{len(atoms)} atoms ({len(named)} single-script) over {len(chain)-1} steps; {sum(1 for a in named if a['needs'])} with read-dependencies", file=sys.stderr)
