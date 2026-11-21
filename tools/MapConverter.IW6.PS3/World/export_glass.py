#!/usr/bin/env python3
"""Export the retail PS3 Swamp panes as IW4Studio's v22 glass extension."""

import argparse
import json
from math import isfinite
from pathlib import Path
from struct import iter_unpack, pack, unpack_from
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Materials.export_materials import (  # noqa: E402
    OWNED, Source, Target, bind_constants, sampler_state, emit_images, json_out, stream_table, string, u32,
)

FX_WORLD = 0xE59D5A
GAME_GLASS = 0x3CF81ED
FX_DEFINITION_SIZE = 80
FX_GLASS_SIZE = 112
GAME_GLASS_SIZE = 128
SOURCE_INITIAL_STATE = ">10fIfH6B"
SOURCE_INITIAL_SIZE = 56
SOURCE_LIGHTING = 0xE5ABA6
SOURCE_DEF_HANDLE = 0x4024AD21
MATERIAL_ROOTS = {
    (0, 0): (0xE5A147, "m/mp_ca_flooded_ls_glass_dusty_01", "m/mtl_coffeemug_vodkabottle_inside"),
    (0, 1): (0xE5A420, "m/com_glass_clear_shattered", "m/com_glass_clear_shattered"),
    (2, 0): (0xE5A898, "m/ct_glass02", "m/ct_glass02"),
}
PHYSICS_PRESET = "glass_chunk"


def target_counts(bsp):
    magic, version, count = unpack_from("<4sII", bsp)
    if (magic, version) != (b"IBSP", 22):
        raise ValueError("The target bound must come from an IW4 v22 BSP")
    sizes = dict(iter_unpack("<II", bsp[12:12 + count * 8]))
    if sizes[0x08] % 4 or sizes[0x19] % 112:
        raise ValueError("Invalid target BSP brush or cell extent")
    return sizes[0x08] // 4, sizes[0x19] // 112 + 1


def write_name(data, name):
    encoded = name.encode("ascii")
    data.extend(pack("<i", len(encoded)))
    data.extend(encoded)


def glass_material(target, root, expected, template):
    mat = target.source.material(root)
    if mat.name != expected:
        raise ValueError(f"Expected glass material {expected} at {root:#x}, found {mat.name}")
    material = target.stock_material(template).copy()
    material["sortKey"] = mat.sort
    material["surfaceTypeBits"] = mat.surface_bits
    material["textureAtlas"] = {"rows": mat.atlas[0] or 1, "columns": mat.atlas[1] or 1}
    material["constants"] = bind_constants(mat, material["constants"])
    result = []
    for original in material["textures"]:
        tex = target.source.texture_for_material(mat, original["name"])
        image = target.source.image_for_texture(tex)
        name = original["image"]
        if image is not None:
            source_name, _, _, _ = target.source.image_record(image)
            if source_name is None:
                raise ValueError(f"Unresolved packed glass image on {mat.name}")
            if source_name.startswith(","):
                name = target.stock_image(source_name[1:])
            else:
                target.images[target.source, image] = name = source_name
        else:
            if mat.name == "m/mp_ca_flooded_ls_glass_dusty_01" and original["name"] == "normalMap":
                name = "$identitynormalmap"
            name = target.stock_image(name)
        result.append({**original, "image": name,
                       "samplerState": sampler_state(tex.sampler) if tex else original["samplerState"]})
    material["textures"] = result
    json_out(target.raw / "materials" / (mat.name + ".json"), material)
    return mat.name


