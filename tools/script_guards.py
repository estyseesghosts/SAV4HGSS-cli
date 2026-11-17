"""Path guards for every state write in the decomp scripts.

For each script file, walks from every entry label (scr_seq_<TAG>_<NNN>) through GoTo/GoToIf*/CallIf* edges.
The branch conditions on the path are collected as guards: flag set/unset (GoToIfSet/Unset, CallIfSet/Unset)
and variable compares (Compare VAR, X / CompareVarToValue followed by GoToIf{Eq,Ne,Lt,Le,Gt,Ge} / CallIf*). A taken branch adds
the condition, a fall-through adds its negation. Each SetFlag / ClearFlag / SetVar / AddVar / GiveBadge
reached records the guards of the path that reached it. Output: research/script-guards.json.

Limits (reported, not hidden): compares whose operand is not VAR/literal (e.g. VAR vs VAR) are skipped;
GoToIfNoItemSpace/Defeated and std_* call-outs are treated as unconditional; each write keeps at most
--max-paths distinct guard sets; the walk is capped at --max-depth blocks per path.

usage: script_guards.py --decomp DIR --out research/script-guards.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

WRITE = {"SetFlag": "flag=1", "ClearFlag": "flag=0", "SetVar": "var=", "AddVar": "var+=", "SubVar": "var-=", "GiveBadge": "badge"}
TERM = {"End", "Return", "GoTo", "EndMovement"}
NEG = {"Eq": "Ne", "Ne": "Eq", "Lt": "Ge", "Ge": "Lt", "Le": "Gt", "Gt": "Le"}
SYM = {"Eq": "==", "Ne": "!=", "Lt": "<", "Ge": ">=", "Le": "<=", "Gt": ">"}


def parse(path: Path):
    order, labels, cur = [], {}, None
    for n, raw in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        line = raw.split("@", 1)[0].rstrip()
        m = re.match(r"^([A-Za-z_]\w*):", line)
        if m:
            cur = m.group(1)
            order.append(cur)
            labels[cur] = []
            continue
        if cur is None or not line.startswith("\t") or line.startswith("\t."):
            continue
        p = line.strip().split(None, 1)
        labels[cur].append((n, p[0], [a.strip() for a in p[1].split(",")] if len(p) > 1 else []))
    return order, labels


def walk(fname, order, labels, entry, out, max_paths, max_depth):
    nxt = {a: b for a, b in zip(order, order[1:])}
    stack = [(entry, (), None, 0, None)]
    seen: set = set()
    while stack:
        lb, guards, cmp_, depth, _ = stack.pop()
        if lb not in labels or depth > max_depth or len(guards) > 8 or (lb, guards, cmp_) in seen or len(seen) > 20000:
            continue
        seen.add((lb, guards, cmp_))
        terminated = False
        for n, op, a in labels[lb]:
            if op == "Compare" and len(a) == 2:
                cmp_ = (a[0], a[1])
            elif op == "CompareVarToValue" and len(a) == 2:
                cmp_ = (a[0], a[1])
            elif op in WRITE and a:
                key = f"{op} {a[0]}" + (f", {a[1]}" if len(a) > 1 else "")
                bucket = out.setdefault(key, {})
                gk = tuple(sorted(guards))
                if gk not in bucket and len(bucket) < max_paths:
                    bucket[gk] = {"at": f"{fname}:{n}", "entry": entry}
            m = re.match(r"^(GoToIf|CallIf)(Set|Unset|Eq|Ne|Lt|Le|Gt|Ge)$", op)
            if m and a:
                kind, cond, target = m.group(1), m.group(2), a[-1]
                if cond in ("Set", "Unset") and len(a) == 2:
                    g_t, g_f = f"{a[0]}={'1' if cond == 'Set' else '0'}", f"{a[0]}={'0' if cond == 'Set' else '1'}"
                elif cond in NEG and cmp_ and len(a) == 1:
                    g_t, g_f = f"{cmp_[0]} {SYM[cond]} {cmp_[1]}", f"{cmp_[0]} {SYM[NEG[cond]]} {cmp_[1]}"
                else:
                    g_t = g_f = None
                if g_t is not None:
                    stack.append((target, guards + (g_t,), cmp_, depth + 1, None))
                    guards = guards + (g_f,) if kind == "GoToIf" else guards
                else:
                    stack.append((target, guards, cmp_, depth + 1, None))
            elif op == "GoTo" and a:
                stack.append((a[-1], guards, cmp_, depth + 1, None))
                terminated = True
                break
            elif op in ("Call", "GoToIfNoItemSpace", "GoToIfDefeated", "GoToIfNotDefeated", "CallIfDefeated", "CallIfNotDefeated") and a and a[-1] in labels:
                stack.append((a[-1], guards, cmp_, depth + 1, None))
            elif op in TERM:
                terminated = True
                break
        if not terminated and lb in nxt:
            stack.append((nxt[lb], guards, cmp_, depth + 1, None))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--decomp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--max-paths", type=int, default=6)
    ap.add_argument("--max-depth", type=int, default=40)
    o = ap.parse_args()
    scr = Path(o.decomp) / "files/fielddata/script/scr_seq"
    result = {}
    files = 0
    for f in sorted(scr.glob("scr_seq_*.s")):
        if f.name.endswith("_hdr.s"):
            continue
        order, labels = parse(f)
        out: dict = {}
        for lb in order:
            if re.match(r"^scr_seq_.+_\d+$", lb):
                walk(f.name, order, labels, lb, out, o.max_paths, o.max_depth)
        for key, bucket in out.items():
            for gk, info in bucket.items():
                result.setdefault(key, []).append({"guards": list(gk), **info})
        files += 1
    Path(o.out).write_text(json.dumps({"schema": "hgss-save.script-guards/v1", "writes": result}, indent=0), encoding="utf-8")
    print(f"{files} scripts, {len(result)} distinct writes, {sum(len(v) for v in result.values())} guarded paths", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
