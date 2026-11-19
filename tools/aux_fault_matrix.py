r"""Auxiliary-chunk fault matrix (FMT-06).

The six auxiliary chunks (Hall of Fame, Battle Hall, four Battle Videos) live at
fixed sectors inside each partition and are stored **twice**, at `sector` and
`sector+64` (save_arrays.c:334-373, save.c:878-910). Each copy carries a
SaveArrayFooter {magic, saveno, size, idx, crc} in its last 0x10 bytes
(include/save.h:25-31), built by CreateChunkFooter (save.c:836-847).

Selection (save.c:950-1003 for the Hall of Fame; save.c:1000-1096 for chunks
1-5, which additionally require the misc key pair to match):

    copy 1 valid only   -> load copy 1
    copy 2 valid only   -> load copy 2
    both valid          -> the higher `saveno` wins, tie -> copy 1
    neither valid       -> *ret_p = 2, no message for the Hall of Fame

Chunks 1-5 are only examined at all when SAVE_MISC.extraChunksExist is set
(save.c:534-563), and only when the misc key pair for that chunk is not -1.

SAVE_MISC sits at general offset 0x2064. Two independent checks:
  - key table 0x230C == SAVE_MISC + 0x2A8 (unk_02A8[2][5], save_misc_data.h:55)
  - rivalName 0x22D4 == SAVE_MISC + 0x270 (PKHeX SAV4HGSS.cs:196 RivalSpan)
`extraChunksExist` is bit 0 of SAVE_MISC + 0x29B (save_misc_data.h:51).

  python tools/aux_fault_matrix.py --rom research-stage/fmt/heartgold.nds \
    --base "..\hgss-saves\Gen 4 - Pokemon HeartGold (ENG).sav" \
    --outdir leveld-out/aux-matrix
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
SECTOR = 0x1000
AUX_FOOTER = 0x10
MAGIC = 0x20060623

# SAVE_MISC, derived above and checked against PKHeX's rivalName offset.
MISC = 0x2064
MISC_EXTRA_EXIST = MISC + 0x29B
MISC_KEYS = MISC + 0x2A8          # u32[2][5]: key, backup key
MISC_PREFER = MISC + 0x2D0        # u8[5]

# id, partition-relative offset, payload length (SAV4HGSS.cs:40-48)
AUX = [
    (0, 0x23000, 0x2AC0, "hall-of-fame"),
    (1, 0x26000, 0x0BB0, "battle-hall"),
    (2, 0x27000, 0x1D60, "battle-video-0"),
    (3, 0x29000, 0x1D60, "battle-video-1"),
    (4, 0x2B000, 0x1D60, "battle-video-2"),
    (5, 0x2D000, 0x1D60, "battle-video-3"),
]

MARKERS = {"p0": (1, 0x11, 0x11), "p1": (2, 0x22, 0x22)}


def crc16(data: bytes) -> int:
    return binascii.crc_hqx(data, 0xFFFF)


def main_crc_ok(d: bytearray, base: int, size: int) -> bool:
    saved = struct.unpack_from("<H", d, base + size - 2)[0]
    return saved == crc16(bytes(d[base:base + size - FOOTER]))


def main_count(d: bytearray, base: int, size: int) -> int:
    return struct.unpack_from("<I", d, base + size - 0x10)[0]


def put_main_body(d: bytearray, base: int, size: int, body: bytes) -> None:
    out = bytearray(d[base:base + size])
    out[:size - FOOTER] = body[:size - FOOTER]
    out[size - 2:size] = struct.pack("<H", crc16(bytes(out[:size - FOOTER])))
    d[base:base + size] = out


def set_playtime(d: bytearray, base: int, hms: tuple[int, int, int]) -> None:
    struct.pack_into("<HBB", d, base + 0x86, *hms)


def playtime(d: bytearray, base: int) -> tuple[int, int, int]:
    return struct.unpack_from("<HBB", d, base + 0x86)


def refresh_general(d: bytearray, base: int, body: bytes) -> None:
    """Rewrite a general block's payload and rebuild its CRC, keeping its counter."""
    put_main_body(d, base, GENERAL_SIZE, body)


def aux_footer(d: bytearray, base: int, length: int) -> dict:
    """Read a SaveArrayFooter; the payload size is length-0x10 (save.c:836-847)."""
    off = base + length - AUX_FOOTER
    magic, saveno, size = struct.unpack_from("<III", d, off)
    idx, crc = struct.unpack_from("<HH", d, off + 0x0C)
    calc = crc16(bytes(d[base:base + length - 2]))
    return {"magic": magic, "saveno": saveno, "size": size, "idx": idx,
            "crc_ok": crc == calc, "key": struct.unpack_from("<I", d, base)[0]}


