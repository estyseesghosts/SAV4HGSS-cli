"""Summarise a `fault_matrix.py` run into one JSON table (FMT-01/08).

Reads each variant's `in.sav`, `after_save.sav` and `report.json` and states,
per fault, what the game should do (decomp port), what it did (field reached,
message shown, which partition was loaded) and which partition it wrote next.

  python tools/fmt_matrix_report.py leveld-out/fmt-matrix \
    --out research/fmt-matrix.json
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import struct
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("fault_matrix", HERE / "fault_matrix.py")
fm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fm)

GENERAL_SIZE = fm.GENERAL_SIZE
STORAGE_START = fm.STORAGE_START
STORAGE_SIZE = fm.STORAGE_SIZE
PARTITION = fm.PARTITION


def playtime(data: bytes, base: int) -> list[int]:
    return list(struct.unpack_from("<HBB", data, base + 0x86))


def footers(data: bytearray) -> dict:
    return {
        "p0": {"general": fm.check(data, 0, GENERAL_SIZE),
               "storage": fm.check(data, STORAGE_START, STORAGE_SIZE)},
        "p1": {"general": fm.check(data, PARTITION, GENERAL_SIZE),
               "storage": fm.check(data, STORAGE_START + PARTITION, STORAGE_SIZE)},
    }


def all_crc_valid(f: dict) -> bool:
    return all(c["valid"] for side in f.values() for c in side.values())


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("directory")
    p.add_argument("--out", default="")
    o = p.parse_args()
    root = Path(o.directory)

    run = {}
    report_path = root / "report.json"
    if report_path.exists():
        for row in json.loads(report_path.read_text(encoding="utf-8"))["rows"]:
            run[row["variant"]] = row

    out = {"directory": str(root), "rows": []}
    for vdir in sorted(d for d in root.iterdir() if d.is_dir()):
        inp = vdir / "in.sav"
        if not inp.exists():
            continue
        data = bytearray(inp.read_bytes())
        row: dict = {
            "variant": vdir.name,
            "input_sha256": hashlib.sha256(bytes(data)).hexdigest(),
            "predicted": fm.predict(data),
            "footers": footers(data),
            "playtime_in": {"p0": playtime(data, 0), "p1": playtime(data, PARTITION)},
        }
        rep = run.get(vdir.name, {})
        row["reached_field"] = rep.get("reached_field")
        row["screenshots"] = [s["file"] for s in rep.get("screenshots", [])]
        after = vdir / "after_save.sav"
        if after.exists():
            out_bytes = after.read_bytes()
            row["after_save"] = {
                "sha256": hashlib.sha256(out_bytes).hexdigest(),
                "footers": footers(bytearray(out_bytes)),
                "playtime": {"p0": playtime(out_bytes, 0),
                             "p1": playtime(out_bytes, PARTITION)},
            }
            changed = [side for side in ("p0", "p1")
                       if row["after_save"]["playtime"][side] != row["playtime_in"][side]]
            written = changed[0] if len(changed) == 1 else "unclear"
            row["written"] = written
            row["loaded"] = ("p1" if written == "p0" else "p0") if written != "unclear" else "unclear"
            carried = row["after_save"]["playtime"][written] if written != "unclear" else None
            marker = row["playtime_in"][row["loaded"]] if row["loaded"] != "unclear" else None
            row["carried_playtime"] = carried
            row["loaded_marker"] = marker
            row["carried_matches_loaded"] = (
                None if carried is None or marker is None else
                (carried[0] * 3600 + carried[1] * 60 + carried[2])
                - (marker[0] * 3600 + marker[1] * 60 + marker[2]))
            row["after_all_crc_valid"] = all_crc_valid(row["after_save"]["footers"])
        out["rows"].append(row)

    text = json.dumps(out, indent=2)
    if o.out:
        Path(o.out).write_text(text, encoding="utf-8")

    print(f"{'variant':<24} {'predicted':<28} {'field':<6} {'loaded':<7} "
          f"{'wrote':<6} {'delta':<6} {'crc'}")
    for r in out["rows"]:
        delta = r.get("carried_matches_loaded")
        print(f"{r['variant']:<24} {r['predicted']:<28} "
              f"{str(r['reached_field']):<6} {r.get('loaded', '-'):<7} "
              f"{r.get('written', '-'):<6} "
              f"{('-' if delta is None else f'+{delta}s'):<6} "
              f"{r.get('after_all_crc_valid', '-')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
