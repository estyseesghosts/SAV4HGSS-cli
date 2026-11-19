"""Portable registry checks that need no ROM, save, decomp or emulator.

Run by CI and by hand. Every check here is a statement about committed files:
that they parse, that their schemas match, that the counts they advertise are
the counts they hold, and that the references between them resolve. Nothing
touches `leveld-out/`, a ROM or the corpus.

The scene-drift check (regenerating `registry/hgss-us.scenes.json` from
`tools/make_scenes.py`) is *not* here: make_scenes.py reads the pokeheartgold
checkout, so it belongs to the private tier.

  python tools/registry_checks.py            # exits non-zero on the first failure
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REG = ROOT / "registry"

failures: list[str] = []
checks = 0


def check(ok: bool, what: str) -> None:
    global checks
    checks += 1
    if not ok:
        failures.append(what)


def count() -> None:
    global checks
    checks += 1


def load(name: str) -> dict | None:
    path = REG / name
    if not path.exists():
        failures.append(f"missing file: registry/{name}")
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as e:  # noqa: BLE001 - report, do not raise
        failures.append(f"registry/{name} is not valid JSON: {e}")
        return None
    check(isinstance(data, dict) and "schema" in data, f"registry/{name} declares no schema")
    return data


def main() -> int:
    rules = load("hgss-us.badge-rules.json")
    presets = load("presets/index.json")
    scenes = load("hgss-us.scenes.json")
    symbols = load("hgss-us.symbols.json")
    warp_spawns = load("hgss-us.warp-spawns.json")

    # --- badge rules: every badge id present, counters reference real badges ---
    if rules:
        ids = {b["id"] for b in rules.get("badges", [])}
        check(ids == set(range(16)), f"badge-rules covers badges {sorted(ids)}, expected 0..15")
        for c in rules.get("counters", []):
            check(set(c["badges"]) <= ids, f"counter {c.get('var')} references a badge outside 0..15")
            check(0 <= c["hold_from"] <= 16, f"counter {c.get('var')} has hold_from {c.get('hold_from')}")
        for g in rules.get("grants", []):
            check(isinstance(g.get("flag_id"), int), f"grant {g.get('flag')} has no flag_id")
            check(isinstance(g.get("item_id"), int), f"grant {g.get('item')} has no item_id")
            check(set(g.get("observed_badges", [])) <= ids,
                  f"grant {g.get('flag')} cites a badge outside 0..15")

    # --- presets: index agrees with files, badges in range, game known ---
    if presets:
        listed = presets.get("presets", [])
        for p in listed:
            name = p["name"]
            path = REG / "presets" / f"{name}.json"
            check(path.exists(), f"preset index lists {name} but presets/{name}.json is missing")
            check(p["game"] in ("HG", "SS"), f"preset {name} has game {p['game']!r}")
            for k in ("badges_johto", "badges_kanto"):
                check(0 <= p[k] <= 255, f"preset {name} {k}={p[k]} is outside 0..255")
        on_disk = {f.stem for f in (REG / "presets").glob("*.json")} - {"index"}
        indexed = {p["name"] for p in listed}
        check(on_disk == indexed,
              f"preset files and index disagree: only on disk {sorted(on_disk - indexed)}, "
              f"only indexed {sorted(indexed - on_disk)}")

    # --- scenes: counts match, ids unique, preset references resolve ---
    if scenes:
        sc = scenes.get("scenes", [])
        ids = [s["id"] for s in sc]
        check(len(ids) == len(set(ids)), "scene ids are not unique")
        counts = scenes.get("counts", {})
        advertised = counts.get("total")
        check(advertised == len(sc), f"scene registry advertises {advertised} scenes but holds {len(sc)}")
        per_kind = {k: v for k, v in counts.items() if k != "total"}
        check(sum(per_kind.values()) == len(sc),
              f"scene registry per-kind counts sum to {sum(per_kind.values())} but it holds {len(sc)}")
        for kind, n in per_kind.items():
            actual = sum(1 for s in sc if s.get("kind") == kind)
            check(actual == n, f"scene registry says {n} {kind} scenes but holds {actual}")
        preset_names = ({p["name"] for p in presets.get("presets", [])} if presets else set())
        for s in sc:
            check(s.get("game") in ("HG", "SS"), f"scene {s['id']} has game {s.get('game')!r}")
            base = s.get("base_preset")
            if base is not None:
                check(base in preset_names, f"scene {s['id']} names missing preset {base}")
            exp = s.get("expect", {})
            for key in ("badges_johto", "badges_kanto"):
                if key in exp:
                    check(0 <= exp[key] <= 255, f"scene {s['id']} expect.{key}={exp[key]}")

            # flags_on and flags_off must not both name the same symbol
            on, off = set(exp.get("flags_on", [])), set(exp.get("flags_off", []))
            both = on & off
            check(not both, f"scene {s['id']} expects {sorted(both)} both on and off")

    # --- warp spawns: map keys are integers, tiles present ---
    if warp_spawns:
        maps = warp_spawns.get("maps", warp_spawns.get("spawns", {}))
        if isinstance(maps, dict):
            for key, value in maps.items():
                check(str(key).isdigit(), f"warp-spawns has a non-numeric map key {key!r}")
                check(isinstance(value, (list, dict)), f"warp-spawns map {key} is not a list or object")

    # --- symbols: ids unique per kind ---
    if symbols:
        seen: set[tuple[str, int]] = set()
        for kind in ("flags", "vars", "items", "species", "moves"):
            for e in symbols.get(kind, []):
                key = (kind, e.get("id"))
                if not isinstance(e.get("id"), int):
                    failures.append(f"symbols.{kind} has an entry without an integer id")
                    continue
                check(key not in seen, f"symbols.{kind} repeats id {e['id']}")
                seen.add(key)

    # --- every generated research file parses ---
    research = ROOT / "research"
    if research.exists():
        for f in research.glob("*.json"):
            try:
                json.loads(f.read_text(encoding="utf-8"))
                count()
            except Exception as e:  # noqa: BLE001
                failures.append(f"research/{f.name} is not valid JSON: {e}")

    # --- every tool compiles ---
    import py_compile
    import tempfile
    tools_dir = ROOT / "tools"
    with tempfile.TemporaryDirectory() as tmp:
        for f in sorted(tools_dir.glob("*.py")):
            try:
                py_compile.compile(str(f), cfile=str(Path(tmp) / (f.stem + ".pyc")), doraise=True)
                count()
            except py_compile.PyCompileError as e:
                failures.append(f"tools/{f.name} does not compile: {e}")

    print(f"{checks} checks, {len(failures)} failures")
    for f in failures:
        print(f"  FAIL {f}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