def aux_valid(f: dict, length: int, idx: int) -> bool:
    """Port of ValidateChunk (save.c:850-868)."""
    return (f["magic"] == MAGIC and f["size"] == length - AUX_FOOTER
            and f["idx"] == idx and f["crc_ok"])


def aux_copies(d: bytearray, offset: int, length: int):
    """The chunk's two copies.

    A chunk is written at `sector` and at `sector + 64` (save.c:906-907). Flash
    is 128 sectors of 0x1000, so sector+64 is the same slot in the *other*
    partition, not a second copy inside one partition. The pair is therefore
    (partition A copy, partition B copy), which is what PKHeX compares
    (SAV4.cs:189 `CompareExtra(Data, Data[PartitionSize..], block)`).
    """
    return (aux_footer(d, offset, length),
            aux_footer(d, PARTITION + offset, length))


def predict_battle_hall(d: bytearray) -> str:
    """Port of sub_020284A4 (save.c:1000-1096) as Save_CheckFrontierData sees it.

    `ret_p` (sp14) is the load result, `ret2_p` (sp10) is the repair flag:
      sp14 == 2                     -> err1 = 3 -> statusFlags |= 8  ("erased")
      sp14 == 1 and sp10 == 1       -> err1 = 2 -> statusFlags |= 4  ("previous loaded")
    """
    idx, off, length, _ = AUX[1]
    if not (d[MISC_EXTRA_EXIST] & 1):
        return "GATE_OFF: no check, err1 stays 1, no message"
    a, b = aux_copies(d, off, length)
    key = struct.unpack_from("<I", d, MISC_KEYS + 4 * (idx - 1))[0]
    backup = struct.unpack_from("<I", d, MISC_KEYS + 0x14 + 4 * (idx - 1))[0]
    prefer = d[MISC_PREFER + idx - 1]
    if key == 0xFFFFFFFF and backup == 0xFFFFFFFF:
        return "SKIPPED: keys are -1, err1 stays 1, no message"
    va = aux_valid(a, length, idx) and a["key"] == key
    vb = aux_valid(b, length, idx) and b["key"] == backup
    warn = ""
    if va and not vb:
        if prefer == 1:
            warn = " -> err1=2 statusFlags|=4 (previous record loaded)"
        else:
            warn = " -> err1=1, silent"
        return "load p0" + warn
    if vb and not va:
        if prefer == 0:
            warn = " -> err1=2 statusFlags|=4 (previous record loaded)"
        else:
            warn = " -> err1=1, silent"
        return "load p1" + warn
    if va and vb:
        # save.c:1057-1078. Chunks 1-5 never consult saveno: the prefer byte
        # picks which key is tested first, and the *first* key that matches the
        # corresponding payload decides. Only falling through to the second
        # key sets ret2_p (the "previous record loaded" warning).
        if prefer == 0:
            if a["key"] == key:
                return "load p0, err1=1 (silent)"
            if b["key"] == backup:
                return "load p1 via backup key -> err1=2 statusFlags|=4"
        else:
            if b["key"] == backup:
                return "load p1, err1=1 (silent)"
            if a["key"] == key:
                return "load p0 via backup key -> err1=2 statusFlags|=4"
        return "err1=3 statusFlags|=8 (Battle Hall record erased)"
    return "err1=3 statusFlags|=8 (Battle Hall record erased)"


def init_battle_hall(d: bytearray, key: int = 0x12345678, prefer: int = 0) -> None:
    """Make chunk 1 look like one the game wrote itself.

    sub_02028230 (save.c:914-948) picks a random key, stores it as the payload's
    first dword and records it in SAVE_MISC as both the key and the backup key,
    then writes copy 1 or copy 2 depending on the prefer byte.
    """
    idx, off, length, _ = AUX[1]
    for base in (off, PARTITION + off):
        out = bytearray(d[base:base + length])
        struct.pack_into("<I", out, 0, key)
        struct.pack_into("<III", out, length - 0x10, MAGIC, 1, length - 0x10)
        struct.pack_into("<H", out, length - 0x0C, idx)
        struct.pack_into("<H", out, length - 0x02, crc16(bytes(out[:length - 2])))
        d[base:base + length] = out
    for part in (0, 1):
        g = part * PARTITION
        struct.pack_into("<I", d, g + MISC_KEYS + 4 * (idx - 1), key)
        struct.pack_into("<I", d, g + MISC_KEYS + 0x14 + 4 * (idx - 1), key)
        d[g + MISC_PREFER + idx - 1] = prefer
        d[g + MISC_EXTRA_EXIST] |= 1


