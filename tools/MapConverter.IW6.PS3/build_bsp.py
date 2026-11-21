"""Assemble recovered IW6 PS3 map data for the native IW4 v22 disk linker."""

import json
import math
import re
import struct
from World.export_glass import glass_lump
from World.export_world import SOURCE_LAYER_PREFIX, WORLD
from World.visibility import render_spatial_lumps


PS3_VERTEX_LAYERS = b"IW4PS3L\x01"


def read_primary_lights(zone):
    # ComWorld is inline at the first occurrence of the owned map name. Its
    # 80-byte ComPrimaryLight adds an up vector to the IW4 light fields.
    name = b"maps/mp/mp_swamp.d3dbsp\0"
    pos = zone.index(name)
    owner, in_use, count, lights = struct.unpack_from(">4I", zone, pos - 16)
    if (owner, in_use, count, lights) != (0xffffffff, 1, 26, 0xffffffff):
        raise ValueError("Unrecognized IW6 ComWorld header")
    pos += len(name)
    names = pos + 80 * count
    attenuation = b"light_point_linear"
    if not zone.startswith(attenuation + b"\0", names):
        raise ValueError("Unsupported primary-light attenuation")
    rows = bytearray()
    for index in range(count):
        kind, shadow, exponent, _, *fields, pointer = struct.unpack_from(">4B18fI", zone, pos + 80 * index)
        if kind > 3 or not all(map(math.isfinite, fields)):
            raise ValueError(f"Invalid primary light {index}")
        if kind > 1 and pointer not in (0xffffffff, 0x4000fb21):
            raise ValueError(f"Unsupported light definition on primary light {index}")
        row = bytearray(128)
        struct.pack_into("<BB2x12fI2f", row, 0, kind, shadow, *fields[0:6],
                         *fields[9:12], *fields[12:15], exponent, *fields[16:18])
        if kind > 1:
            row[64:64 + len(attenuation)] = attenuation
        rows.extend(row)
    return rows


def add_trigger_models(zone, lumps, entities):
    # BSP v22 encodes each linked MapTriggers model as a brush submodel.
    # The IW4 linker rebuilds native trigger models, hulls and slabs from these.
    root = 0x3cd4b73
    nchars, nmodels, _, nhulls, _, nslabs, _ = struct.unpack_from(">7I", zone, root + 8)
    p = root + 128 + nchars
    models = list(struct.iter_unpack(">IHH", zone[p:p+8*nmodels]))
    p += 8*nmodels
    hulls = list(struct.iter_unpack(">6fIHH", zone[p:p+32*nhulls]))
    p += 32*nhulls
    slabs = list(struct.iter_unpack(">5f", zone[p:p+20*nslabs]))
    by_contents = {contents: i for i, (_, _, contents) in enumerate(struct.iter_unpack("<64sII", lumps[0]))}
    references = {}
    pattern = r'"model" "\?([0-9]+)"'
    for idx in sorted({int(n) for n in re.findall(pattern, entities)}):
        _, count, first = models[idx]
        if not count or first + count > len(hulls):
            raise ValueError(f"Invalid hull range for trigger {idx}")
        target = len(lumps[0x25]) // 48
        references[idx] = target
        first_brush = len(lumps[8]) // 4
        lows, highs = [], []
        for *bounds, contents, ns, start in hulls[first:first+count]:
            if contents not in by_contents:
                by_contents[contents] = len(lumps[0]) // 72
                lumps[0] += struct.pack("<64sII", f"trigger_{contents:08x}".encode(), 0, contents)
            material = by_contents[contents]
            low = [bounds[i]-bounds[i+3] for i in range(3)]
            high = [bounds[i]+bounds[i+3] for i in range(3)]
            lows.append(low)
            highs.append(high)
            for a,b in zip(low, high):
                lumps[5] += struct.pack("<fifi", a, material, b, material)
            if start + ns > len(slabs):
                raise ValueError(f"Invalid slab range for trigger {idx}")
            for x,y,z,mid,half in slabs[start:start+ns]:
                plane = len(lumps[4]) // 16
                lumps[4] += struct.pack("<8f", x,y,z,mid+half, -x,-y,-z,half-mid)
                lumps[5] += struct.pack("<4i", plane,material,plane+1,material)
            side_count = 6 + ns*2
            lumps[6] += bytes(side_count)
            lumps[8] += struct.pack("<hh", side_count, material)
        lumps[0x25] += struct.pack("<6f8x4i", *map(min, zip(*lows)), *map(max, zip(*highs)),
                                   0, 0, first_brush, count)
    return re.sub(pattern, lambda m: f'"model" "*{references[int(m[1])]}"', entities)


def pack_terrain_vertices(zone, layers_offset, row, layers, target_stride):
    stride = row["source_vertex_layer_stride"]
    first = layers_offset + row["source_vertex_layer_offset"] + row["source_surface_first_vertex"] * stride
    uv_bytes = 8 * (layers - 1)
    if (stride != target_stride - 8 or
            not SOURCE_LAYER_PREFIX.size + uv_bytes <= stride <= SOURCE_LAYER_PREFIX.size + uv_bytes + 8):
        raise ValueError(f"Incompatible terrain vertex stride for {row['material_ref']}")
    # Both PS3 layouts use RGBA weights, packed normals and tangents, big-endian
    # secondary UV pairs, and packed additional normal transforms. IW4 expands
    # the base and lightmap UVs from half floats to full floats.
    packed = bytearray()
    for start in range(first, first + row["vertex_count"] * stride, stride):
        extra = start + SOURCE_LAYER_PREFIX.size
        _, _, _, _, u, v, lu, lv, _, _ = SOURCE_LAYER_PREFIX.unpack_from(zone, start)
        if not all(map(math.isfinite, (u, v, lu, lv, *struct.unpack_from(f">{2 * (layers - 1)}f", zone, extra)))):
            raise ValueError(f"Non-finite terrain UV for {row['material_ref']}")
        packed.extend(zone[start:start + 4])
        packed.extend(struct.pack(">4f", u, v, lu, lv))
        packed.extend(zone[start + 12:start + stride])
    return packed


