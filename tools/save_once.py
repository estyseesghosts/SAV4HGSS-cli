"""One no-op in-game save, exported as a battery file (FMT-03).

Boots a staged ROM/save pair in the headless melonDS core, waits for the
field menu by sampling the framebuffer (the boot intro timing drifts
between runs, so a fixed frame schedule is not reliable), navigates to
SAVE, confirms overwrite, and exports the battery.

  python tools/save_once.py --rom research-stage/fmt/heartgold.nds \
    --save "hgss-saves/Lyra - Pichu.sav" --outdir leveld-out/fmt-c1
  python tools/save_once.py --rom research-stage/fmt/heartgold.nds \
    --save leveld-out/fmt-c1/out.sav --outdir leveld-out/fmt-c2 --idle 3900

Nothing touches the input save: the ROM and save are copied under
--outdir/stage.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
from pathlib import Path

SCREEN_W, SCREEN_H = 256, 192


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="One no-op in-game save, exported.")
    p.add_argument("--rom", required=True)
    p.add_argument("--save", required=True, help="Input .sav (read-only, copied to staging).")
    p.add_argument("--outdir", required=True)
    p.add_argument("--idle", type=int, default=0,
                   help="Frames to idle in the field before saving (3900 = 65 s).")
    p.add_argument("--out", default="out.sav")
    p.add_argument("--mcp-python", default="")
    p.add_argument("--library", default="")
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

    sys.path.insert(0, args.mcp_python or os.environ.get(
        "MCP_PYTHON",
        str(Path(__file__).resolve().parents[2] / "melonDS-mcp" / "mcp" / "python"),
    ))
    if args.library:
        os.environ["MELONDS_MCP_LIB"] = args.library
    from melonds_mcp.constants import buttons_to_bitmask
    from melonds_mcp.emulator import EmulatorState
    from PIL import Image

    emu = EmulatorState()
    report: dict = {
        "rom": str(rom), "save": str(save), "save_sha256": sha256(save),
        "idle_frames": args.idle, "screenshots": [], "ok": False,
    }

    def frames() -> tuple[bytes, bytes]:
        raw = emu.lib.screenshot_bytes()
        n = SCREEN_W * SCREEN_H * 3
        return raw[:n], raw[n:2 * n]

    def field_menu_visible() -> bool:
        # The field's touch menu is a large green panel on the left half of
        # the bottom screen; the Continue card and dialogs are not green.
        _, bottom = frames()
        hits = 0
        for y in range(48, 160, 4):
            for x in range(0, 80, 4):
                i = (y * SCREEN_W + x) * 3
                r, g, b = bottom[i], bottom[i + 1], bottom[i + 2]
                if g > r + 30 and g > b + 10:
                    hits += 1
        return hits > 40

    def save_prompt_visible() -> bool:
        # The save dialog's card is orange across the lower bottom screen.
        _, bottom = frames()
        hits = 0
        for y in range(140, 190, 3):
            for x in range(20, 236, 3):
                i = (y * SCREEN_W + x) * 3
                r, g, b = bottom[i], bottom[i + 1], bottom[i + 2]
                if r > 180 and 80 < g < 200 and b < 110:
                    hits += 1
        return hits > 100

    def press(button: str, hold: int, after: int) -> None:
        emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask([button]))
        try:
            emu.advance_frames(hold)
        finally:
            emu.lib.lib.melonds_input_keypad_update(0)
        if after:
            emu.advance_frames(after)

    def shot(name: str) -> None:
        top, bottom = frames()
        Image.frombytes("RGB", (256, 384), top + bottom).save(outdir / f"{name}.png")
        report["screenshots"].append(f"{name}.png")

    try:
        emu.ensure_init()
        if not emu.open_rom(str(staged_rom)):
            report["error"] = "ROM parse failed."
            return 1
        # The title screen is up at ~4200 frames; START opens the main menu,
        # one A selects Continue. The field menu appears after the load.
        emu.advance_frames(4300)
        press("start", 8, 600)
        press("a", 8, 1800)
        reached = False
        for _ in range(8):
            if field_menu_visible():
                reached = True
                break
            if save_prompt_visible():
                press("b", 8, 300)
                continue
            press("a", 8, 400)
        shot("field")
        if not reached:
            report["error"] = "field menu not reached"
            return 1
        if args.idle:
            emu.advance_frames(args.idle)
        for button in ("x", "right", "down"):
            press(button, 8, 40)
        press("a", 8, 120)   # "Would you like to save the game?" YES
        press("a", 8, 700)   # "There is already a saved file..." YES
        press("a", 8, 1500)  # write
        shot("after_save")
        dest = outdir / args.out
        ok = bool(emu.lib.lib.melonds_backup_export(str(dest).encode()))
        report["export"] = {"file": args.out, "ok": ok,
                            "sha256": sha256(dest) if ok else None}
        if not ok:
            report["error"] = "backup export failed"
            return 1
        report["ok"] = True
    except Exception as e:  # noqa: BLE001 - keep native failures in the report
        report["error"] = f"{type(e).__name__}: {e}"
        return 1
    finally:
        with open(outdir / "report.json", "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
