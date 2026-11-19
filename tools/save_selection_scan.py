"""Decide which partition the game would load, for every raw save in a directory.

Not the same question as "which copy of each block is newer". The game picks a
single partition per boot and then requires the general and storage counters
*inside that partition* to agree (`save.c:483-515`). This tool ports that
decision tree and prints, per file, the status the game would reach and the
partition it would load.

It also prints what PKHeX's per-block `SAV4BlockDetection` rule would pick, so a
file where the two disagree is visible instead of silently mis-read.

  python tools/save_selection_scan.py DIR [--out research/save-selection.json]
"""

from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import struct
import subprocess
import sys
from pathlib import Path

GENERAL_SIZE = 0xF628
STORAGE_START = 0xF700
STORAGE_SIZE = 0x12310
PARTITION = 0x40000
FOOTER = 0x10
MAGIC = (0x20060623, 0x20070903)


def crc16(data: bytes) -> int:
    return binascii.crc_hqx(data, 0xFFFF)


def copy_info(data: bytes, start: int, size: int, slot: int) -> dict:
    """Port of ValidateSaveSectorFooter + SaveSlotCheck_InitFromSavedat."""
    block = data[start:start + size]
    count, recorded, magic = struct.unpack_from("<III", block, size - FOOTER)
    fslot, saved = struct.unpack_from("<HH", block, size - 4)
    computed = crc16(block[:size - FOOTER])
    valid = (recorded == size and magic in MAGIC and fslot == slot
             and saved == computed)
    return {
        "count": count if valid else 0,
        "count_raw": count,
        "recorded_size": recorded,
        "magic": magic,
        "slot": fslot,
        "crc_saved": saved,
        "crc_computed": computed,
        "crc_valid": saved == computed,
        "size_ok": recorded == size,
        "slug_ok": fslot == slot,
        "valid": valid,
    }


def compare(a: dict, b: dict) -> tuple[int, int, int]:
    """Port of SaveSlotCheckCompare (save.c:391-421) -> (numGood, newer, older)."""
    if a["valid"] and b["valid"]:
        return (2, 0, 1) if a["count"] >= b["count"] else (2, 1, 0)
    if a["valid"]:
        return 1, 0, 2
    if b["valid"]:
        return 1, 1, 2
    return 0, 2, 2


def status(data: bytes) -> dict:
    """Port of Save_GetSaveFilesStatus (save.c:429-526)."""
    main = [copy_info(data, 0, GENERAL_SIZE, 0),
            copy_info(data, PARTITION, GENERAL_SIZE, 0)]
    sub = [copy_info(data, STORAGE_START, STORAGE_SIZE, 1),
           copy_info(data, STORAGE_START + PARTITION, STORAGE_SIZE, 1)]
    good_main, newer_main, older_main = compare(*main)
    good_sub, newer_sub, older_sub = compare(*sub)
    res = {"general": main, "storage": sub, "counts": {
        "p0": [main[0]["count_raw"], sub[0]["count_raw"]],
        "p1": [main[1]["count_raw"], sub[1]["count_raw"]]}}

    def pick(idx: int) -> dict:
        return {"status": f"LOAD_STATUS_{'IS_GOOD' if idx >= 0 else 'FAIL'}",
                "partition": None if idx < 0 else f"p{idx}"}

    if good_main == 0 and good_sub == 0:
        res.update(code="NOT_EXIST", partition=None,
                   message="no message; treated as a new game")
        return res
    if good_main == 0 or good_sub == 0:
        res.update(code="TOTAL_FAIL", partition=None,
                   message='"The save file will be erased due to corruption or damage."')
        return res
    if good_main == 2 and good_sub == 2:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            res.update(code="IS_GOOD", partition=f"p{newer_main}", message="")
            return res
        if main[older_main]["count"] != sub[older_main]["count"]:
            res.update(code="TOTAL_FAIL", partition=None,
                       message='"The save file will be erased due to corruption or damage."')
            return res
        res.update(code="SLOT_FAIL", partition=f"p{older_main}",
                   message='"The save file is corrupted. The previous save file will be loaded."')
        return res
    if good_main == 1 and good_sub == 2:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            res.update(code="SLOT_FAIL", partition=f"p{newer_main}",
                       message='"The save file is corrupted. The previous save file will be loaded."')
        else:
            res.update(code="TOTAL_FAIL", partition=None,
                       message='"The save file will be erased due to corruption or damage."')
        return res
    if good_main == 2 and good_sub == 1:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            res.update(code="IS_GOOD", partition=f"p{newer_main}", message="")
            return res
        if older_main != 2 and main[older_main]["count"] == sub[older_main]["count"]:
            res.update(code="SLOT_FAIL", partition=f"p{older_main}",
                       message='"The save file is corrupted. The previous save file will be loaded."')
            return res
        res.update(code="TOTAL_FAIL", partition=None,
                   message='"The save file will be erased due to corruption or damage."')
        return res
    if good_main == 1 and good_sub == 1 and newer_main == newer_sub:
        res.update(code="IS_GOOD", partition=f"p{newer_main}", message="")
        return res
    res.update(code="TOTAL_FAIL", partition=None,
               message='"The save file will be erased due to corruption or damage."')
    return res


