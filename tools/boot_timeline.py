"""Contact sheet of the boot timeline (timing reference for the FMT runs).

Boots a save with no input, screenshots every --gap frames, and tiles the
frames into one PNG so the title, intro, main menu and overworld entry can
be timed from a single image.

  python tools/boot_timeline.py --rom research-stage/fmt/heartgold.nds \
    --save hgss-saves/Gen4-Ash.sav --outdir leveld-out/boot-timeline
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
    p.add_argument("--steps", type=int, default=20)
    p.add_argument("--start", type=int, default=0)
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
    from melonds_mcp.emulator import EmulatorState
    from PIL import Image, ImageDraw

    emu = EmulatorState()
    emu.ensure_init()
    if not emu.open_rom(str(staged_rom)):
        return 1

    shots = []
    for i in range(args.steps):
        emu.advance_frames(args.gap)
        raw = emu.lib.screenshot_bytes()
        img = Image.frombytes("RGB", (256, 384), raw[:512 * 192 * 3])
        img = img.resize((128, 192))
        d = ImageDraw.Draw(img)
        d.text((2, 2), str(args.start + (i + 1) * args.gap), fill=(255, 0, 0))
        shots.append(img)

    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * 128, rows * 192), (0, 0, 0))
    for i, img in enumerate(shots):
        sheet.paste(img, ((i % cols) * 128, (i // cols) * 192))
    sheet.save(outdir / "timeline.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
