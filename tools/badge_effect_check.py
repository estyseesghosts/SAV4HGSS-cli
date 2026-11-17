"""Check decomp GiveBadge effect blocks (research/badge-effects.json) against census saves.

For each badge, each SetFlag/ClearFlag/SetVar effect is tested in every census save of the
chosen game: 'with' = save owns the badge, 'without' = it does not. An effect that holds in all
'with' saves and fails in some 'without' saves is confirmed as a consequence of the badge.
usage: badge_effect_check.py --census C --symbols S --effects E --game HG
"""
import argparse, json, sys
ap = argparse.ArgumentParser()
for a in ("census", "symbols", "effects", "game"): ap.add_argument("--" + a, required=True)
o = ap.parse_args()
census = json.load(open(o.census, encoding="utf-8"))["saves"]
syms = json.load(open(o.symbols, encoding="utf-8"))["symbols"]
fid = {s["name"]: s["id"] for s in syms if s["kind"] == "persistent_flag"}
vid = {s["name"]: s["id"] for s in syms if s["kind"] == "save_work"}
BADGES = ["ZEPHYR","HIVE","PLAIN","FOG","STORM","MINERAL","GLACIER","RISING","BOULDER","CASCADE","THUNDER","RAINBOW","SOUL","MARSH","VOLCANO","EARTH"]
saves = [s for s in census if s["game"] == o.game]
def has(s, b):
    return bool((s["badges_johto"] >> b) & 1) if b < 8 else bool((s["badges_kanto"] >> (b - 8)) & 1)
for blk in json.load(open(o.effects, encoding="utf-8")):
    name = blk["anchor"]["args"][0].removeprefix("BADGE_"); b = BADGES.index(name)
    w = [s for s in saves if has(s, b)]; wo = [s for s in saves if not has(s, b)]
    print(f"== {name}: {len(w)} with / {len(wo)} without   ({blk['anchor']['at']})")
    for e in blk["effects"]:
        op, a = e["op"], e["args"]
        if op in ("SetFlag", "ClearFlag"):
            i = fid.get(a[0]); want = op == "SetFlag"
            if i is None: print(f"   {op} {a[0]}: no id"); continue
            t = lambda s: (i in set(s["flags"])) == want
        elif op == "SetVar":
            i = vid.get(a[0]);
            if i is None: print(f"   {op} {a[0]}: no id"); continue
            try: val = int(a[1], 0)
            except ValueError: continue
            t = lambda s, i=i, val=val: int(s["vars"].get(str(i), 0)) == val
        else:
            continue
        print(f"   {op:9} {', '.join(a):44} with {sum(map(t, w))}/{len(w)}  without {sum(map(t, wo))}/{len(wo)}")