def assemble(zone, world_dir, collision_dir, material_names, entities, output, lighting_dir,
             fastfile, raw, stock, base_image_packages, terrain):
    lumps = {int(path.name[:2], 16): path.read_bytes() for path in collision_dir.glob("??-*.bin")}
    lighting = json.loads((lighting_dir / "lighting.json").read_text())
    for kind, filename in lighting["lumps"].items():
        lumps[int(kind, 16)] = (lighting_dir / filename).read_bytes()
    world = json.loads((world_dir / "drawsurfaces.json").read_text())
    surfaces = world["surfaces"]
    verts = bytearray((world_dir / "drawverts-le.bin").read_bytes())
    indices = (world_dir / "drawindices-le.bin").read_bytes()
    materials = bytearray(lumps[0])
    model_rows = bytearray(lumps[0x25])
    indices_by_name = {}
    soups = bytearray()
    layer_data = bytearray()

    def append_surface(name, row, flags, layer_offset):
        if name not in indices_by_name:
            native_name = name.encode("ascii")
            if len(native_name) >= 64:
                raise ValueError(f"Material name exceeds the IW4 BSP field: {name}")
            indices_by_name[name] = len(materials) // 72
            materials.extend(struct.pack("<64sII", native_name, 0, 0))
        lm, probe, light, _ = row["lighting_and_flags_bytes"]
        soup = bytearray(24)
        probe = lighting["reflection_probe_indices"][probe]
        struct.pack_into("<HBBBB", soup, 0, indices_by_name[name], lm, probe, light, flags)
        struct.pack_into("<iIHHI", soup, 8, layer_offset, row["first_vertex"], row["vertex_count"],
                         row["index_count"], row["first_index"])
        soups.extend(soup)

    surface_boundaries = [0]
    for row in surfaces:
        reference = row["material_ref"]
        name = material_names[reference]
        # This Ghosts compiler material has no camera pass in IW4. Collision
        # comes from the clipmap and its brush-model indices are independent.
        if name != "w/caulk_shadow":
            _, _, _, flags = row["lighting_and_flags_bytes"]
            layer_offset = -1
            if reference in terrain:
                binding = terrain[reference]
                layer_offset = len(layer_data)
                layer_data.extend(pack_terrain_vertices(zone, world["source_vertex_layers_offset"],
                                                       row, binding["layers"], binding["stride"]))
            append_surface(name, row, flags & 1, layer_offset)
        surface_boundaries.append(len(soups) // 24)

    # The native 44 GfxBrushModel records directly precede MaterialMemory.
    # Clip and render models share indices. Keep the collider ranges intact.
    model_count = struct.unpack_from(">I", zone, WORLD + 0x2ac)[0]
    if len(model_rows) != 48 * model_count:
        raise ValueError("IW6 render and collision brush model counts differ")
    models = 0x26bac2f - model_count * 56
    world_first, world_surface_count = struct.unpack_from(">HH", zone, models + 52)
    if world_first or world_surface_count > len(surfaces):
        raise ValueError("IW6 world model does not address the leading world surfaces")
    for i in range(model_count):
        first, count = struct.unpack_from(">HH", zone, models + i * 56 + 52)
        if count and first + count > len(surfaces):
            raise ValueError(f"Invalid surface range on brush model {i}")
        if count:
            count = surface_boundaries[first + count] - surface_boundaries[first]
            first = surface_boundaries[first]
        struct.pack_into("<H", model_rows, 48*i + 24, first)
        struct.pack_into("<H", model_rows, 48*i + 28, count)
    model_export = json.loads((raw / "static-model-export.json").read_text(encoding="utf-8"))
    lumps.update(render_spatial_lumps(zone, world_surface_count, surface_boundaries,
                                     lighting["reflection_probe_indices"], model_export))
    lumps.update({0: materials, 9: soups, 10: verts, 11: indices,
                  0x18: struct.pack("<III", 0, surface_boundaries[-1], 0),
                  0x25: model_rows,
                  0x2b: read_primary_lights(zone)})
    if layer_data:
        lumps[0x2a] = PS3_VERTEX_LAYERS + layer_data
    entities = add_trigger_models(zone, lumps, entities)
    lumps[0x27] = entities.encode("ascii").rstrip(b"\0") + b"\0"
    lumps[0x40] = glass_lump(fastfile, zone, len(lumps[8]) // 4, 1 + len(lumps[0x19]) // 112,
                             collision_dir, raw, stock, base_image_packages)
    grid = json.loads((lighting_dir / "grid.json").read_text())
    for kind, filename in grid["lumps"].items():
        lumps[int(kind, 16)] = (lighting_dir / filename).read_bytes()
    with output.open("wb") as stream:
        stream.write(struct.pack("<4sII", b"IBSP", 22, len(lumps)))
        for kind, data in sorted(lumps.items()):
            stream.write(struct.pack("<II", kind, len(data)))
        for _, data in sorted(lumps.items()):
            stream.write(data)
            stream.write(bytes(-len(data) % 4))
