"""Probe the in-game save sequence frame by frame (FMT-01/03/08 helper).

Boots the staged Ash save, walks to SAVE, presses A three times, and
captures a screenshot every --gap frames for --shots shots, so the save
UI and its completion can be observed instead of assumed.

  python tools/save_probe.py --rom research-stage/fmt/heartgold.nds \
    --save hgss-saves/Gen4-Ash.sav --outdir leveld-out/fmt-probe
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
from pathlib import Path

BOOT_PRE = 1200
BOOT_GAP = 592
MENU = [("x", 8, 40), ("right", 8, 40), ("down", 8, 40)]
PROMPT = [("a", 8, 60), ("a", 8, 60), ("a", 8, 0)]


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--rom", required=True)
    p.add_argument("--save", required=True)
    p.add_argument("--outdir", required=True)
    p.add_argument("--gap", type=int, default=60)
    p.add_argument("--shots", type=int, default=20)
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

    def shot(name: str) -> None:
        raw = emu.lib.screenshot_bytes()
        Image.frombytes("RGB", (256, 384),
                        raw[:512 * 192 * 3]).save(outdir / f"{name}.png")

    emu.advance_frames(BOOT_PRE)
    press("start", 8, BOOT_GAP)
    press("a", 8, BOOT_GAP)
    press("a", 8, BOOT_GAP)
    shot("00_overworld")
    for i, (button, hold, after) in enumerate(MENU + PROMPT):
        press(button, hold, after)
        shot(f"{i + 1:02d}_{button}")
    for i in range(args.shots):
        emu.advance_frames(args.gap)
        shot(f"{i + 10:02d}_save")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
