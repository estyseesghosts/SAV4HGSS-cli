"""Boot-time save-selection fault matrix (FMT-01/08).

Crafts HG/SS saves with one deliberate fault each, predicts the load status from
the decomp's own decision tree (pokeheartgold/src/save.c:429-526), boots every
variant in the headless melonDS core, and records the message the game actually
shows plus the play time of the save it actually loaded.

Each partition gets a distinct play time so the loaded one is unambiguous:

    partition 0 -> 1:17   (hours 1, minutes 0x11, seconds 0x11)
    partition 1 -> 2:34   (hours 2, minutes 0x22, seconds 0x22)

  python tools/fault_matrix.py --rom research-stage/fmt/heartgold.nds \
    --base leveld-out/fmt-lyra-c2/out.sav --outdir leveld-out/fmt-matrix
"""

from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import os
import shutil
import struct
import sys
from pathlib import Path

GENERAL_SIZE = 0xF628
STORAGE_START = 0xF700
STORAGE_SIZE = 0x12310
PARTITION = 0x40000
FOOTER = 0x10

# Displayed as H:MM; chosen to be different in the leading digit only.
MARKERS = {"p0": (1, 0x11, 0x11), "p1": (2, 0x22, 0x22)}


def crc16(data: bytes) -> int:
    return binascii.crc_hqx(data, 0xFFFF)


def chunk(d: bytearray, base: int, size: int) -> bytearray:
    return bytearray(d[base:base + size])


def put_chunk(d: bytearray, base: int, size: int, body: bytearray) -> None:
    """Write a chunk payload and rebuild its footer (count/size/magic/slot/crc)."""
    out = bytearray(d[base:base + size])
    out[:size - FOOTER] = body[:size - FOOTER]
    out[size - 2:size] = struct.pack("<H", crc16(bytes(out[:size - FOOTER])))
    d[base:base + size] = out


def footer_count(d: bytearray, base: int, size: int) -> int:
    return struct.unpack_from("<I", d, base + size - 0x10)[0]


def set_footer_count(d: bytearray, base: int, size: int, count: int) -> None:
    struct.pack_into("<I", d, base + size - 0x10, count)


def set_playtime(d: bytearray, base: int, hms: tuple[int, int, int]) -> None:
    h, m, s = hms
    struct.pack_into("<HBB", d, base + 0x86, h, m, s)


def crc_ok(d: bytearray, base: int, size: int) -> bool:
    saved = struct.unpack_from("<H", d, base + size - 2)[0]
    return saved == crc16(bytes(d[base:base + size - FOOTER]))


def check(d: bytearray, base: int, size: int) -> dict:
    """Port of SaveSlotCheck_InitFromSavedat (save.c:350-360)."""
    valid = crc_ok(d, base, size)
    return {"valid": valid,
            "count": footer_count(d, base, size) if valid else 0}


def compare(a: dict, b: dict) -> tuple[int, int, int]:
    """Port of SaveSlotCheckCompare (save.c:391-421).

    Returns (numGood, newer, older).
    """
    if a["valid"] and b["valid"]:
        ca, cb = a["count"], b["count"]
        if ca > cb:
            return 2, 0, 1
        if ca < cb:
            return 2, 1, 0
        return 2, 0, 1
    if a["valid"]:
        return 1, 0, 2
    if b["valid"]:
        return 1, 1, 2
    return 0, 2, 2


def predict(d: bytearray) -> str:
    """Port of Save_GetSaveFilesStatus (save.c:429-526)."""
    main = [check(d, 0, GENERAL_SIZE), check(d, PARTITION, GENERAL_SIZE)]
    sub = [check(d, STORAGE_START, STORAGE_SIZE),
           check(d, STORAGE_START + PARTITION, STORAGE_SIZE)]
    good_main, newer_main, older_main = compare(*main)
    good_sub, newer_sub, older_sub = compare(*sub)
    if good_main == 0 and good_sub == 0:
        return "LOAD_STATUS_NOT_EXIST"
    if good_main == 0 or good_sub == 0:
        return "LOAD_STATUS_TOTAL_FAIL"
    if good_main == 2 and good_sub == 2:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            return f"LOAD_STATUS_IS_GOOD p{newer_main}"
        if main[older_main]["count"] != sub[older_main]["count"]:
            return "LOAD_STATUS_TOTAL_FAIL"
        return f"LOAD_STATUS_SLOT_FAIL p{older_main}"
    if good_main == 1 and good_sub == 2:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            return f"LOAD_STATUS_SLOT_FAIL p{newer_main}"
        return "LOAD_STATUS_TOTAL_FAIL"
    if good_main == 2 and good_sub == 1:
        if main[newer_main]["count"] == sub[newer_main]["count"]:
            return f"LOAD_STATUS_IS_GOOD p{newer_main}"
        if older_main == 2:
            return "LOAD_STATUS_TOTAL_FAIL"
        if main[older_main]["count"] == sub[older_main]["count"]:
            return f"LOAD_STATUS_SLOT_FAIL p{older_main}"
        return "LOAD_STATUS_TOTAL_FAIL"
    if good_main == 1 and good_sub == 1:
        if newer_main == newer_sub:
            return f"LOAD_STATUS_IS_GOOD p{newer_main}"
    return "LOAD_STATUS_TOTAL_FAIL"


