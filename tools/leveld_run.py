"""Level D runtime driver: boot a staged fixture save in headless melonDS.

Stages a copy of the ROM plus the fixture .sav (same basename, as
load_rom auto-loads <romname>.sav), advances N frames, captures
screenshots at milestones, and writes a machine-readable report.

Never touches the original ROM or save: everything runs on copies
under --outdir/stage/.

Requires the melonDS-mcp native core:
  MELONDS_MCP_LIB=C:/.../melonDS-mcp/build/mcp-direct/melonds_mcp.dll
  PYTHONPATH=C:/.../melonDS-mcp/mcp/python  (or pass --mcp-python)

Example:
  python tools/leveld_run.py --rom C:/roms/soulsilver.nds \
    --save fixture-out/ss-violet-safe-money.sav \
    --outdir leveld-out/ss-violet-safe-money --label ss-violet-safe-money \
    --frames 600 --shots 60,300,600
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Boot a fixture save in headless melonDS and capture frames.")
    p.add_argument("--rom", required=True, help="NDS ROM path (read-only, copied to staging).")
    p.add_argument("--save", required=True, help="Fixture .sav to stage alongside the ROM copy.")
    p.add_argument("--outdir", required=True, help="Output directory for report.json and PNGs.")
    p.add_argument("--label", default="", help="Fixture/run label recorded in the report.")
    p.add_argument("--frames", type=int, default=600, help="Total frames to advance.")
    p.add_argument("--shots", default="60,300,600", help="Comma-separated frame milestones for screenshots.")
    p.add_argument("--mcp-python", default="", help="Override path to melonDS-mcp/mcp/python.")
    p.add_argument("--library", default="", help="Override path to melonds_mcp.dll.")
    p.add_argument("--inputs", default="",
                   help="Timed inputs: 'frame:buttons:frames,...' e.g. '1200:start:5,1500:a:10'. "
                        "Buttons from {a,b,x,y,l,r,start,select,up,down,left,right}.")
    p.add_argument("--savestate-at", default="",
                   help="Capture savestates at frames: 'frame:name,...' e.g. '2400:menu,4200:overworld'. "
                        "Files are written as <name>.mcpr under --outdir/states/.")
    p.add_argument("--load-state", default="",
                   help="Load a .mcpr savestate right after ROM open instead of booting fresh.")
    return p.parse_args()


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    args = parse_args()
    rom = Path(args.rom)
    save = Path(args.save)
    outdir = Path(args.outdir)
    if not rom.is_file():
        print(f"ROM not found: {rom}", file=sys.stderr)
        return 2
    if not save.is_file():
        print(f"Save not found: {save}", file=sys.stderr)
        return 2
    if save.stat().st_size != 0x80000:
        print(f"Save is {save.stat().st_size} bytes, expected 524288 (raw HGSS).", file=sys.stderr)
        return 2

    mcp_python = args.mcp_python or os.environ.get(
        "MCP_PYTHON",
        str(Path(__file__).resolve().parents[2] / "melonDS-mcp" / "mcp" / "python"),
    )
    if args.library:
        os.environ["MELONDS_MCP_LIB"] = args.library
    # Python 3.8+ no longer searches PATH for native dependencies of
    # ctypes-loaded DLLs (e.g. MinGW libgcc/libstdc++/winpthread).
    for key in ("MINGW_BIN",):
        if os.environ.get(key):
            os.add_dll_directory(os.environ[key])
    else:
        import shutil as _shutil

        _mm = _shutil.which("mingw32-make")
        if _mm:
            os.add_dll_directory(str(Path(_mm).parent))
    sys.path.insert(0, mcp_python)
    try:
        from melonds_mcp.emulator import EmulatorState
    except ImportError as e:
        print(f"Cannot import melonds_mcp from {mcp_python}: {e}", file=sys.stderr)
        return 2

    try:
        from PIL import Image
    except ImportError:
        print("Pillow is required (pip install Pillow).", file=sys.stderr)
        return 2

    outdir.mkdir(parents=True, exist_ok=True)
    stage = outdir / "stage"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    staged_rom = stage / rom.name
    shutil.copyfile(rom, staged_rom)
    shutil.copyfile(save, stage / (staged_rom.stem + ".sav"))

    milestones = sorted({min(max(1, int(s)), args.frames) for s in args.shots.split(",") if s.strip()})
    presses: list[tuple[int, list[str], int]] = []
    if args.inputs.strip():
        for spec in args.inputs.split(","):
            parts = spec.strip().split(":")
            if len(parts) != 3:
                print(f"Ignoring malformed input spec: {spec!r} (want frame:buttons:frames).", file=sys.stderr)
                continue
            presses.append((int(parts[0]), parts[1].split("+"), int(parts[2])))
        presses.sort()
    state_at: dict[int, str] = {}
    if args.savestate_at.strip():
        for spec in args.savestate_at.split(","):
            parts = spec.strip().split(":")
            if len(parts) != 2:
                print(f"Ignoring malformed savestate spec: {spec!r} (want frame:name).", file=sys.stderr)
                continue
            state_at[int(parts[0])] = parts[1]
    states_dir = outdir / "states"
    if state_at:
        states_dir.mkdir(parents=True, exist_ok=True)
    emu = EmulatorState()
    report: dict = {
        "label": args.label,
        "rom": str(rom),
        "save": str(save),
        "save_sha256": sha256(save),
        "frames_requested": args.frames,
        "milestones": [],
        "ok": False,
    }
    try:
        emu.ensure_init()
        if not emu.open_rom(str(staged_rom)):
            report["error"] = "ROM parse failed in headless core."
            return 1
        from melonds_mcp.libmelonds import LibMelonDS

        lib = emu.lib
        info = lib.rom_info()
        report["rom_info"] = info
        if args.load_state.strip():
            if not lib.lib.melonds_savestate_load(args.load_state.encode()):
                report["error"] = f"Savestate load failed: {args.load_state}."
                return 1
            report["state_loaded"] = args.load_state
        advanced = 0
        press_idx = 0
        events: list[dict] = []
        states: list[dict] = []

        def advance_to(target: int) -> None:
            nonlocal advanced
            target = min(target, args.frames)
            if target > advanced:
                emu.advance_frames(target - advanced)
                advanced = target

        def maybe_snapshot() -> None:
            if advanced in state_at:
                name = state_at[advanced]
                dest = states_dir / f"{name}.mcpr"
                ok = bool(lib.lib.melonds_savestate_save(str(dest).encode()))
                states.append({"frame": advanced, "name": name, "file": dest.name, "ok": ok})
                if not ok:
                    raise RuntimeError(f"Savestate save failed at frame {advanced}.")

        for target in milestones:
            while press_idx < len(presses) and presses[press_idx][0] <= target:
                pf, buttons, hold = presses[press_idx]
                advance_to(pf)
                maybe_snapshot()
                from melonds_mcp.constants import buttons_to_bitmask

                emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask(buttons))
                try:
                    emu.advance_frames(hold)
                finally:
                    emu.lib.lib.melonds_input_keypad_update(0)
                advanced += hold
                maybe_snapshot()
                events.append({"frame": pf, "buttons": buttons, "hold": hold})
                press_idx += 1
            advance_to(target)
            maybe_snapshot()
            raw = lib.screenshot_bytes()
            top = raw[:256 * 192 * 3]
            bottom = raw[256 * 192 * 3:512 * 192 * 3]
            img = Image.frombytes("RGB", (256, 384), top + bottom)
            png = outdir / f"frame_{target:06d}.png"
            img.save(png)
            # Distinct-pixel count as a cheap liveness signal (all-black = no render).
            try:
                flat = img.get_flattened_data()  # Pillow >= 13
            except AttributeError:
                flat = list(img.getdata())
            pixels = set(flat)
            report["milestones"].append({
                "frame": target,
                "png": png.name,
                "running": emu.is_running(),
                "distinct_colors": len(pixels),
            })
        # Remaining presses/frames past the last milestone.
        while press_idx < len(presses):
            pf, buttons, hold = presses[press_idx]
            advance_to(pf)
            maybe_snapshot()
            from melonds_mcp.constants import buttons_to_bitmask

            emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask(buttons))
            try:
                emu.advance_frames(hold)
            finally:
                emu.lib.lib.melonds_input_keypad_update(0)
            advanced += hold
            maybe_snapshot()
            events.append({"frame": pf, "buttons": buttons, "hold": hold})
            press_idx += 1
        for frame in sorted(state_at):
            if frame > advanced and frame <= args.frames:
                advance_to(frame)
                maybe_snapshot()
        if advanced < args.frames:
            emu.advance_frames(args.frames - advanced)
        report["inputs"] = events
        report["savestates"] = states
        report["frames_advanced"] = args.frames
        report["running"] = emu.is_running()
        report["ok"] = True
    except Exception as e:  # noqa: BLE001 - record any native failure in the report
        report["error"] = f"{type(e).__name__}: {e}"
        return 1
    finally:
        with open(outdir / "report.json", "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
