"""Johto/Kanto overworld tile data from the ROM and decomp: behaviours, collision, matrix layout.

Tile attribute (land_data member, offset 0x14, 32x32 u16, row = z): low byte = metatile behaviour
(include/constants/metatile_behavior.h enum), bit 15 = blocked (checked against known water/building tiles in
map_reach.py output). Matrix bins: files/fielddata/mapmatrix/map_matrix/map_matrix_NNNN*.bin
(src/map_matrix.c:34-58: width, height, has_headers, has_altitudes, name_len, name, [headers u16], [altitudes u8], models u16).
"""
from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import nds_files  # noqa: E402


def behaviours(decomp: Path) -> tuple[dict[str, int], set[int]]:
    text = (decomp / "include/constants/metatile_behavior.h").read_text(encoding="utf-8")
    body = text[text.index("enum"):]
    names, n = {}, 0
    for m in re.finditer(r"^\s*(TILE_BEHAVIOR_\w+)(?:\s*=\s*(\w+))?\s*,", body, re.M):
        if m.group(2):
            n = int(m.group(2), 0)
        names[m.group(1)] = n
        n += 1
    src = (decomp / "src/metatile_behavior.c").read_text(encoding="utf-8")
    flags = src[src.index("sMetatileBehaviorFlags"):src.index("};", src.index("sMetatileBehaviorFlags"))]
    surf = set()
    for m in re.finditer(r"\[(TILE_BEHAVIOR_\w+)\]\s*=\s*([A-Z_|\s]+),", flags):
        if "SURFABLE" in m.group(2) and m.group(1) in names:
            surf.add(names[m.group(1)])
    return names, surf


def load_land(rom_path: str) -> list[bytes]:
    rom = open(rom_path, "rb").read()
    fs = nds_files.read_fs(rom)
    a, b = fs["a/0/6/5"]
    return nds_files.narc_members(rom[a:b])


def load_matrix(decomp: Path, idx: int):
    f = next((decomp / "files/fielddata/mapmatrix/map_matrix").glob(f"map_matrix_{idx:04d}*.bin"))
    d = f.read_bytes()
    w, h, hh, ha, nl = d[0], d[1], d[2], d[3], d[4]
    p = 5 + nl
    headers = None
    if hh:
        headers = struct.unpack_from(f"<{w*h}H", d, p)
        p += 2 * w * h
    if ha:
        p += w * h
    models = struct.unpack_from(f"<{w*h}H", d, p)
    return w, h, headers, models


def tile_grid(land, w, h, models):
    """dict (x, z) -> attribute u16 over the whole matrix."""
    out = {}
    for cz in range(h):
        for cx in range(w):
            mi = models[cz * w + cx]
            if mi >= len(land):
                continue
            blk = land[mi]
            attrs = struct.unpack_from("<1024H", blk, 0x14)
            for t in range(1024):
                out[(cx * 32 + t % 32, cz * 32 + t // 32)] = attrs[t]
    return out
