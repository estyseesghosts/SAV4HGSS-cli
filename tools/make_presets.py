"""Generate named full-state story presets (hgss-save.patch/v1 recipes) from census milestone saves.

A preset overwrites the whole story domain of the target save with the state of a real played
save: every persistent flag (true for the ones set in the source, false for the rest) and every
writable save variable, plus badges, KeyItems and Machines (TM/HM) pockets and a spawn warp.
The patch is absolute, so the result does not depend on the base save's progress.

Not overwritten (left as the base has them):
  FLAG_DAILY_*, FLAG_MAPTEMP_*   per-day / per-map scratch state
  flag 0x966                     owned by location.warp (state-log F4/F5)
  VAR_PLAYER_STARTER, VAR_MAGIKARP_SIZE_RECORD, VAR_LOTO_NUMBER_*, VAR_FRIENDSHIP_STEPS, VAR_RANDOM*
                                 identity / per-session values, not story
  vars below 0x4030              script temp vars (not writable)
Items, party, boxes, dex, identity, money are never touched.

usage: make_presets.py --census C --symbols S --warps W --outdir DIR
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

SKIP_FLAG_PREFIX = ("FLAG_DAILY_", "FLAG_MAPTEMP_")
SKIP_FLAG_IDS = {0x966}
SKIP_VARS = {"VAR_PLAYER_STARTER", "VAR_MAGIKARP_SIZE_RECORD", "VAR_LOTO_NUMBER_LO", "VAR_LOTO_NUMBER_HI",
             "VAR_FRIENDSHIP_STEPS"}
FIRST_WRITABLE_VAR = 0x4030
KEEP_POCKETS = ("KeyItems", "Machines")

# Post-game saves spawn at the player house door, where the game itself places the player after the Hall of Fame (state-log F5, game_clear.c).
SPAWN_OVERRIDE = {"hg-champion": (60, 1), "ss-kanto-8": (60, 1)}

# (game, trainer, substring of source path, preset name, description, spawn map id, spawn note)
PRESETS = [
    ("HG", "Ash", "Gen 4 - Pokemon HeartGold (ENG)", "hg-00-cherrygrove",
     "HeartGold, 0 badges. Real save at Cherrygrove; the pre-Falkner state. Census dex 493/493 comes from the donor save, not from play."),
    ("HG", "Master", "HeartGold1gyn", "hg-01-zephyr", "HeartGold, 1 badge (Zephyr). Source: played save at Violet City."),
    ("HG", "Master", "HeartGold2gyn", "hg-02-hive", "HeartGold, 2 badges (Zephyr, Hive). Azalea Rockets beaten, Elm's egg collected."),
    ("HG", "Master", "HeartGold3gyn", "hg-03-plain", "HeartGold, 3 badges (through Plain). Goldenrod."),
    ("HG", "Master", "HeartGold4gyn", "hg-04-fog", "HeartGold, 4 badges (through Fog). Ecruteak."),
    ("HG", "Master", "HeartGold5gyn", "hg-05-storm", "HeartGold, 5 badges (through Storm). Cianwood."),
    ("HG", "Master", "HeartGold6gyn", "hg-06-mineral", "HeartGold, 6 badges (through Mineral). Rocket takeover phase 1 in progress."),
    ("HG", "Master", "HeartGold7gyn", "hg-07-glacier", "HeartGold, 7 badges (through Glacier). Mahogany."),
    ("HG", "Master", "HeartGold8gyn", "hg-08-rising", "HeartGold, 8 badges. Blackthorn, before the Elite Four."),
    ("HG", "Master", "HeartGoldE4.SAV", "hg-e4-gate", "HeartGold, 8 badges, at the Pokemon League entrance."),
    ("HG", "Master", "HeartGoldposE4", "hg-champion", "HeartGold, Elite Four and Lance beaten (Hall of Fame map)."),
    ("HG", "Master", "HeartGoldCelebi", "hg-kanto-4", "HeartGold, post-game Kanto, 4 Kanto badges (Pewter)."),
    ("SS", "M@ster", "SoulSilver", None, None),
]


def hms(text: str) -> int:
    h, m, s = (int(p) for p in text.split(":"))
    return h * 3600 + m * 60 + s


def main() -> int:
    ap = argparse.ArgumentParser()
    for a in ("census", "symbols", "warps", "outdir"):
        ap.add_argument("--" + a, required=True)
    o = ap.parse_args()
    census = json.load(open(o.census, encoding="utf-8"))["saves"]
    syms = json.load(open(o.symbols, encoding="utf-8"))["symbols"]
    warps = json.load(open(o.warps, encoding="utf-8"))["maps"]
    flag_name = {s["id"]: s["name"] for s in syms if s["kind"] == "persistent_flag"}
    var_name = {s["id"]: s["name"] for s in syms if s["kind"] == "save_work"}
    pc_warp = {}
    for m in warps:
        for a in m["warp_arrivals"]:
            if "POKECENTER" in (a["destination_symbol"] or ""):
                pc_warp.setdefault(m["map_id"], a["warp_index"])
                break
    arrival_warp = {m["map_id"]: m["warp_arrivals"][0]["warp_index"] for m in warps if m["warp_arrivals"]}

    plan = [p for p in PRESETS if p[3]]
    # SS lineage: name by badge count in play order, one preset per distinct badge mask.
    ss = sorted((s for s in census if s["game"] == "SS" and s["trainer"] == "M@ster"), key=lambda s: hms(s["play_time"]))
    seen_masks = set()
    for s in ss:
        key = (s["badges_johto"], s["badges_kanto"])
        if key in seen_masks:
            continue
        seen_masks.add(key)
        n = bin(s["badges_johto"]).count("1")
        nm = f"ss-{n:02d}-badges" if s["badges_kanto"] == 0 else f"ss-kanto-{bin(s['badges_kanto']).count('1')}"
        plan.append(("SS", "M@ster", s["paths"][0], nm, f"SoulSilver, {n} Johto badges, {bin(s['badges_kanto']).count('1')} Kanto."))

    out = Path(o.outdir)
    out.mkdir(parents=True, exist_ok=True)
    index = []
    for game, trainer, needle, name, desc in plan:
        hits = [s for s in census if s["game"] == game and s["trainer"] == trainer and any(needle in p for p in s["paths"])]
        if len(hits) != 1:
            print(f"skip {name}: {len(hits)} census matches for {needle}", file=sys.stderr)
            continue
        s = hits[0]
        on = set(s["flags"])
        ops: list[dict] = []
        for fid in sorted(flag_name):
            n = flag_name[fid]
            if fid in SKIP_FLAG_IDS or n.startswith(SKIP_FLAG_PREFIX):
                continue
            ops.append({"op": "flag.set", "id": fid, "value": fid in on})
        vals = {int(k): v for k, v in s["vars"].items()}
        for vid in sorted(var_name):
            if vid < FIRST_WRITABLE_VAR or var_name[vid] in SKIP_VARS or var_name[vid].startswith("VAR_RANDOM"):
                continue
            ops.append({"op": "variable.set", "id": vid, "value": vals.get(vid, 0)})
        ops.append({"op": "trainer.badges.set", "johto": s["badges_johto"], "kanto": s["badges_kanto"]})
        for key, (iname, qty) in sorted(s["bag"].items(), key=lambda kv: int(kv[0].split(":")[1])):
            pocket, item = key.split(":")
            if pocket in KEEP_POCKETS:
                ops.append({"op": "inventory.quantity.set", "item": int(item), "quantity": qty})
        mid = s["map_id"]
        spawn = pc_warp.get(mid) if mid in pc_warp else None
        spawn_map = mid
        if spawn is None:
            spawn_map, spawn = mid, arrival_warp.get(mid)
        if name in SPAWN_OVERRIDE:
            spawn_map, spawn = SPAWN_OVERRIDE[name]
        if spawn is not None:
            ops.append({"op": "location.warp", "map": spawn_map, "warp": spawn})
        recipe = {"schema": "hgss-save.patch/v1", "game": game, "operations": ops}
        (out / f"{name}.json").write_text(json.dumps(recipe), encoding="utf-8")
        index.append({"name": name, "game": game, "description": desc, "source_sha256": s["sha256"],
                      "badges_johto": s["badges_johto"], "badges_kanto": s["badges_kanto"],
                      "source_map": s["map"], "spawn": {"map": spawn_map, "warp": spawn},
                      "operations": len(ops), "flags_on": len(on)})
        print(f"{name}: {len(ops)} ops, spawn {spawn_map}/{spawn}", file=sys.stderr)
    (out / "index.json").write_text(json.dumps({"schema": "hgss-save.presets/v1", "presets": index}, indent=1), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