def newer(a: dict, b: dict) -> int:
    """PKHeX SAV4BlockDetection.CompareFooters, applied to one block."""
    def cmp(x: int, y: int) -> int:
        if x == 0xFFFFFFFF and y != 0xFFFFFFFE:
            return 1
        if y == 0xFFFFFFFF and x != 0xFFFFFFFE:
            return 0
        return 0 if x > y else (1 if x < y else 2)

    r = cmp(a["count_raw"], b["count_raw"])
    return 0 if r != 1 else 1


def scan(path: Path, exe: str) -> dict | None:
    data = path.read_bytes()
    if len(data) != 0x80000:
        return None
    st = status(data)
    gen = "AB"[newer(st["general"][0], st["general"][1])]
    sto = "AB"[newer(st["storage"][0], st["storage"][1])]
    ident = json.loads(subprocess.run(
        [exe, "inspect", str(path), "--flags-limit", "0"],
        capture_output=True, text=True, encoding="utf-8").stdout)
    if ident.get("operation") != "inspect":
        return None
    return {
        "file": path.name,
        "sha256": hashlib.sha256(data).hexdigest(),
        "game": ident["format"]["game"],
        "trainer": ident["trainer"]["name"],
        "badges": [ident["trainer"]["badges_johto"], ident["trainer"]["badges_kanto"]],
        "game_status": st["code"],
        "game_partition": st["partition"],
        "game_message": st["message"],
        "counts": st["counts"],
        "pkhex_general": gen,
        "pkhex_storage": sto,
        "pkhex_split": gen != sto,
        "pkhex_disagrees": {
            "general": st["partition"] not in (None, "p" + str("AB".index(gen))),
            "storage": st["partition"] not in (None, "p" + str("AB".index(sto))),
        },
        "copies": {"general": st["general"], "storage": st["storage"]},
    }


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("directory")
    p.add_argument("--exe", required=True)
    p.add_argument("--out", default="")
    o = p.parse_args()

    rows = []
    for path in sorted(Path(o.directory).rglob("*")):
        if path.suffix.lower() not in {".sav", ".save"}:
            continue
        row = scan(path, o.exe)
        if row is not None:
            rows.append(row)

    uniq: dict[str, dict] = {}
    for row in rows:
        uniq.setdefault(row["sha256"], row)

    def tally(key: str) -> dict:
        out: dict[str, int] = {}
        for r in uniq.values():
            out[r[key]] = out.get(r[key], 0) + 1
        return out

    mismatched = [r for r in uniq.values()
                  if r["pkhex_disagrees"]["general"] or r["pkhex_disagrees"]["storage"]]
    report = {
        "files_scanned": len(rows),
        "unique_saves": len(uniq),
        "game_status": tally("game_status"),
        "game_partition": tally("game_partition"),
        "pkhex_split_selection": sum(1 for r in uniq.values() if r["pkhex_split"]),
        "pkhex_disagreements": len(mismatched),
        "rows": list(uniq.values()),
    }
    text = json.dumps(report, indent=2)
    if o.out:
        Path(o.out).write_text(text, encoding="utf-8")
    print(f"{len(rows)} raw HG/SS files, {len(uniq)} unique")
    print(f"  game status: {report['game_status']}")
    print(f"  game partition: {report['game_partition']}")
    print(f"  PKHeX per-block splits: {report['pkhex_split_selection']}")
    print(f"  files where PKHeX's pick is not the partition the game loads: "
          f"{report['pkhex_disagreements']}")
    for r in mismatched:
        print(f"  {r['file']}: game {r['game_status']} {r['game_partition']}, "
              f"PKHeX general {r['pkhex_general']} storage {r['pkhex_storage']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
