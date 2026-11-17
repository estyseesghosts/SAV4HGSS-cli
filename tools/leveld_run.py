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
    p.add_argument("--touches", default="",
                   help="Timed touchscreen taps: 'frame:x+y:frames,...' e.g. '700:170+80:5'. "
                        "Coords are bottom-screen 0-255 x 0-191. Needed for the "
                        "HGSS bottom-screen menu (SAVE is a touch icon).")
    p.add_argument("--savestate-at", default="",
                   help="Capture savestates at frames: 'frame:name,...' e.g. '2400:menu,4200:overworld'. "
                        "Files are written as <name>.mcpr under --outdir/states/.")
    p.add_argument("--load-state", default="",
                   help="Load a .mcpr savestate right after ROM open instead of booting fresh.")
    p.add_argument("--assert-spawn", default="",
                   help="Verify the live player map/x/y in ARM9 RAM: 'map,x,y,z'. "
                        "Scans main RAM for the decomp Location struct "
                        "(field_types_def.h:10-16) built from the staged save's "
                        "M/X/Y (SAV4HGSS.cs:186-188). The z component (save Z, "
                        "SAV4HGSS.cs:200) is recorded, not asserted: real saves "
                        "carry Z=4,7 but decomp directions are 0..3 "
                        "(global_fieldmap.h:4-9), so they are different quantities.")
    p.add_argument("--extract-save", default="",
                   help="Export the in-game battery save after the run: 'auto' "
                        "writes <outdir>/played.sav, or give a file name written "
                        "under <outdir>. The headless core keeps saves in memory "
                        "(no auto-flush), so an in-game save only reaches disk "
                        "via this export (melonds_backup_export).")
    return p.parse_args()


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def check_spawn(lib, save: Path, spec: str) -> dict:
    """Verify the live player map/x/y in ARM9 main RAM.

    Differential of two overworld RAM dumps (Violet 73,459,246 vs Kanto
    gatehouse 172,6,7) isolated the live decomp `Location` struct
    (field_types_def.h:10-16: int mapId, warpId, x, y, direction):
    u16 map @+0, u32 0xFFFFFFFF warp @+4, u16 x @+8, u16 y @+12,
    u16 direction @+16 (u16 zero padding = int upper halves).
    Wanted map/x/y come from the staged save's own Location
    (SAV4HGSS.cs:186-188). The spec's 4th component (save Z,
    SAV4HGSS.cs:200) is NOT asserted: save Z takes values 4 and 7 on
    real saves (Mahogany, Blackthorn) while decomp directions are
    0..3 (global_fieldmap.h:4-9 DIR_MAX 4), so they are different
    quantities; live direction is reported as observed. Scans
    0x02000000-0x02400000.

    The scan keys on map+warp and decodes x/y/dir per hit, but matches
    on the full map/x/y pattern and requires AT LEAST TWO agreeing
    copies. Nov 2026 testing showed why both halves matter: the field
    keeps several Location-shaped structs per map (Violet fresh boot:
    player (459,246) at 0x227d498 + 0x239aa40 plus NPC structs
    (470,260) at 0x2377a20 and (459,255) at 0x2377a70; Saffron fresh
    boot: player x3 at 0x227d498/0x2377a20/0x239aa40 plus an NPC x3),
    so map-only unanimity false-fails on NPCs; and after DOWN x30 one
    player copy (0x227d498) moved 246->248 while its mirror
    (0x239aa40) stayed, so any-single-hit matching false-passes on
    the stale mirror. Fresh boots (no inputs, stationary player) show
    2-3 agreeing player copies; a moved player leaves exactly 1. The
    report lists every same-map struct with coordinates so future
    divergence is visible instead of silent.
    """
    import struct

    want = [int(v) for v in spec.split(",")]
    if len(want) != 4:
        raise ValueError(f"Bad --assert-spawn spec {spec!r} (want map,x,y,z).")
    want_map, want_x, want_y, want_z = want
    for v in want:
        if not 0 <= v <= 0xFFFF:
            raise ValueError(f"Spawn component {v} outside u16 range.")
    prefix = struct.pack("<H", want_map) + b"\x00\x00\xff\xff\xff\xff"
    pattern = prefix + struct.pack("<H", want_x) + b"\x00\x00" + struct.pack("<H", want_y) + b"\x00\x00"
    hits: list[dict] = []
    matched: list[dict] = []
    RAM_START, RAM_LEN, CHUNK = 0x02000000, 0x400000, 0x1000
    addr = RAM_START
    carry = b""
    while addr < RAM_START + RAM_LEN:
        size = min(CHUNK, RAM_START + RAM_LEN - addr)
        try:
            view = lib.peek_block(0, addr, size)
        except Exception:
            addr += size
            carry = b""
            continue
        window = carry + view
        base = addr - len(carry)
        idx = window.find(prefix)
        while idx >= 0:
            hit_addr = base + idx
            try:
                rest = lib.peek_block(0, hit_addr + 8, 12)
                hx = struct.unpack("<H", rest[0:2])[0]
                hy = struct.unpack("<H", rest[4:6])[0]
                hd = struct.unpack("<H", rest[8:10])[0]
            except Exception:
                hx, hy, hd = None, None, None
            rec = {"address": hex(hit_addr), "x": hx, "y": hy, "direction": hd}
            hits.append(rec)
            if hx == want_x and hy == want_y:
                matched.append(rec)
            idx = window.find(prefix, idx + 1)
        carry = window[-(len(prefix) - 1):] if len(prefix) > 1 else b""
        addr += size
    match = len(matched) >= 2
    live = [want_map, want_x, want_y] if match else None
    return {"prefix": pattern.hex(), "hits": hits, "matched": matched,
            "live": live, "want": [want_map, want_x, want_y],
            "save_z": want_z, "z_note": "save Z is not live direction (Z=4,7 observed; DIR range is 0..3)",
            "match": match}


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
    touches: list[tuple[int, int, int, int]] = []
    if args.touches.strip():
        for spec in args.touches.split(","):
            parts = spec.strip().split(":")
            if len(parts) != 3:
                print(f"Ignoring malformed touch spec: {spec!r} (want frame:x+y:frames).", file=sys.stderr)
                continue
            xy = parts[1].split("+")
            if len(xy) != 2:
                print(f"Ignoring malformed touch spec: {spec!r} (want frame:x+y:frames).", file=sys.stderr)
                continue
            touches.append((int(parts[0]), int(xy[0]), int(xy[1]), int(parts[2])))
    actions: list[tuple[int, str, object]] = []
    for pf, buttons, hold in presses:
        actions.append((pf, "buttons", (buttons, hold)))
    for tf, tx, ty, hold in touches:
        actions.append((tf, "touch", (tx, ty, hold)))
    actions.sort(key=lambda a: a[0])
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
        action_idx = 0
        events: list[dict] = []
        states: list[dict] = []

        def apply_action(af: int, kind: str, payload: object) -> None:
            if kind == "buttons":
                from melonds_mcp.constants import buttons_to_bitmask

                buttons, hold = payload  # type: ignore[misc]
                emu.lib.lib.melonds_input_keypad_update(buttons_to_bitmask(buttons))
                try:
                    emu.advance_frames(hold)
                finally:
                    emu.lib.lib.melonds_input_keypad_update(0)
                events.append({"frame": af, "buttons": buttons, "hold": hold})
            else:
                tx, ty, hold = payload  # type: ignore[misc]
                emu.lib.lib.melonds_input_set_touch_pos(tx, ty)
                try:
                    emu.advance_frames(hold)
                finally:
                    emu.lib.lib.melonds_input_release_touch()
                events.append({"frame": af, "touch": [tx, ty], "hold": hold})

        def advance_to(target: int) -> None:
            nonlocal advanced
            target = min(target, args.frames)
            if target > advanced:
                emu.advance_frames(target - advanced)
                advanced = target

        saved_state_frames: set[int] = set()

        def snapshot_crossed(prev: int) -> None:
            for f in sorted(state_at):
                if prev < f <= advanced and f not in saved_state_frames:
                    name = state_at[f]
                    dest = states_dir / f"{name}.mcpr"
                    ok = bool(lib.lib.melonds_savestate_save(str(dest).encode()))
                    states.append({"frame": f, "name": name, "file": dest.name, "ok": ok})
                    saved_state_frames.add(f)
                    if not ok:
                        raise RuntimeError(f"Savestate save failed at frame {f}.")

        for target in milestones:
            while action_idx < len(actions) and actions[action_idx][0] <= target:
                af, kind, payload = actions[action_idx]
                prev = advanced
                advance_to(af)
                snapshot_crossed(prev)
                apply_action(af, kind, payload)
                prev = advanced
                advanced += payload[-1]
                snapshot_crossed(prev)
                action_idx += 1
            prev = advanced
            advance_to(target)
            snapshot_crossed(prev)
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
        # Remaining actions/frames past the last milestone.
        while action_idx < len(actions):
            af, kind, payload = actions[action_idx]
            prev = advanced
            advance_to(af)
            snapshot_crossed(prev)
            apply_action(af, kind, payload)
            prev = advanced
            advanced += payload[-1]
            snapshot_crossed(prev)
            action_idx += 1
        if advanced < args.frames:
            prev = advanced
            emu.advance_frames(args.frames - advanced)
            advanced = args.frames
            snapshot_crossed(prev)
        report["inputs"] = events
        report["savestates"] = states
        if args.extract_save.strip():
            name = args.extract_save.strip()
            dest = outdir / ("played.sav" if name == "auto" else name)
            okx = bool(lib.lib.melonds_backup_export(str(dest).encode()))
            report["extracted_save"] = {"file": dest.name, "ok": okx,
                                        "sha256": sha256(dest) if okx else None}
            if not okx:
                report["error"] = "Backup export failed."
                return 1
        if args.assert_spawn.strip():
            report["spawn_check"] = check_spawn(lib, save, args.assert_spawn)
            if not report["spawn_check"]["match"]:
                report["error"] = "Live spawn mismatch: %s" % report["spawn_check"]
                return 1
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
