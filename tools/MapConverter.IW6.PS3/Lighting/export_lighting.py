#!/usr/bin/env python3
"""Extract mp_swamp's native lightmaps, outdoor lookup, and reflection probes."""

import argparse
import json
import mmap
import struct
from pathlib import Path


LIGHTMAP_OFFSETS = (0x15BA097, 0x17BA0FA)
LIGHTMAP_NAMES = ("*lightmap0_primary", "*lightmap0_secondary")
REFLECTION_ORIGINS = 0x15BA003
REFLECTION_OFFSET = 0x14D964E
IMAGE_SIZE = 0x50
REFLECTION_FACE_STRIDE = 21888
REFLECTION_MIP_SIZES = tuple(64 >> mip for mip in range(7))
REFLECTION_PIXEL_COUNT = 131064


def image(data, offset, name, expected):
    fmt, levels, dimensions, cube, remap, width, height, depth = struct.unpack_from(">4BI3H", data, offset)
    if (fmt, levels, dimensions, cube, remap, width, height, depth) != expected:
        raise ValueError(f"Unexpected image descriptor for {name}")
    (size,) = struct.unpack_from(">I", data, offset + 0x1C)
    (payload_ref,) = struct.unpack_from(">I", data, offset + 0x28)
    if payload_ref != 0xFFFFFFFF:
        raise ValueError(f"Baked image {name} is missing inline pixels")
    encoded = name.encode("ascii") + b"\0"
    name_offset = offset + IMAGE_SIZE
    pixels_offset = name_offset + len(encoded)
    if data[name_offset:pixels_offset] != encoded:
        raise ValueError(f"Missing baked image {name}")
    return data[pixels_offset:pixels_offset + size]


def write_baked_images(data, library):
    directory = library / "images"
    directory.mkdir(parents=True, exist_ok=True)
    descriptions = (
        (LIGHTMAP_OFFSETS[0], LIGHTMAP_NAMES[0], 0x81, 0x1A9FF, 1024, 2048, 1, 2),
        (LIGHTMAP_OFFSETS[1], LIGHTMAP_NAMES[1], 0x85, 0x1AAE4, 512, 2048, 4, 2),
        (0x27543B4, "$outdoor", 0x81, 0x1A9FF, 512, 512, 1, 1),
    )
    no_stream = {"width": 0, "height": 0, "levelSizeAndOffset": 0}
    for offset, name, fmt, remap, width, height, channels, category in descriptions:
        payload = image(data, offset, name, (fmt, 1, 2, 0, remap, width, height, 1))
        if len(payload) != width * height * channels:
            raise ValueError(f"Incomplete baked image {name}")
        metadata = {
            "format": "iw4-image-source",
            "version": 2,
            "name": name,
            "imageFormat": fmt,
            "levelCount": 1,
            "dimensionCount": 2,
            "multiFaceControl": 0,
            "textureControl1": remap,
            "width": width,
            "height": height,
            "depth": 1,
            "memoryLocation": 0,
            "minLodControl": 0,
            "renderTargetPitch": 0,
            "pixelsOffset": 0,
            "mapType": 3,
            "semantic": 1,
            "category": category,
            "useSrgbReads": 0,
            "cardMemory": len(payload),
            "baseWidth": width,
            "baseHeight": height,
            "baseDepth": 1,
            "baseLevelCount": 1,
            "cached": 0,
            "streamData": [no_stream] * 4,
            "payloadByteCount": len(payload),
        }
        stem = name.replace("*", "_")
        (directory / f"{stem}.image.json").write_text(json.dumps(metadata, indent=2) + "\n")
        (directory / f"{stem}.pixels.bin").write_bytes(payload)


def reflection_mip_bgra(source, edge):
    # Cubemap faces are square, so X and Y bits alternate in RSX storage.
    spread = [sum(((v >> bit) & 1) << (2 * bit) for bit in range(6)) for v in range(edge)]
    linear = bytearray(edge * edge * 4)
    for y in range(edge):
        for x in range(edge):
            src = 4 * (spread[x] | (spread[y] << 1))
            dst = 4 * (y * edge + x)
            linear[dst:dst + 4] = source[src:src + 4][::-1]
    return linear


def reflection_probes(data):
    images = []
    cursor = REFLECTION_OFFSET
    for index in range(7):
        name = f"*reflection_probe{index}"
        payload = image(data, cursor, name, (0x85, 7, 2, 1, 0x1AAE4, 64, 64, 1))
        if len(payload) != REFLECTION_FACE_STRIDE * 6:
            raise ValueError(f"Incomplete reflection probe {name}")
        images.append(payload)
        cursor += IMAGE_SIZE + len(name) + 1 + len(payload)
    if cursor != REFLECTION_ORIGINS:
        raise ValueError("Unexpected reflection probe origin table offset")

    rows = bytearray()
    # Both engines reserve probe zero for a red diagnostic cube. The IW4
    # linker supplies its own default and keeps it out of authored selection.
    for index in range(1, len(images)):
        x, y, z, volume_pointer, volume_count = struct.unpack_from(">3f2I", data, cursor + index * 20)
        if volume_pointer or volume_count:
            raise ValueError(f"Unrecognized probe volumes on probe {index}")
        payload = images[index]
        row = bytearray(76 + REFLECTION_PIXEL_COUNT)
        struct.pack_into("<3f", row, 0, x, y, z)
        top_mip_size = 64 * 64 * 4
        tail_size = 21844 - top_mip_size
        for face in range(6):
            source = face * REFLECTION_FACE_STRIDE
            tail = 76 + top_mip_size * 6 + face * tail_size
            for mip, edge in enumerate(REFLECTION_MIP_SIZES):
                size = edge * edge * 4
                start = 76 + face * top_mip_size if mip == 0 else tail
                row[start:start + size] = reflection_mip_bgra(payload[source:source + size], edge)
                source += size
                if mip:
                    tail += size
        rows.extend(row)
    return rows


def extract(zone, library, output):
    reflections = reflection_probes(zone)
    write_baked_images(zone, library)
    if struct.unpack_from(">I", zone, 0x147560f + 0x378)[0] != 0xfffffffe:
        raise ValueError("Expected the owned mp_swamp outdoor lookup image")
    matrix = struct.unpack_from(">16f", zone, 0x147560f + 0x338)
    output.mkdir(parents=True, exist_ok=True)
    (output / "29-reflection-probes.bin").write_bytes(reflections)
    (output / "lighting.json").write_text(json.dumps({
        "linker_args": ["--lightmap", *LIGHTMAP_NAMES, "--outdoor-image", "$outdoor",
                        "--outdoor-lookup-matrix", ",".join(format(x, ".9g") for x in matrix)],
        "lumps": {"0x29": "29-reflection-probes.bin"},
        "reflection_probe_indices": list(range(7)),
    }, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("asset_library", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    with args.zone.open("rb") as source, mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as zone:
        extract(zone, args.asset_library, args.output)