def build_variants(base: bytes) -> dict[str, bytearray]:
    def fresh() -> bytearray:
        d = bytearray(base)
        # Both partitions valid, both carrying a distinct play-time marker.
        set_playtime(d, 0, MARKERS["p0"])
        put_chunk(d, 0, GENERAL_SIZE, chunk(d, 0, GENERAL_SIZE))
        set_playtime(d, PARTITION, MARKERS["p1"])
        put_chunk(d, PARTITION, GENERAL_SIZE, chunk(d, PARTITION, GENERAL_SIZE))
        return d

    out: dict[str, bytearray] = {}

    out["control-clean"] = fresh()                # both partitions fine, p0 newest

    d = fresh()                                   # newer general chunk, bad CRC
    d[0x200] ^= 0xFF
    out["newer-general-crc-bad"] = d

    d = fresh()                                   # newer storage chunk, bad CRC
    d[STORAGE_START + 0x400] ^= 0xFF
    out["newer-storage-crc-bad"] = d

    d = fresh()                                   # older general chunk, bad CRC
    d[PARTITION + 0x200] ^= 0xFF
    out["older-general-crc-bad"] = d

    d = fresh()                                   # counters disagree, CRCs fine
    set_footer_count(d, 0, GENERAL_SIZE, footer_count(d, 0, GENERAL_SIZE) + 1)
    out["counters-unequal"] = d

    d = fresh()                                   # nothing loadable
    d[0x200] ^= 0xFF
    d[PARTITION + 0x200] ^= 0xFF
    out["both-general-bad"] = d

    d = fresh()                                   # general and storage disagree
    d[0x200] ^= 0xFF                              # p0 general bad  -> newer_main = p1
    d[STORAGE_START + PARTITION + 0x400] ^= 0xFF   # p1 storage bad  -> newer_sub  = p0
    out["cross-partition"] = d

    return out


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--rom", required=True)
    p.add_argument("--base", required=True)
    p.add_argument("--outdir", required=True)
    p.add_argument("--start", type=int, default=1200)
    p.add_argument("--gap", type=int, default=300)
    p.add_argument("--steps", type=int, default=10)
    p.add_argument("--only", default="")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    variants = build_variants(Path(args.base).read_bytes())
    if args.only:
        wanted = set(args.only.split(","))
        variants = {k: v for k, v in variants.items() if k in wanted}

    sys.path.insert(0, os.environ.get(
        "MCP_PYTHON",
        str(Path(__file__).resolve().parents[2] / "melonDS-mcp" / "mcp" / "python"),
    ))
    from melonds_mcp.constants import buttons_to_bitmask
    from melonds_mcp.emulator import EmulatorState
    from PIL import Image

    report: dict = {"base": args.base, "rows": []}
    for name, data in variants.items():
        row: dict = {"variant": name,
                     "predicted": predict(data),
                     "sha256": hashlib.sha256(bytes(data)).hexdigest(),
                     "screenshots": []}
        vdir = outdir / name
        vdir.mkdir(parents=True, exist_ok=True)
        sav = vdir / "in.sav"
        sav.write_bytes(bytes(data))
        row["footers"] = {
            "p0": {"general": check(data, 0, GENERAL_SIZE),
                   "storage": check(data, STORAGE_START, STORAGE_SIZE)},
            "p1": {"general": check(data, PARTITION, GENERAL_SIZE),
                   "storage": check(data, STORAGE_START + PARTITION, STORAGE_SIZE)},
        }

        stage = vdir / "stage"
        if stage.exists():
            shutil.rmtree(stage)
        stage.mkdir()
        rom = Path(args.rom)
        staged_rom = stage / rom.name
        shutil.copyfile(rom, staged_rom)
        shutil.copyfile(sav, stage / (staged_rom.stem + ".sav"))

        emu = EmulatorState()
        try:
            emu.ensure_init()
            if not emu.open_rom(str(staged_rom)):
                row["error"] = "ROM parse failed"
                report["rows"].append(row)
                continue

            def shot(tag: str, note: str = "") -> None:
                raw = emu.lib.screenshot_bytes()
                f = f"{tag}.png"
                Image.frombytes("RGB", (256, 384), raw[:512 * 192 * 3]).save(vdir / f)
                row["screenshots"].append({"file": f, "note": note})

            def press(button: str, hold: int, after: int) -> None:
                emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask([button]))
                try:
                    emu.advance_frames(hold)
                finally:
                    emu.lib.lib.melonds_input_keypad_update(0)
                if after:
                    emu.advance_frames(after)

            def frames() -> tuple[bytes, bytes]:
                raw = emu.lib.screenshot_bytes()
                n = 256 * 192 * 3
                return raw[:n], raw[n:2 * n]

            def in_field() -> bool:
                # The field's touch menu is a green panel on the left half of
                # the bottom screen; dialogs and the Continue card are not.
                _, bottom = frames()
                hits = 0
                for y in range(48, 160, 4):
                    for x in range(0, 80, 4):
                        i = (y * 256 + x) * 3
                        r, g, b = bottom[i], bottom[i + 1], bottom[i + 2]
                        if g > r + 30 and g > b + 10:
                            hits += 1
                return hits > 40

            # The save check runs during the intro; a dialog waits for A.
            emu.advance_frames(args.start)
            shot("d0", "before first A")
            reached = False
            for i in range(args.steps):
                press("a", 8, args.gap - 8)
                if i < 2:
                    shot(f"d{i + 1}", f"after A #{i + 1}: message dialog")
                if in_field():
                    reached = True
                    break
            if not reached:
                for i in range(6):
                    press("a", 8, 400)
                    if in_field():
                        reached = True
                        break
            shot("field" if reached else "no-field",
                 "in-game after the load" if reached else "no Continue: new-game path")
            row["reached_field"] = reached

            # The game always writes into the partition it did not load, with
            # count = loaded count + 1 (save.c:624, save.c:635). Saving once and
            # reading the result therefore names the loaded partition twice
            # over: by which partition moved, and by the play time carried into
            # it (each partition starts with a distinct marker time).
            if reached:
                emu.advance_frames(120)
                for button in ("x", "right", "down"):
                    press(button, 8, 40)
                press("a", 8, 120)
                press("a", 8, 700)
                press("a", 8, 1500)
                shot("after-save")
                dest = vdir / "after_save.sav"
                if emu.lib.lib.melonds_backup_export(str(dest).encode()):
                    out = bytearray(dest.read_bytes())
                    row["after_save"] = {
                        "sha256": hashlib.sha256(bytes(out)).hexdigest(),
                        "p0": {"general": check(out, 0, GENERAL_SIZE),
                               "storage": check(out, STORAGE_START, STORAGE_SIZE),
                               "playtime": list(struct.unpack_from("<HBB", out, 0x86))},
                        "p1": {"general": check(out, PARTITION, GENERAL_SIZE),
                               "storage": check(out, STORAGE_START + PARTITION, STORAGE_SIZE),
                               "playtime": list(struct.unpack_from("<HBB", out, PARTITION + 0x86))},
                    }
                    # The written partition always carries a fresh play time;
                    # the counter is not a reliable tell after a fallback,
                    # because it can land on the value the rejected partition
                    # already had. loaded = the partition that was not written.
                    before = [tuple(struct.unpack_from("<HBB", data, b + 0x86))
                              for b in (0, PARTITION)]
                    after = [tuple(row["after_save"][p]["playtime"])
                             for p in ("p0", "p1")]
                    changed = [i for i in (0, 1) if after[i] != before[i]]
                    row["written"] = f"p{changed[0]}" if len(changed) == 1 else "unclear"
                    row["loaded"] = (f"p{1 - changed[0]}" if len(changed) == 1
                                     else "unclear")
                else:
                    row["after_save"] = {"export": False}
            row["ok"] = True
        except Exception as e:  # noqa: BLE001
            row["error"] = f"{type(e).__name__}: {e}"
        finally:
            report["rows"].append(row)
            (outdir / "report.json").write_text(json.dumps(report, indent=2),
                                               encoding="utf-8")

    for row in report["rows"]:
        print(f"{row['variant']:<24} predicted {row['predicted']}")
        for k, v in row["footers"].items():
            print(f"  {k} general {v['general']}  storage {v['storage']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
