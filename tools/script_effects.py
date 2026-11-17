"""Extract what a decomp script does to save state, starting from any statement.

Parses pokeheartgold files/fielddata/script/scr_seq/*.s into labels and statements and follows
the straight-line path from a start point: unconditional GoTo continues into the target label,
conditional GoTo*/CallIf* branches are listed separately (they may or may not run). Effects
recorded: SetFlag, ClearFlag, SetVar, AddVar, SubVar, SetTrainerFlag, GiveBadge, GiveItem.

usage: script_effects.py --scripts DIR --anchor GiveBadge [--out file.json]
Emits, for each GiveBadge statement, the main-path effects after it and the branch targets.
Source for every row is the .s file and line, so each effect can be checked by hand.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

EFFECT_OPS = {"SetFlag", "ClearFlag", "SetVar", "AddVar", "SubVar", "SetTrainerFlag", "GiveBadge",
              "GiveItem", "ClearTrainerFlag", "SetOrCopyVar", "CopyVar"}
COND_JUMPS = re.compile(r"^(GoToIf\w+|CallIf\w+|InitScriptGoToIf\w+)$")
LABEL = re.compile(r"^([A-Za-z_][\w]*):")


class Stmt:
    __slots__ = ("file", "line", "op", "args")

    def __init__(self, file: str, line: int, op: str, args: list[str]):
        self.file, self.line, self.op, self.args = file, line, op, args


def parse(path: Path) -> dict[str, list[Stmt]]:
    labels: dict[str, list[Stmt]] = {}
    cur: list[Stmt] | None = None
    for n, raw in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        line = raw.split("@", 1)[0].rstrip()
        if not line.strip():
            continue
        m = LABEL.match(line)
        if m:
            cur = labels.setdefault(m.group(1), [])
            continue
        if cur is None or not line.startswith("\t"):
            continue
        parts = line.strip().split(None, 1)
        args = [a.strip() for a in parts[1].split(",")] if len(parts) > 1 else []
        cur.append(Stmt(path.name, n, parts[0], args))
    return labels


def walk(labels: dict[str, list[Stmt]], label: str, start: int, seen: set[str] | None = None):
    """Yield (stmt, is_main_path). Follows unconditional GoTo; lists conditional targets as branches."""
    seen = seen or set()
    stmts = labels.get(label, [])
    for s in stmts[start:]:
        if s.op in EFFECT_OPS:
            yield s, "main"
        elif COND_JUMPS.match(s.op):
            yield s, "branch"
        elif s.op == "GoTo" and s.args:
            tgt = s.args[0]
            if tgt in labels and tgt not in seen:
                yield from walk(labels, tgt, 0, seen | {label})
            return
        elif s.op in ("End", "Return", "EndMovement"):
            return


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--scripts", required=True)
    ap.add_argument("--anchor", default="GiveBadge")
    ap.add_argument("--out")
    o = ap.parse_args()

    result = []
    for f in sorted(Path(o.scripts).glob("scr_seq_*.s")):
        labels = parse(f)
        for name, stmts in labels.items():
            for i, s in enumerate(stmts):
                if s.op != o.anchor:
                    continue
                main_eff, branches = [], []
                for st, kind in walk(labels, name, i):
                    row = {"op": st.op, "args": st.args, "at": f"{st.file}:{st.line}"}
                    (main_eff if kind == "main" else branches).append(row)
                result.append({"anchor": {"op": s.op, "args": s.args, "at": f"{s.file}:{s.line}", "label": name},
                               "effects": main_eff, "branches": branches})
    text = json.dumps(result, indent=1)
    if o.out:
        Path(o.out).write_text(text, encoding="utf-8")
    for r in result:
        a = r["anchor"]
        print(f"{a['args'][0]:16} {a['at']:36} effects {len(r['effects'])} branches {len(r['branches'])}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
