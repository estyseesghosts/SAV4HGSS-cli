"""Summarise a PKHeX no-op round trip (FMT-04).

Three input files, in order:

  1. the game's own save (a base the game wrote)
  2. a PKHeX no-op export of it: `hgss-save patch BASE --recipe EMPTY --out ...`
  3. the battery exported after one in-game save, then after two

For each file it records the partition counters, chunk CRCs, play time and the
changed-box mask at storage+0x12004, then lists the bytes that differ between
consecutive stages. The mask is what PKHeX forces to 0x3FFFF so the game
rewrites every box on its next save (`SAV4HGSS.cs:52-58`).

  python tools/noop_roundtrip_report.py \
    --base leveld-out/fmt-lyra-c2/out.sav \
    --pkhex leveld-out/fmt-04/pkhex-noop.sav \
    --save1 leveld-out/fmt-04/roundtrip/out.sav \
    --save2 leveld-out/fmt-04/roundtrip2/out.sav \
    --out research/fmt-04-roundtrip.json
"""

from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import struct
from pathlib import Path

GENERAL_SIZE = 0xF628
STORAGE_START = 0xF700
STORAGE_SIZE = 0x12310
PARTITION = 0x40000
BOX_MASK = STORAGE_START + 0x12004


def crc16(data: bytes) -> int:
    return binascii.crc_hqx(data, 0xFFFF)


def describe(data: bytes) -> dict:
    out: dict = {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
    for name, base in (("p0", 0), ("p1", PARTITION)):
        chunks = {}
        for block, offset, size in (("general", 0, GENERAL_SIZE),
                                    ("storage", STORAGE_START, STORAGE_SIZE)):
            count = struct.unpack_from("<I", data, base + offset + size - 0x10)[0]
            saved = struct.unpack_from("<H", data, base + offset + size - 2)[0]
            chunks[block] = {
                "count": count,
                "crc_valid": saved == crc16(data[base + offset:base + offset + size - 0x10]),
            }
        hours, minutes, seconds = struct.unpack_from("<HBB", data, base + 0x86)
        chunks["play_time"] = f"{hours}:{minutes:02}:{seconds:02}"
        chunks["box_mask"] = struct.unpack_from("<I", data, base + BOX_MASK)[0]
        out[name] = chunks
    return out


def delta(before: bytes, after: bytes) -> dict:
    rows = []
    for off, (x, y) in enumerate(zip(before, after)):
        if x == y:
            continue
        part = "p0" if off < PARTITION else "p1"
        rows.append({"partition": part,
                     "offset": off - (0 if off < PARTITION else PARTITION),
                     "before": x, "after": y})
    return {"changed_bytes": len(rows), "bytes": rows}


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--base", required=True)
    p.add_argument("--pkhex", required=True)
    p.add_argument("--save1", required=True)
    p.add_argument("--save2", default="")
    p.add_argument("--out", default="")
    o = p.parse_args()

    stages = [("game", Path(o.base)), ("pkhex-noop", Path(o.pkhex)),
              ("after-one-save", Path(o.save1))]
    if o.save2:
        stages.append(("after-two-saves", Path(o.save2)))

    report = {"stages": {}, "deltas": []}
    previous = None
    for name, path in stages:
        data = path.read_bytes()
        report["stages"][name] = {"path": str(path), **describe(data)}
        if previous is not None:
            report["deltas"].append({"from": previous[0], "to": name,
                                     **delta(previous[1], data)})
        previous = (name, data)

    text = json.dumps(report, indent=2)
    if o.out:
        Path(o.out).write_text(text, encoding="utf-8")

    for name, st in report["stages"].items():
        print(f"{name:<16} sha {st['sha256'][:12]}  "
              f"counts p0 {st['p0']['general']['count']}/{st['p0']['storage']['count']} "
              f"p1 {st['p1']['general']['count']}/{st['p1']['storage']['count']}  "
              f"mask p0 {st['p0']['box_mask']:#08x} p1 {st['p1']['box_mask']:#08x}  "
              f"time p0 {st['p0']['play_time']} p1 {st['p1']['play_time']}")
    for d in report["deltas"]:
        print(f"{d['from']} -> {d['to']}: {d['changed_bytes']} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
