"""Read the big-endian signed retail PS3 IW6 container and resident loading image."""

import json
from pathlib import Path
import struct
import zlib


def inflate(source, destination):
    data = Path(source).read_bytes()
    if data[:12] != b"IWff0100\x00\x00\x02\x2e":
        raise ValueError(f"{source} is not a retail PS3 IW6 version 558 fastfile")
    stream_entries = struct.unpack_from(">I", data, 24)[0]
    signature = 36 + stream_entries * 12
    if data[signature:signature + 8] != b"IWffS100":
        raise ValueError(f"Invalid IW6 PS3 zone header at {signature:#x}")
    pos = signature + 0x20000
    with destination.open("xb") as out:
        while pos < len(data):
            size = struct.unpack_from(">H", data, pos)[0]
            pos += 2
            length = size or 65536
            payload = data[pos:pos + length]
            if len(payload) != length:
                raise ValueError(f"Truncated IW6 frame at {pos:#x}")
            if size:
                decoder = zlib.decompressobj(-15)
                payload = decoder.decompress(payload, 65537)
                if not decoder.eof or decoder.unused_data != struct.pack(">I", zlib.adler32(payload)):
                    raise ValueError(f"Malformed IW6 deflate stream at {pos:#x}")
            if len(payload) != 65536:
                raise ValueError(f"Invalid IW6 frame output at {pos:#x}")
            out.write(payload)
            pos += length


def resident_loadscreen(zone, map_name, library):
    source_name = f"loadscreen_{map_name}_patch".encode("ascii") + b"\0"
    name_position = zone.index(source_name)
    root = name_position - 80
    if root < 0 or zone.find(source_name, name_position + 1) != -1:
        raise ValueError("Ambiguous load-screen image")
    header = zone[root:name_position]
    fmt, levels, dims, faces, remap, width, height, depth, location, lod, pitch, texture_offset = (
        struct.unpack_from(">4BI3H2B2I", header))
    map_type, semantic, category, flags, size, base_w, base_h, base_d, base_levels, cached = (
        struct.unpack_from(">4BI3H2B", header, 24))
    if (fmt, levels, dims, faces, map_type, semantic, depth, location) != (0xA6, 1, 2, 0, 3, 0, 1, 0):
        raise ValueError("Unsupported load-screen image profile")
    if (width, height, depth, levels) != (base_w, base_h, base_d, base_levels) or (
            size != ((width + 3) // 4) * ((height + 3) // 4) * 8 or flags != 0x80 or
            header[40:] != b'\xff' * 4 + bytes(32) + b'\xff' * 4):
        raise ValueError("Invalid resident load-screen dimensions or stream metadata")
    start = name_position + len(source_name)
    pixels = zone[start:start + size]
    if len(pixels) != size:
        raise ValueError("Truncated loading image")
    name = f"loadscreen_{map_name}_load"
    image_dir = library / "images"
    image_dir.mkdir(exist_ok=True, parents=True)
    metadata = dict(format="iw4-image-source", version=2, name=name, imageFormat=fmt,
                    levelCount=levels, dimensionCount=dims, multiFaceControl=faces,
                    textureControl1=remap, width=width, height=height, depth=depth,
                    memoryLocation=location, minLodControl=lod, renderTargetPitch=pitch,
                    pixelsOffset=texture_offset, mapType=map_type, semantic=semantic,
                    category=category, useSrgbReads=0, cardMemory=size,
                    baseWidth=base_w, baseHeight=base_h, baseDepth=base_d,
                    baseLevelCount=base_levels, cached=cached,
                    streamData=[dict(width=0, height=0, levelSizeAndOffset=0) for _ in range(4)],
                    payloadByteCount=size)
    (image_dir / (name + ".image.json")).write_text(json.dumps(metadata, indent=2))
    (image_dir / (name + ".pixels.bin")).write_bytes(pixels)
    return name
