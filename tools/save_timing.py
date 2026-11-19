"""Export the battery at several times after an in-game save (FMT-03 timing).

Purpose: decide whether the newer partition's storage CRC is invalid at the
moment of export because the save write is still in flight. Prints the
counter and CRC status of both chunks in both partitions for each export.

  python tools/save_timing.py --rom research-stage/fmt/heartgold.nds \
    --save hgss-saves/Gen4-Ash.sav --outdir leveld-out/fmt-timing
"""

from __future__ import annotations

import argparse
import binascii
import os
import shutil
import struct
import sys
from pathlib import Path

BOOT_PRE = 1200
BOOT_GAP = 592
MENU = [("x", 8, 40), ("right", 8, 40), ("down", 8, 40)]
PROMPT = [("a", 8, 60), ("a", 8, 120), ("a", 8, 0)]
STEPS = [120, 240, 480, 960, 1920, 3840]


def chunks(path: Path) -> list[dict]:
    d = path.read_bytes()
    out = []
    for part in range(2):
        for name, off, size in (("general", part * 0x40000, 0xF628),
                                ("storage", part * 0x40000 + 0xF700, 0x12310)):
            b = d[off:off + size]
            cnt, sz, magic, slot, crc = struct.unpack_from("<IIIHH", b, size - 0x10)
            out.append({"part": part, "chunk": name, "count": cnt,
                        "crc_ok": crc == binascii.crc_hqx(b[:size - 0x10], 0xFFFF)})
    return out


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--rom", required=True)
    p.add_argument("--save", required=True)
    p.add_argument("--outdir", required=True)
    return p.parse_args()


def main() -> int:
    args = parse_args()
    rom, save, outdir = Path(args.rom), Path(args.save), Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    stage = outdir / "stage"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    staged_rom = stage / rom.name
    shutil.copyfile(rom, staged_rom)
    shutil.copyfile(save, stage / (staged_rom.stem + ".sav"))
    sys.path.insert(0, os.environ.get(
        "MCP_PYTHON",
        str(Path(__file__).resolve().parents[2] / "melonDS-mcp" / "mcp" / "python"),
    ))
    from melonds_mcp.constants import buttons_to_bitmask
    from melonds_mcp.emulator import EmulatorState
    from PIL import Image

    emu = EmulatorState()
    emu.ensure_init()
    if not emu.open_rom(str(staged_rom)):
        return 1

    def press(button: str, hold: int, after: int) -> None:
        emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask([button]))
        try:
            emu.advance_frames(hold)
        finally:
            emu.lib.lib.melonds_input_keypad_update(0)
        if after:
            emu.advance_frames(after)

    emu.advance_frames(BOOT_PRE)
    press("start", 8, BOOT_GAP)
    press("a", 8, BOOT_GAP)
    press("a", 8, BOOT_GAP)
    for button, hold, after in MENU + PROMPT:
        press(button, hold, after)
    for i, step in enumerate(STEPS):
        emu.advance_frames(step)
        dest = outdir / f"t{sum(STEPS[:i + 1])}.sav"
        ok = bool(emu.lib.lib.melonds_backup_export(str(dest).encode()))
        print(f"frame+{sum(STEPS[:i + 1])} export={ok}", chunks(dest) if ok else "")
        raw = emu.lib.screenshot_bytes()
        Image.frombytes("RGB", (256, 384), raw[:512 * 192 * 3]).save(
            outdir / f"t{sum(STEPS[:i + 1])}.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
