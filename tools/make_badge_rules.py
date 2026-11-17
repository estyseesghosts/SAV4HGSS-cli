"""Build registry/hgss-us.badge-rules.json: what the game itself changes when a badge is won.

Source of every effect: the straight-line SetFlag/ClearFlag/SetVar/SetTrainerFlag statements after
GiveBadge in the gym script (research/badge-effects.json, from tools/script_effects.py). Each effect
is classified against the census (HG+SS saves):
  confirmed    holds in every save that owns the badge, and is absent in at least one save without it
  source-only  no census save owns the badge, so only the script supports it
  skipped      held in some saves with the badge but not all: later story overwrote it (reason recorded)
FLAG_GOT_TM<nn>_FROM_* effects also carry the TM item (item id 327+nn, checked against the census bag).
AddVar effects are not copied: VAR_MIDGAME_BADGES is a counter rule (badges Fog, Storm, Mineral,
Glacier; census: value == count below 4, then 5 once the Rocket takeover starts, 29/29 saves).
usage: make_badge_rules.py --census C --symbols S --effects E --out F
"""
import argparse, json, re, sys
ap = argparse.ArgumentParser()
for a in ("census", "symbols", "effects", "out"): ap.add_argument("--" + a, required=True)
o = ap.parse_args()
cen = json.load(open(o.census))["saves"]
syms = json.load(open(o.symbols))
sy = syms["symbols"]
fid = {s["name"]: s["id"] for s in sy if s["kind"] == "persistent_flag"}
vid = {s["name"]: s["id"] for s in sy if s["kind"] == "save_work"}
BADGES = ["ZEPHYR","HIVE","PLAIN","FOG","STORM","MINERAL","GLACIER","RISING","BOULDER","CASCADE","THUNDER","RAINBOW","SOUL","MARSH","VOLCANO","EARTH"]
def has(s, b): return bool((s["badges_johto"] >> b) & 1) if b < 8 else bool((s["badges_kanto"] >> (b - 8)) & 1)
tm_items = {}
for s in cen:
    for k, (name, q) in s["bag"].items():
        m = re.fullmatch(r"TM(\d\d)", name)
        if k.startswith("Machines:") and m: tm_items[int(m.group(1))] = int(k.split(":")[1])
out = {"schema": "hgss-save.badge-rules/v1", "badges": [], "counters": []}
blocks = {b["anchor"]["args"][0].removeprefix("BADGE_"): b for b in json.load(open(o.effects))}
for b, name in enumerate(BADGES):
    blk = blocks[name]
    w = [s for s in cen if has(s, b)]; wo = [s for s in cen if not has(s, b)]
    eff, skipped = [], []
    for e in blk["effects"]:
        op, a = e["op"], e["args"]
        if op in ("SetFlag", "ClearFlag", "SetTrainerFlag"):
            n = a[0] if op != "SetTrainerFlag" else "FLAG_" + a[0]
            i = fid.get(n); val = op != "ClearFlag"
            if i is None: skipped.append({"name": n, "why": "no flag id in registry", "at": e["at"]}); continue
            t = lambda s, i=i, val=val: (i in set(s["flags"])) == val
            row = {"kind": "flag", "name": n, "id": i, "value": val}
        elif op == "SetVar":
            n = a[0]; i = vid.get(n)
            try: val = int(a[1], 0)
            except ValueError: continue
            if i is None: skipped.append({"name": n, "why": "no var id in registry", "at": e["at"]}); continue
            t = lambda s, i=i, val=val: int(s["vars"].get(str(i), 0)) == val
            row = {"kind": "var", "name": n, "id": i, "value": val}
        else:
            if op == "AddVar": skipped.append({"name": a[0], "why": "AddVar counter; only VAR_MIDGAME_BADGES is a verified counter rule", "at": e["at"]})
            continue
        hold, miss = sum(map(t, w)), sum(1 for s in wo if not t(s))
        row["at"] = e["at"]
        if not w: row["status"] = "source-only"; eff.append(row)
        elif hold == len(w) and miss >= 1: row["status"] = f"confirmed {hold}/{len(w)}"; eff.append(row)
        elif hold == len(w): row["status"] = f"confirmed {hold}/{len(w)} (already true elsewhere)"; eff.append(row)
        else: skipped.append({"name": row["name"], "why": f"held in {hold} of {len(w)} saves with the badge; later story overwrote it", "at": e["at"]})
        m = re.fullmatch(r"FLAG_GOT_TM(\d\d)_FROM_.*", row["name"])
        if m and row["value"] and int(m.group(1)) in tm_items:
            eff.append({"kind": "item", "name": f"TM{m.group(1)}", "id": tm_items[int(m.group(1))], "quantity": 1, "at": e["at"], "status": "from FLAG_GOT_TM flag"})
    out["badges"].append({"id": b, "name": name, "script": blk["anchor"]["at"], "effects": eff, "skipped": skipped})
mg = vid["VAR_MIDGAME_BADGES"]
out["counters"].append({"var": "VAR_MIDGAME_BADGES", "id": mg, "badges": [3, 4, 5, 6], "hold_from": 4,
    "rule": "value = number of owned badges among the list while below hold_from; at or above it keep max(count, current) because the takeover script raises it to 5",
    "source": ["scr_seq_0877_T24GYM0101.s:43", "scr_seq_0913_T26GYM0101.s:135", "scr_seq_0922_T27GYM0101.s:116", "scr_seq_0932_T28GYM0101.s:88", "scr_seq_0875_T24.s:46,86"]})
json.dump(out, open(o.out, "w"), indent=1)
for b in out["badges"]:
    print(f"{b['name']:9} effects {len(b['effects']):2} skipped {len(b['skipped'])}", file=sys.stderr)
