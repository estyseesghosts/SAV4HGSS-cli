"""Raw byte editor for HG/SS saves, for experiments the CLI does not model yet.

Edits only the active general block of a COPY (never the input), then recomputes the
block CRC16-CCITT (init 0xFFFF, over the first 0xF618 bytes, stored little-endian in the
last 2 bytes; verified against a real save in docs/state-log.md). The output must be
checked afterwards with `hgss-save validate`.

  save_edit.py IN OUT --dynwarp MAP WARP [X Y DIR] [--flag-966] [--set-flag ID] [--clear-flag ID]
  save_edit.py IN OUT --poke OFFSET HEXBYTES        (general-relative)

Layout facts: docs/state-log.md F4. Flags: bit (id & 7) of byte (id >> 3) at general 0x10C4.
"""

from __future__ import annotations

import argparse
import json
import struct
import subprocess
import sys
from pathlib import Path

GENERAL_SIZE = 0xF628
FLAG_BASE = 0x10C4
DYNWARP = 0x1270
PARTITION = {"A": 0, "B": 0x40000}


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


def active_partition(exe: str, path: str) -> str:
    out = subprocess.run([exe, "inspect", path, "--flags-limit", "0"], capture_output=True, text=True, encoding="utf-8").stdout
    return json.loads(out)["blocks"]["active_general"]


def set_flag(general: bytearray, flag: int, value: bool) -> None:
    byte, bit = FLAG_BASE + (flag >> 3), flag & 7
    general[byte] = (general[byte] | (1 << bit)) if value else (general[byte] & ~(1 << bit) & 0xFF)


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("input")
    p.add_argument("output")
    p.add_argument("--exe", required=True, help="hgss-save executable (used to find the active partition)")
    p.add_argument("--dynwarp", nargs="+", type=int, metavar="N", help="MAP WARP [X Y DIR]")
    p.add_argument("--flag-966", action="store_true")
    p.add_argument("--set-flag", type=lambda s: int(s, 0), action="append", default=[])
    p.add_argument("--clear-flag", type=lambda s: int(s, 0), action="append", default=[])
    p.add_argument("--poke", nargs=2, metavar=("OFFSET", "HEX"), action="append", default=[])
    o = p.parse_args()

    if Path(o.output).exists() or Path(o.output).resolve() == Path(o.input).resolve():
        sys.exit("refusing to overwrite an existing file or the input")
    data = bytearray(Path(o.input).read_bytes())
    base = PARTITION[active_partition(o.exe, o.input)]
    general = bytearray(data[base:base + GENERAL_SIZE])

    if o.dynwarp:
        m, w, *rest = o.dynwarp
        x, y, d = (rest + [0, 0, 1][len(rest):])[:3]
        struct.pack_into("<iiiii", general, DYNWARP, m, w, x, y, d)
    if o.flag_966:
        o.set_flag.append(0x966)
    for f in o.set_flag:
        set_flag(general, f, True)
    for f in o.clear_flag:
        set_flag(general, f, False)
    for off, hexed in o.poke:
        raw = bytes.fromhex(hexed)
        general[int(off, 0):int(off, 0) + len(raw)] = raw

    struct.pack_into("<H", general, GENERAL_SIZE - 2, crc16(bytes(general[:GENERAL_SIZE - 0x10])))
    data[base:base + GENERAL_SIZE] = general
    Path(o.output).write_bytes(data)
    print(f"wrote {o.output} (active general {base:#x}, crc {struct.unpack_from('<H', general, GENERAL_SIZE - 2)[0]:#06x})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