def break_aux(d: bytearray, base_offset: int, length: int) -> None:
    d[base_offset + length - 2] ^= 0xFF


def build_variants(base: bytes) -> dict[str, bytearray]:
    def fresh() -> bytearray:
        d = bytearray(base)
        # Both partitions valid and distinguishable by play time, as in
        # tools/fault_matrix.py, so the loaded partition is unambiguous.
        set_playtime(d, 0, MARKERS["p0"])
        refresh_general(d, 0, bytes(d[0:GENERAL_SIZE]))
        set_playtime(d, PARTITION, MARKERS["p1"])
        refresh_general(d, PARTITION, bytes(d[PARTITION:PARTITION + GENERAL_SIZE]))
        init_battle_hall(d)
        refresh_general(d, 0, bytes(d[0:GENERAL_SIZE]))
        refresh_general(d, PARTITION, bytes(d[PARTITION:PARTITION + GENERAL_SIZE]))
        return d

    _, off, length, _ = AUX[1]
    out: dict[str, bytearray] = {}

    out["control-clean"] = fresh()

    # copy 1 (partition A) is dead, copy 2 (partition B) is the survivor.
    # prefer = 1 says the last write went to copy 2, so the game must announce
    # the recovery (save.c:1040-1052 -> err1 = 2 -> statusFlags |= 4).
    d = fresh()
    d[MISC_PREFER + 1] = 0
    refresh_general(d, 0, bytes(d[0:GENERAL_SIZE]))
    break_aux(d, off, length)
    out["copy1-bad-prefer0"] = d

    d = fresh()
    for part in (0, 1):
        g = part * PARTITION
        d[g + MISC_PREFER + AUX[1][0] - 1] = 1
        refresh_general(d, g, bytes(d[g:g + GENERAL_SIZE]))
    break_aux(d, off, length)
    out["copy1-bad-prefer1"] = d

    # copy 2 dead, copy 1 the survivor, prefer says copy 1 was last.
    d = fresh()
    break_aux(d, PARTITION + off, length)
    out["copy2-bad-prefer0"] = d

    d = fresh()
    for part in (0, 1):
        g = part * PARTITION
        d[g + MISC_PREFER + AUX[1][0] - 1] = 1
        refresh_general(d, g, bytes(d[g:g + GENERAL_SIZE]))
    break_aux(d, PARTITION + off, length)
    out["copy2-bad-prefer1"] = d

    # neither copy valid -> ret_p = 2 -> err1 = 3 -> statusFlags |= 8,
    # "The Battle Hall record has been erased."
    d = fresh()
    break_aux(d, off, length)
    break_aux(d, PARTITION + off, length)
    out["both-bad"] = d

    # both valid, copy 2 carries the higher saveno. Chunks 1-5 never compare
    # saveno (save.c:1057-1078): with both keys equal and prefer 0 the p0 copy
    # is taken silently. Kept as a check that saveno really is ignored.
    d = fresh()
    struct.pack_into("<I", d, PARTITION + off + length - 0x0C, 2)
    struct.pack_into("<H", d, PARTITION + off + length - 0x02,
                     crc16(bytes(d[PARTITION + off:PARTITION + off + length - 2])))
    out["copy2-newer"] = d

    # key mismatch: the misc key no longer matches the surviving payload
    d = fresh()
    for part in (0, 1):
        g = part * PARTITION
        struct.pack_into("<I", d, g + MISC_KEYS + 4 * 1, 0x0BADF00D)
        refresh_general(d, g, bytes(d[g:g + GENERAL_SIZE]))
    break_aux(d, off, length)
    out["copy1-bad-key-mismatch"] = d

    # gate off: extras "do not exist", so Save_CheckFrontierData never looks
    d = fresh()
    for part in (0, 1):
        g = part * PARTITION
        d[g + MISC_EXTRA_EXIST] &= 0xFE
        refresh_general(d, g, bytes(d[g:g + GENERAL_SIZE]))
    break_aux(d, off, length)
    break_aux(d, PARTITION + off, length)
    out["gate-off-both-bad"] = d

    return out


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--rom", required=True)
    p.add_argument("--base", required=True)
    p.add_argument("--outdir", required=True)
    p.add_argument("--start", type=int, default=4300)
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
        _, off, length, _ = AUX[1]
        row: dict = {"variant": name,
                     "predicted": predict_battle_hall(data),
                     "sha256": hashlib.sha256(bytes(data)).hexdigest(),
                     "screenshots": []}
        vdir = outdir / name
        vdir.mkdir(parents=True, exist_ok=True)
        sav = vdir / "in.sav"
        sav.write_bytes(bytes(data))
        row["aux_in"] = {p: {"copy": aux_footer(data, b, length)}
                         for p, b in (("p0", off), ("p1", PARTITION + off))}
        row["main_in"] = {p: {"count": main_count(data, b, GENERAL_SIZE),
                              "crc_ok": main_crc_ok(data, b, GENERAL_SIZE),
                              "playtime": list(playtime(data, b))}
                          for p, b in (("p0", 0), ("p1", PARTITION))}

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
                continue

            def frames() -> tuple[bytes, bytes]:
                raw = emu.lib.screenshot_bytes()
                n = 256 * 192 * 3
                return raw[:n], raw[n:2 * n]

            def shot(tag: str, note: str = "") -> None:
                top, bottom = frames()
                Image.frombytes("RGB", (256, 384), top + bottom).save(vdir / f"{tag}.png")
                row["screenshots"].append({"file": f"{tag}.png", "note": note})

            def press(button: str, hold: int, after: int) -> None:
                emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask([button]))
                try:
                    emu.advance_frames(hold)
                finally:
                    emu.lib.lib.melonds_input_keypad_update(0)
                if after:
                    emu.advance_frames(after)

            def in_field() -> bool:
                _, bottom = frames()
                hits = 0
                for y in range(48, 160, 4):
                    for x in range(0, 80, 4):
                        i = (y * 256 + x) * 3
                        r, g, b = bottom[i], bottom[i + 1], bottom[i + 2]
                        if g > r + 30 and g > b + 10:
                            hits += 1
                return hits > 40

            # The save check runs during the intro, before the title screen.
            emu.advance_frames(1200)
            shot("d0", "before first A")
            reached = False
            for i in range(10):
                press("a", 8, 292)
                if i < 3:
                    shot(f"d{i + 1}", f"after A #{i + 1}: message dialog")
                if in_field():
                    reached = True
                    break
            if not reached:
                for _ in range(6):
                    press("a", 8, 400)
                    if in_field():
                        reached = True
                        break
            shot("field" if reached else "no-field",
                 "in-game after the load" if reached else "no Continue")
            row["reached_field"] = reached

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
                        "main": {p: {"count": main_count(out, b, GENERAL_SIZE),
                                     "crc_ok": main_crc_ok(out, b, GENERAL_SIZE),
                                     "playtime": list(playtime(out, b))}
                                 for p, b in (("p0", 0), ("p1", PARTITION))},
                        "aux": {p: {"copy": aux_footer(out, b, length)}
                                for p, b in (("p0", off), ("p1", PARTITION + off))},
                        "misc": {p: {"extraChunksExist": out[b + MISC_EXTRA_EXIST] & 1,
                                     "keys": [struct.unpack_from("<I", out, b + MISC_KEYS + 4 * k)[0]
                                              for k in range(5)],
                                     "backup": [struct.unpack_from("<I", out, b + MISC_KEYS + 0x14 + 4 * k)[0]
                                                for k in range(5)],
                                     "prefer": list(out[b + MISC_PREFER:b + MISC_PREFER + 5])}
                                 for p, b in (("p0", 0), ("p1", PARTITION))},
                    }
                    # The written partition carries a fresh play time, so the
                    # loaded one is the other. Same tell as tools/fault_matrix.py.
                    before = [tuple(row["main_in"][p]["playtime"]) for p in ("p0", "p1")]
                    after = [tuple(row["after_save"]["main"][p]["playtime"]) for p in ("p0", "p1")]
                    changed = [i for i in (0, 1) if after[i] != before[i]]
                    row["written"] = f"p{changed[0]}" if len(changed) == 1 else "unclear"
                    row["loaded"] = (f"p{1 - changed[0]}" if len(changed) == 1 else "unclear")
                else:
                    row["after_save"] = {"export": False}
            row["ok"] = True
        except Exception as e:  # noqa: BLE001
            row["error"] = f"{type(e).__name__}: {e}"
        finally:
            report["rows"].append(row)
            (outdir / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

    for row in report["rows"]:
        print(f"{row['variant']:<26} predicted {row.get('predicted')}")
        for p, v in row["aux_in"].items():
            c = v["copy"]
            print(f"  {p} valid={aux_valid(c, 0x0BB0, 1)} saveno={c['saveno']} "
                  f"idx={c['idx']} crc_ok={c['crc_ok']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
