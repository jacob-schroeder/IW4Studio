#!/usr/bin/env python3
"""Extract the PS3 mp_swamp GfxWorld draw streams for an IW4 BSP import."""

import argparse
import hashlib
import json
import math
import mmap
import re
import struct
from pathlib import Path


WORLD = 0x147560F
POSITIONS = 0x1BBA15F
SURFACES = 0x27BC03D
SURFACE_BOUNDS = 0x27E895D
STATIC_MODEL_BOUNDS = 0x279C05D
STATIC_MODEL_INSTANCES = 0x281B85D
STATIC_MODEL_ALIAS_BASE = 0x409FDB79
STATIC_MODEL_STRIDE = 64
STATIC_MODEL_ROOTS_END = 0x3DE4D00
EXTERNAL_STATIC_MODELS = {
    0x40008765: (0xDA74A9, "swamp_thorn_bush_01"),
    0x4000876D: (0xDBB264, "foliage_ls_ivy_flat_dying_01"),
}
SOURCE_SHA256 = "101c19b32f44f996a43d4bc277b761c656d30b7b4615c2360fad0a5007d8d6f8"

SOURCE_SURFACE = struct.Struct(">IIIHHII4B")
SOURCE_POSITION = struct.Struct(">4f")
SOURCE_LAYER_PREFIX = struct.Struct(">4B4e2I")
BSP_VERTEX = struct.Struct("<3f3f4B2f2f3f3f")
XMODEL_ROOT_SIZE = 0x128
XMODEL_HEADER = re.compile(rb"\xff{4}[\x01-\xff]{3}.\x3f\x80\x00\x00", re.DOTALL)
XMODEL_NAME = re.compile(rb"[A-Za-z0-9_/.,+*-]{2,140}\x00")


def packed_axis(word):
    def signed(value, bits):
        sign = 1 << (bits - 1)
        return (value ^ sign) - sign

    return (
        signed(word & 0x7FF, 11),
        signed((word >> 11) & 0x7FF, 11),
        signed(word >> 22, 10),
    )


def world_direction(word):
    x, y, z = packed_axis(word)
    z *= 2
    length = math.sqrt(x * x + y * y + z * z)
    if length == 0:
        return (0.0, 0.0, 0.0)
    return (x / length, y / length, z / length)


def binormal(normal, tangent, sign):
    nx, ny, nz = normal
    tx, ty, tz = tangent
    return (
        sign * (ny * tz - nz * ty),
        sign * (nz * tx - nx * tz),
        sign * (nx * ty - ny * tx),
    )


def placement_axis(word):
    x, y, z = packed_axis(word)
    return (x / 1023, y / 1023, z / 511)


def static_model_roots(data, start):
    for match in XMODEL_HEADER.finditer(data, start, STATIC_MODEL_ROOTS_END):
        offset = match.start()
        if data[offset + 5] > data[offset + 4] or not 1 <= data[offset + 0xE5] <= 4:
            continue
        name = XMODEL_NAME.match(data, offset + XMODEL_ROOT_SIZE)
        if name:
            yield offset, name[0][:-1].decode("ascii")


def static_models(data):
    (count,) = struct.unpack_from(">I", data, WORLD + 0x3A8)
    instances_end = STATIC_MODEL_INSTANCES + count * STATIC_MODEL_STRIDE
    instances = [
        struct.unpack_from(">3f3IfI", data, offset)
        for offset in range(STATIC_MODEL_INSTANCES, instances_end, STATIC_MODEL_STRIDE)
    ]
    inline_instances = [i for i, row in enumerate(instances) if row[-1] == 0xFFFFFFFF]
    roots = list(static_model_roots(data, instances_end))
    if len(inline_instances) != len(roots):
        raise ValueError(f"Found {len(roots)} XModel roots for {len(inline_instances)} inline static models")

    # Each local alias addresses the model pointer in the first placement
    # that loaded that XModel inline.
    model_by_alias = EXTERNAL_STATIC_MODELS | {
        STATIC_MODEL_ALIAS_BASE + i * STATIC_MODEL_STRIDE: model
        for i, model in zip(inline_instances, roots)
    }
    placements = []
    for i, (x, y, z, a, b, c, scale, source_ref) in enumerate(instances):
        alias = STATIC_MODEL_ALIAS_BASE + i * STATIC_MODEL_STRIDE if source_ref == 0xFFFFFFFF else source_ref
        model_source_offset, model_name = model_by_alias[alias]
        bounds_and_light = struct.unpack_from(">9f", data, STATIC_MODEL_BOUNDS + i * 36)
        draw_row = STATIC_MODEL_INSTANCES + i * STATIC_MODEL_STRIDE
        ground_lighting = struct.unpack_from(">4B", data, draw_row + 32)
        cull_distance, source_flags = struct.unpack_from(">2H", data, draw_row + 54)
        placements.append({
            "source_instance_index": i,
            "source_model_ref": f"0x{source_ref:08x}",
            "model_name": model_name,
            "model_source_offset": model_source_offset,
            "origin": [x, y, z],
            "axis": [placement_axis(word) for word in (a, b, c)],
            "scale": scale,
            "ground_lighting_rgba": ground_lighting,
            "source_flags": source_flags,
            "source_primary_light": data[draw_row + 60],
            "source_reflection_probe": data[draw_row + 61],
            "bounds_center": bounds_and_light[:3],
            "bounds_half_size": bounds_and_light[3:6],
            "lighting_origin": bounds_and_light[6:],
            "cull_distance": cull_distance,
        })
    return placements


