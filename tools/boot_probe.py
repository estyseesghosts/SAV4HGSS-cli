"""Boot a save and capture every step of the Continue path (FMT-01/08).

Start at frame 1200, then press A every --gap frames and screenshot, so
corruption dialogs and fallback prompts are visible instead of guessed.

  python tools/boot_probe.py --rom research-stage/fmt/heartgold.nds \
    --save leveld-out/fmt-c1/out.sav --outdir leveld-out/boot-c1
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--rom", required=True)
    p.add_argument("--save", required=True)
    p.add_argument("--outdir", required=True)
    p.add_argument("--gap", type=int, default=300)
    p.add_argument("--steps", type=int, default=12)
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

    def shot(name: str) -> None:
        raw = emu.lib.screenshot_bytes()
        Image.frombytes("RGB", (256, 384), raw[:512 * 192 * 3]).save(outdir / f"{name}.png")

    emu.advance_frames(1200)
    shot("f1200")
    for i in range(args.steps):
        emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask(["start" if i == 0 else "a"]))
        emu.advance_frames(8)
        emu.lib.lib.melonds_input_keypad_update(0)
        emu.advance_frames(args.gap - 8)
        shot(f"f{1200 + (i + 1) * args.gap:05d}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