def convert_fx_world(zone, target, target_cells):
    header = FX_WORLD + 4
    if u32(zone, FX_WORLD) != 0x4000f2e9:
        raise ValueError("Missing the serialized FxWorld name handle")
    (_, _, def_count, piece_limit, piece_words, pieces, _, _,
     first_free, geo_limit, _, geo_count) = unpack_from(">12I", zone, header)
    if (def_count, pieces, geo_count) != (10, 60, 240):
        raise ValueError("Unexpected retail Swamp FxWorld glass counts")
    if zone[header + 48:header + 100] != b"\xff" * 52:
        raise ValueError("Unexpected retail Swamp glass ownership fields")
    data = bytearray(pack("<8IBH", def_count, piece_limit, piece_words, pieces,
                          target_cells, first_free, geo_limit, geo_count, zone[header + 101], 0))

    defs = header + FX_GLASS_SIZE
    definitions_end = defs + FX_DEFINITION_SIZE * def_count
    physics, _ = string(zone, definitions_end + 0x4c)
    if physics != "," + PHYSICS_PRESET:
        raise ValueError("The expected breakable-glass physics provider was not found")
    target.copy_source(f"physic/{PHYSICS_PRESET}.physic.json")
    inline = {slot: glass_material(target, *values) for slot, values in MATERIAL_ROOTS.items()}
    handles = {SOURCE_DEF_HANDLE + row * FX_DEFINITION_SIZE + 24 + field * 4: name
               for (row, field), name in inline.items()}
    handles[SOURCE_DEF_HANDLE + 32] = PHYSICS_PRESET
    inline[(0, 2)] = PHYSICS_PRESET
    definition_materials = []
    for i in range(def_count):
        pos = defs + i * FX_DEFINITION_SIZE
        shape = unpack_from(">5fI", zone, pos)
        refs = unpack_from(">3I", zone, pos + 24)
        radii = unpack_from(">2f", zone, pos + 64)
        if not all(map(isfinite, (*shape[:5], *radii))):
            raise ValueError(f"Invalid retail glass definition {i}")
        names = tuple(inline.get((i, field)) if ptr == OWNED else handles.get(ptr)
                      for field, ptr in enumerate(refs))
        if any(name is None for name in names):
            raise ValueError(f"Unresolved asset on retail glass definition {i}: {refs!r}")
        data.extend(pack("<5fI", *shape))
        for name in names:
            write_name(data, name)
        data.extend(pack("<2f", *radii))
        definition_materials.append(names)

    initial = SOURCE_LIGHTING + 2 * pieces
    end = initial + SOURCE_INITIAL_SIZE * pieces
    for i in range(pieces):
        offset = initial + SOURCE_INITIAL_SIZE * i
        row = unpack_from(SOURCE_INITIAL_STATE, zone, offset)
        definition, vertices, fans, padding = row[13:17]
        if row[12] != i or definition >= def_count or (vertices, fans) != (4, 0):
            raise ValueError(f"Invalid retail Swamp pane shape or lighting slot {i}")
        if not all(map(isfinite, (*row[:10], row[11]))) or not 0.99 < sum(q * q for q in row[:4]) < 1.01:
            raise ValueError(f"Invalid retail Swamp pane basis at {offset:#x}")
        data.extend(pack("<10fIf4B", *row[:12], definition, vertices, fans, padding))
    for (value,) in iter_unpack(">I", zone[end:end + geo_count * 4]):
        data.extend(pack("<I", value))
    for (value,) in iter_unpack(">H", zone[SOURCE_LIGHTING:initial]):
        data.extend(pack("<H", value))
    return data, pieces, definition_materials


def append_game_glass(data, zone, pieces, brush_count, brush_indices):
    root = GAME_GLASS
    ptr, game_pieces, weaken, destroy, names, names_ptr = unpack_from(">IIHHII", zone, root)
    if (ptr, game_pieces, names, names_ptr) != (OWNED, pieces, 0, OWNED):
        raise ValueError("The retail GameWorld glass table does not match the panes")
    padding = zone[root + 20:root + GAME_GLASS_SIZE]
    start = root + GAME_GLASS_SIZE
    state = zone[start:start + pieces * 12]
    if len(state) != pieces * 12 or any(state):
        raise ValueError("Expected retail initial glass game-state rows")
    data.extend(pack("<iHHii", pieces, weaken, destroy, names, len(padding)))
    data.extend(padding)
    data.extend(state)

    indices = [0] * brush_count
    if sorted(brush_indices.values()) != list(range(1, pieces + 1)):
        raise ValueError("The source collision manifest lacks the exact one-based glass pane indices")
    for key, piece in brush_indices.items():
        index = int(key)
        if not 0 <= index < brush_count:
            raise ValueError(f"Source glass brush {index} does not exist in the target BSP")
        indices[index] = piece
    data.extend(pack("<i", brush_count))
    data.extend(pack(f"<{brush_count}H", *indices))


def glass_lump(fastfile: Path, zone: bytes, brush_count: int, cell_count: int,
               collision_dir: Path, raw: Path, stock: Path,
               base_image_packages: Path) -> bytes:
    """Convert glass for the final BSP brush count and render cell count plus one."""
    source = Source(zone, stream_table(fastfile.read_bytes(), zone))
    manifest = json.loads((collision_dir / "manifest.json").read_bytes())
    target = Target(source, raw, stock)
    data, pieces, materials = convert_fx_world(zone, target, cell_count)
    append_game_glass(data, zone, pieces, brush_count, manifest["source_glass_brush_indices"])
    emit_images(target, (fastfile.parent, base_image_packages))
    json_out(collision_dir / "glass.json", {
        "panes": pieces,
        "definitions": [{"intact": intact, "shattered": shattered, "physics": physics}
                        for intact, shattered, physics in materials],
    })
    return bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fastfile", type=Path)
    parser.add_argument("zone", type=Path)
    parser.add_argument("target_bsp", type=Path)
    parser.add_argument("collision_dir", type=Path)
    parser.add_argument("iw4_library", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--base-image-packages", type=Path, required=True)
    args = parser.parse_args()

    brush_count, cell_count = target_counts(args.target_bsp.read_bytes())
    data = glass_lump(args.fastfile, args.zone.read_bytes(), brush_count, cell_count,
                      args.collision_dir, args.output / "raw", args.iw4_library, args.base_image_packages)
    lump = args.output / "40-glass.bin"
    lump.write_bytes(data)
    print(f"Exported the retail Swamp panes to {lump}")


if __name__ == "__main__":
    main()
