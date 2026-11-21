#!/usr/bin/env python3
"""Convert Fog's authored light-grid samples to native IW4 sparse rows and RGB palettes."""

import argparse
from collections import Counter, defaultdict
from itertools import groupby, product
import json
from pathlib import Path
import struct

from grid_tree import grid_corners
from irradiance import WORLD_LIGHTGRID, palette_color


LUMPS = {
    "0x02": "02-light-grid-entries.bin",
    "0x03": "03-light-grid-colors.bin",
    "0x2c": "2c-light-grid-header.bin",
    "0x2d": "2d-light-grid-rows.bin",
}
PALETTE_TILE_SIZE = 4
PALETTE_CHANNEL_TOLERANCE = 8


def trace_mask(point, voxels):
    # Ghosts checks the lower and upper voxel at the query's floor XY and
    # switches between their masks halfway up the cell (0x548a0c–0x548a98).
    # IW4 has one trace bit for each of the eight ways to reach this sample.
    # Request the actual IW4 sight trace when either half can require it.
    x, y, z = point
    mask = 0
    for dx, dy, dz in product(range(2), repeat=3):
        lower = (x - dx, y - dy, z - dz)
        upper = (lower[0], lower[1], lower[2] + 1)
        if voxels.get(lower, 0) & 1 or voxels.get(upper, 0) & 2:
            mask |= 1 << (dx * 4 + dy * 2 + dz)
    return mask


def compact_palette(palette, entries, palette_groups):
    uses = Counter(color for color, _, _ in struct.iter_unpack("<HBB", entries))
    representative_offsets = defaultdict(list)
    for offsets_by_color in palette_groups.values():
        representatives = []
        # Prefer widely reused colors and compare directly to the retained original.
        for color in sorted(offsets_by_color, key=lambda index: (-uses[index], index)):
            representative = next(
                (candidate for candidate in representatives
                 if all(abs(a - b) <= PALETTE_CHANNEL_TOLERANCE
                        for a, b in zip(palette[color], palette[candidate], strict=True))),
                color,
            )
            if representative == color:
                representatives.append(color)
            representative_offsets[representative].extend(offsets_by_color[color])

    compacted = palette[:2]
    for color, offsets in sorted(representative_offsets.items()):
        index = len(compacted)
        compacted.append(palette[color])
        for offset in offsets:
            struct.pack_into("<H", entries, offset, index)
    return compacted


def encode_rows(columns, palette):
    # For each X row the engine supports one continuous vertical span per Y.
    # Empty columns and rows have native sparse encodings.
    xs = [x for x, y in columns]
    ys = [y for x, y in columns]
    mins = [min(xs), min(ys), min(min(column) for column in columns.values())]
    maxs = [max(xs), max(ys), max(max(column) for column in columns.values())]
    header = bytearray(struct.pack("<6H2I", *mins, *maxs, 0, 1))
    entries, rows = bytearray(), bytearray()
    palette_groups = defaultdict(lambda: defaultdict(list))
    by_x = defaultdict(list)
    for x, y in columns:
        by_x[x].append(y)
    for x in range(mins[0], maxs[0] + 1):
        if x not in by_x:
            header.extend(struct.pack("<H", 0xffff))
            continue
        start = len(rows) // 4
        if start >= 0xffff:
            raise ValueError("Light-grid row offset exceeds IW4's 16-bit word index")
        header.extend(struct.pack("<H", start))
        ymin, ymax = min(by_x[x]), max(by_x[x])
        zmin = min(min(columns[x, y]) for y in by_x[x])
        zmax = max(max(columns[x, y]) for y in by_x[x])
        depth = zmax - zmin + 1
        rows.extend(struct.pack("<4HI", ymin, ymax - ymin + 1, zmin, depth, len(entries) // 4))
        spans = []
        for y in range(ymin, ymax + 1):
            column = columns.get((x, y))
            if not column:
                spans.append((0, 0))
                continue
            zs = sorted(column)
            height = zs[-1] - zs[0] + 1
            if height > 255:
                raise ValueError("Light-grid column exceeds the native span height")
            spans.append((height, zs[0] - zmin))
            i = 0
            for z in range(zs[0], zs[-1] + 1):
                while i + 1 < len(zs) and abs(zs[i+1] - z) < abs(zs[i] - z):
                    i += 1
                color, light, trace = column[zs[i]]
                if color >= 2:
                    tile = (x // PALETTE_TILE_SIZE, y // PALETTE_TILE_SIZE, z, light)
                    palette_groups[tile][color].append(len(entries))
                # A hole between occupied heights has no IW4 representation.
                # Extend the closest authored sample and require a sight trace.
                entries.extend(struct.pack("<HBB", color, light, trace if zs[i] == z else 0xff))
        for (height, zoffset), group in groupby(spans):
            count = sum(1 for _ in group)
            for skip in range(0, count, 255):
                rows.extend((min(255, count - skip), height))
                if height:
                    rows.extend(zoffset.to_bytes(2 if depth > 255 else 1, 'little'))
        rows.extend(bytes(-len(rows) % 4))
    palette = compact_palette(palette, entries, palette_groups)
    return entries, palette, header, rows, mins, maxs


def export_grid(zone, output):
    if struct.unpack_from(">2I", zone, WORLD_LIGHTGRID + 60) != (1, 1):
        raise ValueError("Unsupported Ghosts light-grid format")
    # IW4 extrapolates with the same default and sky slots as Ghosts.
    palette = [palette_color(zone, 0), palette_color(zone, 1)]
    palette_indices = {rgb: i for i, rgb in enumerate(palette)}
    source_indices = {0: 0, 1: 1}
    voxels, samples = {}, {}
    for point, color, primary, voxel in grid_corners(zone):
        if voxel:
            voxels[point] = voxel
        if color:
            samples[point] = (color, primary)
    columns = defaultdict(dict)
    for point, (color, primary) in samples.items():
        if color not in source_indices:
            rgb = palette_color(zone, color)
            index = palette_indices.setdefault(rgb, len(palette))
            if index == len(palette):
                palette.append(rgb)
            source_indices[color] = index
        x, y, z = point
        columns[x, y][z] = source_indices[color], primary, trace_mask(point, voxels)
    if len(palette) >= 0xffff:
        raise ValueError("Fog's palette exceeds IW4's index range")
    entries, palette, header, rows, mins, maxs = encode_rows(columns, palette)
    output.mkdir(parents=True, exist_ok=True)
    for filename, payload in zip(LUMPS.values(), (entries, b''.join(palette), header, rows), strict=True):
        (output / filename).write_bytes(payload)
    metadata = {
        "method": "Ghosts octree, directional irradiance and primary lights",
        "sampled_lattice_cells": len(samples), "stored_grid_entries": len(entries) // 4,
        "palette_colors": len(palette), "bounds": [mins, maxs], "lumps": LUMPS,
    }
    (output / "grid.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(samples):,} Ghosts light-grid samples and {len(palette):,} IW4 palette colors.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    export_grid(args.zone.read_bytes(), args.output)
