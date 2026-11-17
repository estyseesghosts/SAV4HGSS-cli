"""Build an hgss-save.patch/v1 recipe that moves one census save's story state to another's.

Both halves are emitted: flags on AND flags off (progress is non-monotonic, state-log F2), and
every save-variable whose value differs. Excluded: temp/script vars below 0x4030 (not writable),
and names listed in NOISE (per-session counters that drift without story meaning).

usage: state_delta.py --census census.json --symbols registry.json --from SUBSTR --to SUBSTR
                      [--out recipe.json] [--verify HGSS_SAVE_EXE]
--from/--to match one census entry by a substring of its path (e.g. "HeartGold2gyn").
--verify applies the recipe to the --from raw save and diffs the result against --to with the CLI;
leftover flag/var differences are printed.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

NOISE = ("VAR_LOTO_NUMBER", "VAR_FRIENDSHIP_STEPS", "VAR_RANDOM")
FIRST_WRITABLE_VAR = 0x4030


def pick(census: dict, needle: str) -> dict:
    hits = [s for s in census["saves"] if any(needle in p for p in s["paths"])]
    if len(hits) != 1:
        sys.exit(f"'{needle}' matched {len(hits)} census entries")
    return hits[0]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--census", required=True)
    ap.add_argument("--symbols", required=True)
    ap.add_argument("--from", dest="src", required=True)
    ap.add_argument("--to", dest="dst", required=True)
    ap.add_argument("--out")
    ap.add_argument("--verify")
    o = ap.parse_args()
    if o.verify:
        o.verify = str(Path(o.verify).resolve())

    census = json.load(open(o.census, encoding="utf-8"))
    symbols = json.load(open(o.symbols, encoding="utf-8"))["symbols"]
    var_names: dict[int, str] = {}
    for s in symbols:
        if s["kind"] == "save_work":
            var_names.setdefault(s["id"], s["name"])

    a, b = pick(census, o.src), pick(census, o.dst)
    if a["game"] != b["game"]:
        sys.exit("refusing a cross-game delta")
    fa, fb = set(a["flags"]), set(b["flags"])
    va = {int(k): v for k, v in a["vars"].items()}
    vb = {int(k): v for k, v in b["vars"].items()}

    ops = []
    for i in sorted(fb - fa):
        ops.append({"op": "flag.set", "id": i, "value": True})
    for i in sorted(fa - fb):
        ops.append({"op": "flag.set", "id": i, "value": False})
    skipped = []
    for i in sorted(set(va) | set(vb)):
        if va.get(i, 0) == vb.get(i, 0):
            continue
        name = var_names.get(i, "")
        if i < FIRST_WRITABLE_VAR or name.startswith(NOISE):
            skipped.append((i, name))
            continue
        ops.append({"op": "variable.set", "id": i, "value": vb.get(i, 0), "expect": va.get(i, 0)})

    recipe = {"schema": "hgss-save.patch/v1", "game": a["game"], "operations": ops}
    text = json.dumps(recipe, indent=1)
    if o.out:
        Path(o.out).write_text(text, encoding="utf-8")
    print(f"{len(ops)} ops ({sum(x['op']=='flag.set' for x in ops)} flag, "
          f"{sum(x['op']=='variable.set' for x in ops)} var); skipped vars: {skipped}", file=sys.stderr)

    if o.verify:
        src_path, dst_path = a["paths"][0], b["paths"][0]
        with tempfile.TemporaryDirectory() as tmp:
            rec, out = Path(tmp, "r.json"), Path(tmp, "out.sav")
            rec.write_text(text, encoding="utf-8")
            r = subprocess.run([o.verify, "patch", src_path, "--recipe", str(rec), "--out", str(out)],
                               capture_output=True, text=True)
            if r.returncode:
                print(r.stderr, file=sys.stderr)
                return r.returncode
            d = subprocess.run([o.verify, "diff", str(out), dst_path, "--max-list", "100000"],
                               capture_output=True, text=True)
            rep = json.loads(d.stdout)
            print(f"verify: flags differing {rep['flags']['changed']}, vars differing {rep['variables']['changed']}",
                  file=sys.stderr)
            for ch in rep["variables"]["changes"][:40]:
                print("  var", ch, file=sys.stderr)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