def extract(data, output):
    if hashlib.sha256(data).hexdigest() != SOURCE_SHA256:
        raise ValueError("This reader requires the decoded PS3 mp_swamp zone revision it was recovered from")

    (surface_count,) = struct.unpack_from(">I", data, WORLD + 0x14)
    (vertex_count,) = struct.unpack_from(">I", data, WORLD + 0x8C)
    (layer_size,) = struct.unpack_from(">I", data, WORLD + 0xA0)
    (index_count,) = struct.unpack_from(">I", data, WORLD + 0xB0)
    if (surface_count, vertex_count, layer_size, index_count) != (6520, 211274, 5078496, 447765):
        raise ValueError("The decoded zone does not have the expected mp_swamp world header")

    layers = POSITIONS + vertex_count * SOURCE_POSITION.size
    indices = layers + layer_size
    surface_rows = list(SOURCE_SURFACE.iter_unpack(data[SURFACES:SURFACE_BOUNDS]))
    source_indices = struct.unpack_from(f">{index_count}H", data, indices)

    batches = sorted({(row[0], row[1]) for row in surface_rows})
    batch_end = (layer_size, vertex_count)
    layer_strides = {}
    vertices = bytearray(vertex_count * BSP_VERTEX.size)
    for (layer_start, first), (layer_end, end) in zip(batches, batches[1:] + [batch_end]):
        stride, remainder = divmod(layer_end - layer_start, end - first)
        if stride < SOURCE_LAYER_PREFIX.size or remainder:
            raise ValueError(f"Invalid vertex layer stride for batch starting at {first}")
        layer_strides[layer_start, first] = stride
        for batch_vertex, vertex_number in enumerate(range(first, end)):
            position = SOURCE_POSITION.unpack_from(data, POSITIONS + vertex_number * SOURCE_POSITION.size)
            layer = SOURCE_LAYER_PREFIX.unpack_from(data, layers + layer_start + batch_vertex * stride)
            red, green, blue, alpha, u, v, lu, lv, normal_word, tangent_word = layer
            normal = world_direction(normal_word)
            tangent = world_direction(tangent_word)
            BSP_VERTEX.pack_into(
                vertices, vertex_number * BSP_VERTEX.size, *position[:3], *normal,
                blue, green, red, alpha, u, v, lu, lv, *tangent,
                *binormal(normal, tangent, position[3]),
            )

    rebased = [0] * index_count
    surfaces = []
    for n, surface in enumerate(surface_rows):
        layer_start, batch_first, local_first, vertices_in_surface, triangles, first_index, material, *lighting = surface
        count = triangles * 3
        surface_indices = source_indices[first_index:first_index + count]
        if not all(local_first <= i < local_first + vertices_in_surface for i in surface_indices):
            raise ValueError(f"World surface {n} contains an out-of-range index")
        rebased[first_index:first_index + count] = [index - local_first for index in surface_indices]
        surfaces.append({
            "source_offset": SURFACES + n * SOURCE_SURFACE.size,
            "source_vertex_layer_offset": layer_start,
            "source_vertex_layer_stride": layer_strides[layer_start, batch_first],
            "source_batch_first_vertex": batch_first,
            "source_surface_first_vertex": local_first,
            "first_vertex": batch_first + local_first,
            "vertex_count": vertices_in_surface,
            "first_index": first_index,
            "index_count": count,
            "material_ref": f"0x{material:08x}",
            "lighting_and_flags_bytes": lighting,
        })

    placements = static_models(data)
    output.mkdir(parents=True, exist_ok=True)
    (output / "drawverts-le.bin").write_bytes(vertices)
    (output / "drawindices-le.bin").write_bytes(struct.pack(f"<{index_count}H", *rebased))
    (output / "drawsurfaces.json").write_text(json.dumps({
        "source_world_offset": WORLD,
        "source_positions_offset": POSITIONS,
        "source_vertex_layers_offset": layers,
        "source_indices_offset": indices,
        "source_surfaces_offset": SURFACES,
        "source_surface_bounds_offset": SURFACE_BOUNDS,
        "source_surface_stride": SOURCE_SURFACE.size,
        "vertex_count": vertex_count,
        "index_count": index_count,
        "vertex_stride": BSP_VERTEX.size,
        "vertex_format": "<3f3f4B2f2f3f3f position, normal, BGRA, base UV, lightmap UV, tangent, binormal",
        "index_format": "little-endian uint16, surface-local, original winding",
        "surfaces": surfaces,
    }, separators=(",", ":")) + "\n")
    (output / "staticmodels.json").write_text(json.dumps({
        "axis_format": "local X, Y, Z basis vectors in world coordinates",
        "placements": placements,
    }, separators=(",", ":"), allow_nan=False) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    with args.zone.open("rb") as source, mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as zone:
        extract(zone, args.output)
