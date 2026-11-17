"""Minimal NDS filesystem reader and NARC splitter (read-only).

usage as a module: files = nds_files.read_fs(rom_bytes) -> {path: (start, end)}
                   members = nds_files.narc_members(blob) -> [bytes, ...]
CLI: nds_files.py ROM --list | --extract PATH OUT
"""
from __future__ import annotations

import struct
import sys


def read_fs(rom: bytes) -> dict[str, tuple[int, int]]:
    fnt_off, fnt_size, fat_off, fat_size = struct.unpack_from("<IIII", rom, 0x40)
    fat = [struct.unpack_from("<II", rom, fat_off + 8 * i) for i in range(fat_size // 8)]
    out: dict[str, tuple[int, int]] = {}

    def walk(dir_id: int, prefix: str) -> None:
        sub_off, first_file, _parent = struct.unpack_from("<IHH", rom, fnt_off + 8 * (dir_id & 0xFFF))
        pos = fnt_off + sub_off
        fid = first_file
        while True:
            t = rom[pos]
            pos += 1
            if t == 0:
                return
            n = t & 0x7F
            name = rom[pos:pos + n].decode("ascii", "replace")
            pos += n
            if t & 0x80:
                sub_id = struct.unpack_from("<H", rom, pos)[0]
                pos += 2
                walk(sub_id, prefix + name + "/")
            else:
                out[prefix + name] = fat[fid]
                fid += 1

    walk(0xF000, "")
    return out


def narc_members(blob: bytes) -> list[bytes]:
    assert blob[:4] == b"NARC", "not a NARC"
    pos = struct.unpack_from("<H", blob, 0x0C)[0]
    assert blob[pos:pos + 4] == b"BTAF"
    size, count = struct.unpack_from("<IH", blob, pos + 4)[0], struct.unpack_from("<H", blob, pos + 8)[0]
    entries = [struct.unpack_from("<II", blob, pos + 12 + 8 * i) for i in range(count)]
    pos += size
    assert blob[pos:pos + 4] == b"BTNF"
    pos += struct.unpack_from("<I", blob, pos + 4)[0]
    assert blob[pos:pos + 4] == b"GMIF"
    base = pos + 8
    return [blob[base + a:base + b] for a, b in entries]


def main() -> int:
    rom = open(sys.argv[1], "rb").read()
    fs = read_fs(rom)
    if sys.argv[2] == "--list":
        for p in sorted(fs):
            print(p, fs[p][1] - fs[p][0])
    else:
        a, b = fs[sys.argv[3]]
        open(sys.argv[4], "wb").write(rom[a:b])
    return 0


if __name__ == "__main__":
    sys.exit(main())
