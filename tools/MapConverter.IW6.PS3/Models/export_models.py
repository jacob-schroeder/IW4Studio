"""Export IW6 PS3 Fog models for the native IW4 XModel source compiler."""

import argparse
import base64
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import subprocess


MP_SWAMP_SHA256 = "101c19b32f44f996a43d4bc277b761c656d30b7b4615c2360fad0a5007d8d6f8"
INLINE = (0xFFFFFFFF, 0xFFFFFFFE)
LOD_HEADER = re.compile(
    rb"(?=\xff{8}\x00([\x01-\xff])\x00\x00.{24}([A-Za-z0-9_./-]{3,155})\x00)",
    re.DOTALL,
)
SAFE_NAME = re.compile(r"[A-Za-z0-9_./-]+")
FALLBACK_MATERIAL = "m/mtl_plastic_case_dummy"
TARGET_PART_BITS = [0] * 6
MAX_STATIC_LOD_SURFACES = 16
VERTS1_IN_LARGE = 0x02
MYERS_BODY = "mp_body_iw6_mmyers"
IW4_PLAYER_BODY = "mp_body_airborne_assault_a"
MP_SWAMP_EFFECT_MODELS = {
    0xB18F50: "crocodile_stat_float",
    0xB6E929: "black_widow_descend",
    0xB91DE1: "owl_fly_wing_mid",
    0xB9761C: "owl_fly_wing_up",
    0xB9BF1F: "owl_fly_wing_dwn",
    0xBA0FA9: "owl_fly_wing_mu",
    0xBA5018: "owl_fly_wing_md",
    0xBAB0E2: "owl_sit_body",
    0xBAF870: "owl_sit_head",
}
MP_SWAMP_BREACH_MODELS = {
    0xDA203E: (0xDA74A9, "swamp_cave_planks"),
    0xE35CC5: (0xE91EFF, "swamp_wood_basement_door"),
}
MP_SWAMP_REQUIRED_MODELS = {
    0xA023CB: (0xA1052F, "viewmodel_axe_iw6", "iw6/mp_swamp/viewmodel_axe_iw6"),
    0xA10750: (0xA1CFB3, "weapon_axe_iw6", "iw6/mp_swamp/weapon_axe_iw6"),
    0xA48234: (0xAAEA99, "mp_fullbody_mp_mmyers_a", MYERS_BODY),
    0xAAEA99: (0xACB044, "viewhands_mp_mmyers", "vh_iw6_mmyers"),
}
MP_SWAMP_AXE_ANIMATIONS = (
    (0xA1D369, 0xA1F714, "viewmodel_axe_idle"),
    (0xA1F714, 0xA23359, "viewmodel_axe_melee_swipe"),
    (0xA23359, 0xA24A57, "viewmodel_axe_raise"),
    (0xA24A57, 0xA259DA, "viewmodel_axe_drop"),
    (0xA259DA, 0xA26A87, "viewmodel_axe_quick_raise"),
    (0xA26A87, 0xA277C4, "viewmodel_axe_sprint_in"),
    (0xA277C4, 0xA287BF, "viewmodel_axe_sprint_loop"),
    (0xA287BF, 0xA2940A, "viewmodel_axe_sprint_out"),
)
MP_SWAMP_SCENERY_MODELS = {
    0x407ACD0: "dub_gametable_playing_card_deck_dyn",
    0x407B8FB: "dub_gametable_playing_cards_js_dyn",
    0x407BC35: "dub_gametable_playing_cards_kd_dyn",
    0x407BF2F: "sn_cleaning_bottles_03",
    0x407F9D4: "sn_cleaning_bottles_02",
    0x40826E2: "chair_folding_iw6",
}
# Shared positions, vertex layers, and indices whose packed pointers were
# matched to their original inline providers in the fingerprinted zone.
MP_SWAMP_SHARED_STREAMS = {
    # The character's second LOD reuses three surfaces' first-LOD geometry.
    0x400EA0B1: (0xA64A58, 2912),
    0x10910559: (0xA655B8, 728),
    0x10910831: (0xA65890, 1320),
    0x109185E1: (0xA75FAC, 3392),
    0x10919321: (0xA76CEC, 5088),
    0x1091A701: (0xA780CC, 3468),
    0x400F35C1: (0xA78E64, 4896),
    0x1091B48D: (0xA7A184, 1224),
    0x1091B961: (0xA7A64C, 2280),
    0x40A43831: (0x28A030D, 216),      # ag_hanging_weeds_01 second indices
    0x40A43A01: (0x28A051D, 24),       # ag_hanging_weeds_01 third indices
    0x40ABDF31: (0x2A082F2, 72),       # ground_root_iw6_c_lod2100 indices
    0x40AC6451: (0x2A1E7D1, 120),     # fishmarket bass box LOD0 second indices
    0x117F2CD1: (0x2A23A7F, 144),     # fishmarket oyster box LOD2 first indices
    0x40AC8881: (0x2A25919, 160),      # harbor barracks picture 01 positions
    0x40AC8921: (0x2A25AA9, 96),       # harbor barracks picture 01 indices
    0x40AD58E1: (0x2A4454D, 12),       # sn_rubber_mat top indices shared across all three LODs
    # The sedan's interior glass shares LOD0 streams with LOD1 and LOD2.
    0x40B7B271: (0x2BC5158, 1456),     # positions
    0x118DC6E9: (0x2BC5708, 2184),     # vertex layer
    0x40B7B821: (0x2BC5F90, 912),      # indices
    0x40C30B41: (0x2DDE362, 192),      # me_picture8 first indices
    0x40C30CB1: (0x2DDE526, 24),       # me_picture8 second indices
    0x11C3FFF5: (0x3138013, 1848),     # burned timber 02 LOD0 first vertex layer
    0x40D7D2C1: (0x313874B, 336),      # burned timber 02 LOD0 first indices
    0x11C40F31: (0x3139A82, 1056),     # burned timber 01 LOD0 first vertex layer
    0x40D7DCC1: (0x3139EA2, 192),      # burned timber 01 LOD0 first indices
    0x11D43CB1: (0x32FA36A, 60),       # office paper stack 02 LOD3 indices
    0x40FE4B61: (0x371FDA6, 38456),    # ng_old_tv_static first positions
    0x11FB1B2D: (0x37293DE, 57684),    # ng_old_tv_static first vertex layer
    0x40FEE1A1: (0x3737532, 32928),    # ng_old_tv_static first indices
    0x40FF9901: (0x3742C78, 952),      # ng_old_tv_static second positions
    0x11FBFC81: (0x3743030, 1428),     # ng_old_tv_static second vertex layer
    0x40FF9CC1: (0x37435C4, 1248),     # ng_old_tv_static second indices
    # Each lake moss model reuses its LOD0 alpha cards at both farther LODs.
    0x4121A011: (0x3C34517, 896),      # lake moss 01 positions
    0x12281941: (0x3C34897, 1344),     # lake moss 01 vertex layer
    0x4121A391: (0x3C34DD7, 336),      # lake moss 01 indices
    0x4121D9C1: (0x3C44870, 608),      # lake moss 02 positions
    0x1228E191: (0x3C44AD0, 912),      # lake moss 02 vertex layer
    0x4121DC21: (0x3C44E60, 228),      # lake moss 02 indices
    0x412222D1: (0x3C50567, 512),      # lake moss 03 positions
    0x12295441: (0x3C50767, 768),      # lake moss 03 vertex layer
    0x412224D1: (0x3C50A67, 192),      # lake moss 03 indices
    0x122F0C51: (0x3CBFA00, 864),      # broken glass pile 03 LOD1 vertex layer
    0x122F0FB1: (0x3CBFD60, 252),      # broken glass pile 03 LOD1 indices
}
MP_SWAMP_SCENERY_STREAMS = {
    0x40C66D81: (0x2E9B0DA, 24),       # patio chair shared 8-vertex indices
    0x415E6201: (0x407BB0B, 64),       # Jack's local positions reused by King
}


class UnsupportedModel(ValueError):
    pass


class Reader:
    def __init__(self, data, start, end):
        self.data = data
        self.offset = start
        self.end = end

    def take(self, count):
        end = self.offset + count
        if end > self.end:
            raise UnsupportedModel(f"Data at {self.offset:#x} crosses its source asset boundary")
        blob = self.data[self.offset:end]
        self.offset = end
        return blob

    def read(self, fmt):
        format = struct.Struct(">" + fmt)
        return format.unpack(self.take(format.size))

    def name(self):
        end = self.data.index(0, self.offset, self.end)
        return self.take(end - self.offset + 1)[:-1].decode("ascii")


def values(data, offset, fmt):
    return struct.unpack_from(">" + fmt, data, offset)


def b64(data):
    return base64.b64encode(data).decode("ascii")


def vec(values):
    return dict(zip(("x", "y", "z"), values, strict=True))


def bounds(values):
    return {"midPoint": vec(values[:3]), "halfSize": vec(values[3:])}


def ptr_offset(value):
    if isinstance(value, int):
        return value
    return int(value, 0)


def source_strings(data):
    count = values(data, 40, "I")[0]
    reader = Reader(data, 60, len(data))
    pointers = reader.read(f"{count}I")
    if pointers[0] != 0 or any(p != INLINE[0] for p in pointers[1:]):
        raise ValueError("Unsupported IW6 PS3 script-string table")
    return [None] + [reader.name() for _ in pointers[1:]]


def model_body(data, root, end, source_name):
    reader = Reader(data, root + 296, end)
    if values(data, root, "I")[0] in INLINE and reader.name() != source_name:
        raise UnsupportedModel("Placement and XModel root names differ")
    return reader


def material_handle_reader(data, root, end, source_name):
    reader = model_body(data, root, end, source_name)
    num_bones, num_roots, _, reactive_count = values(data, root + 4, "4B")
    children = num_bones - num_roots
    if children < 0:
        raise UnsupportedModel("Invalid bones and roots")
    pointers = values(data, root + 36, "8I")
    sizes = (num_bones * 2, children, children * 8, children * 12,
             num_bones, num_bones * 32, reactive_count * 16)
    for pointer, size in zip(pointers[:7], sizes, strict=True):
        if pointer in INLINE:
            reader.take(size)
    if pointers[7] not in INLINE:
        raise UnsupportedModel("Shared model material-handle table")
    return reader


def read_skeleton(data, reader, root, script_strings):
    num_bones, num_roots, _, reactive_count = values(data, root + 4, "4B")
    children = num_bones - num_roots
    if not 0 <= children <= num_bones or num_bones == 0:
        raise UnsupportedModel("Invalid bones and roots")
    pointers = values(data, root + 36, "8I")
    counts = (num_bones, children, children, children, num_bones, num_bones, reactive_count)
    for pointer, count in zip(pointers[:7], counts, strict=True):
        if count and pointer not in INLINE:
            raise UnsupportedModel("Skeleton references an unsupported shared source array")
    bone_names = [script_strings[index] for index in reader.read(f"{num_bones}H")]
    parents = reader.take(children)
    quats = reader.read(f"{children * 4}h")
    translations = reader.read(f"{children * 3}f")
    classifications = reader.take(num_bones)
    matrices = []
    for _ in range(num_bones):
        x, y, z, w, tx, ty, tz, weight = reader.read("8f")
        matrices.append({
            "quat": {"x": x, "y": y, "z": z, "w": w},
            "trans": vec((tx, ty, tz)),
            "transWeight": weight,
        })
    reader.take(16 * reactive_count)
    return {
        "numBones": num_bones, "numRootBones": num_roots, "boneNames": bone_names,
        "parentList": b64(parents), "quats": quats, "trans": translations,
        "partClassification": b64(classifications), "baseMat": matrices,
    }


def quat_components(mat):
    return tuple(mat["quat"][axis] for axis in "xyzw")


def quat_unit(quat):
    length = math.hypot(*quat)
    return tuple(component / length for component in quat)


def quat_conjugate(quat):
    x, y, z, w = quat
    return (-x, -y, -z, w)


def quat_multiply(left, right):
    x, y, z, w = left
    a, b, c, d = right
    return (
        w * a + x * d + y * c - z * b,
        w * b - x * c + y * d + z * a,
        w * c + x * b - y * a + z * d,
        w * d - x * a - y * b - z * c,
    )


def quat_rotate(quat, vector):
    return quat_multiply(quat_multiply(quat, (*vector, 0.0)), quat_conjugate(quat))[:3]


def rotate_bone_bounds(bounds, rotation):
    midpoint = quat_rotate(rotation, [bounds["midPoint"][axis] for axis in "xyz"])
    x, y, z = (bounds["halfSize"][axis] for axis in "xyz")
    axes = [quat_rotate(rotation, axis) for axis in ((x, 0, 0), (0, y, 0), (0, 0, z))]
    half_size = [sum(abs(component) for component in row) for row in zip(*axes, strict=True)]
    return {"midPoint": vec(midpoint), "halfSize": vec(half_size)}


def rebase_myers_body(body, reference):
    # IW4 drives rotations in the stock rig's local frames. Myers's matching
    # joints sit in the right places but its bind axes differ by up to 180°.
    stock_rotations = {name: quat_unit(quat_components(mat)) for name, mat in
                       zip(reference["boneNames"], reference["baseMat"], strict=True)}
    source_mats = body["baseMat"]
    roots = body["numRootBones"]
    mats = list(source_mats[:roots])
    quats = []
    translations = []
    for index, parent_distance in enumerate(base64.b64decode(body["parentList"]), roots):
        parent = mats[index - parent_distance]
        parent_rotation = quat_components(parent)
        parent_inverse = quat_conjugate(quat_unit(parent_rotation))
        source = source_mats[index]
        source_rotation = quat_unit(quat_components(source))
        target_rotation = stock_rotations.get(body["boneNames"][index], source_rotation)
        local_rotation = quat_multiply(parent_inverse, target_rotation)
        packed = [round(value * 32767) for value in local_rotation]
        quats.extend(packed)

        # Rebuild the model-space orientation from the quantized local words.
        rotation = quat_multiply(parent_rotation, [value / 32767 for value in packed])
        mats.append({
            "quat": dict(zip("xyzw", rotation, strict=True)),
            "trans": source["trans"],
            "transWeight": 2.0 / sum(component * component for component in rotation),
        })
        offset = [source["trans"][axis] - parent["trans"][axis] for axis in "xyz"]
        translations.extend(quat_rotate(parent_inverse, offset))

        bone_info = body["boneInfo"][index]
        old_to_new = quat_multiply(quat_conjugate(quat_unit(rotation)), source_rotation)
        bone_info["bounds"] = rotate_bone_bounds(bone_info["bounds"], old_to_new)

    body.update(quats=quats, trans=translations, baseMat=mats)


def align_world_axe(model, lods):
    # Center the shaft 6 units toward the butt from Ghosts' idle grip.
    # Match that point to the Beretta's j_pistol_grip in IW4's pistol pose,
    # with the blade toward +X and the head toward +Z.
    grip_x, grip_y, grip_z = 9.67, -0.0459, 0.0202
    dx, dy, dz = -10.459282 - grip_z, -grip_y, -1.623077 + grip_x

    def position(x, y, z):
        return (z + dx, y + dy, -x + dz)

    def move_bounds(box):
        center, half = box["midPoint"], box["halfSize"]
        box["midPoint"] = vec(position(center["x"], center["y"], center["z"]))
        box["halfSize"] = vec((half["z"], half["y"], half["x"]))

    def turn_direction(word):
        # RSX promotes signed 11/11/10 components by 5/5/6 bits.
        x = ((word & 0x7ff) ^ 0x400) - 0x400
        z = ((word >> 22) ^ 0x200) - 0x200
        new_z = min(511, round(-x / 2))
        return ((new_z & 0x3ff) << 22) | (word & (0x7ff << 11)) | ((2 * z) & 0x7ff)

    def move_tree(tree):
        # q = (p + trans) * scale. The reversed X interval becomes 65535-q.
        origin, scale = tree["trans"], tree["scale"]
        tree["scale"] = vec((scale["z"], scale["y"], scale["x"]))
        tree["trans"] = vec((origin["z"] - dx, origin["y"] - dy, 65535 / scale["x"] - origin["x"] - dz))
        for node in tree["nodes"]:
            box = node["aabb"]
            box.update(minsX=box["minsZ"], maxsX=box["maxsZ"],
                       minsZ=65535 - box["maxsX"], maxsZ=65535 - box["minsX"])

    radius = 0.0
    for lod in lods:
        for surface in lod["surfaces"]:
            positions = bytearray(base64.b64decode(surface["verts0"]))
            layer = bytearray(base64.b64decode(surface["verts1"]))
            for offset in range(0, len(positions), 16):
                point = position(*struct.unpack_from(">3f", positions, offset))
                struct.pack_into(">3f", positions, offset, *point)
                radius = max(radius, math.hypot(*point))
                normal, tangent = struct.unpack_from(">2I", layer, offset + 8)
                struct.pack_into(">2I", layer, offset + 8, turn_direction(normal), turn_direction(tangent))
            surface.update(verts0=b64(positions), verts1=b64(layer))
            for group in surface["vertList"]:
                if group["collisionTree"] is not None:
                    move_tree(group["collisionTree"])

    model["radius"] = radius
    move_bounds(model["bounds"])
    move_bounds(model["boneInfo"][model["boneNames"].index("tag_axe")]["bounds"])
    for row in model["collSurfs"]:
        move_bounds(row["bounds"])

    # The mesh is rigid to identity tag_axe. Its child locator follows the head.
    fx = model["boneNames"].index("tag_axe_fx")
    child = fx - model["numRootBones"]
    mat = model["baseMat"][fx]
    new_pos = position(*(mat["trans"][axis] for axis in "xyz"))
    sin_45 = math.sqrt(0.5)
    rotation = quat_multiply((0, sin_45, 0, sin_45), quat_unit(quat_components(mat)))
    packed = [round(value * 32767) for value in rotation]
    quats, translations = list(model["quats"]), list(model["trans"])
    quats[4 * child:4 * child + 4] = packed
    translations[3 * child:3 * child + 3] = new_pos
    model.update(quats=quats, trans=translations)
    rotation = [value / 32767 for value in packed]
    mat.update(quat=dict(zip("xyzw", rotation, strict=True)), trans=vec(new_pos),
               transWeight=2.0 / sum(value * value for value in rotation))


def read_tree(reader):
    tx, ty, tz, sx, sy, sz, node_count, node_ptr, leaf_count, leaf_ptr = reader.read("6f4I")
    if node_ptr not in INLINE or leaf_ptr not in INLINE or not node_count or not leaf_count:
        raise UnsupportedModel("Collision tree references a shared source array")
    nodes = []
    for _ in range(node_count):
        x0, y0, z0, x1, y1, z1, child, count = reader.read("8H")
        nodes.append({
            "aabb": {"minsX": x0, "minsY": y0, "minsZ": z0, "maxsX": x1, "maxsY": y1, "maxsZ": z1},
            "childBeginIndex": child,
            "childCount": count,
        })
    leaves = [{"triangleBeginIndex": index} for index in reader.read(f"{leaf_count}H")]
    return {"trans": vec((tx, ty, tz)), "scale": vec((sx, sy, sz)), "nodes": nodes, "leafs": leaves}


def read_stream(reader, pointer, size):
    if pointer in INLINE:
        return reader.take(size)
    shared = MP_SWAMP_SHARED_STREAMS.get(pointer)
    if shared is None and reader.offset >= 0x407ACD0:
        shared = MP_SWAMP_SCENERY_STREAMS.get(pointer)
    if shared is None:
        return None
    start, expected_size = shared
    if size != expected_size:
        raise UnsupportedModel(f"Shared stream {pointer:#x} has a different size")
    return reader.data[start:start + size]


def decode_surface(reader, row):
    flags, vertex_count, triangle_count, list_count = values(reader.data, row, "HHHB")
    blends = values(reader.data, row + 8, "4H")
    payload_pointers = values(reader.data, row + 16, "3I")
    list_pointer, blend_pointer = values(reader.data, row + 48, "2I")
    quantization = values(reader.data, row + 60, "f")[0]
    part_bits = values(reader.data, row + 64, "6I")
    if (not flags & 0x20 or flags & ~0xFD or not vertex_count or not triangle_count or
            not math.isfinite(quantization) or quantization <= 0):
        raise UnsupportedModel(f"Unsupported surface layout at {row:#x} (flags {flags:#x})")
    position_pointer, layer_pointer, index_pointer = payload_pointers
    # Skinned IW6 surfaces store normal and tangent beside quantized XYZ
    # and binormal sign. Rigid surfaces keep those directions in the layer.
    directions_in_positions = bool(flags & 0x40)
    position_stride = 16 if directions_in_positions else 8
    has_color = not flags & 0x18
    layer_stride = (8 if has_color else 4) + (0 if directions_in_positions else 8)
    positions = read_stream(reader, position_pointer, vertex_count * position_stride)
    layer = read_stream(reader, layer_pointer, vertex_count * layer_stride)
    index_data = read_stream(reader, index_pointer, triangle_count * 6)
    rigid_rows = [reader.read("4HI") for _ in range(list_count)] if list_pointer in INLINE else []
    rigid = []
    for bone, verts, first_tri, tris, tree_pointer in rigid_rows:
        if first_tri + tris > triangle_count or verts > vertex_count:
            raise UnsupportedModel(f"Invalid rigid vertex span in surface at {row:#x}")
        rigid.append({
            "boneOffset": bone, "vertCount": verts, "triOffset": first_tri, "triCount": tris,
            "collisionTree": read_tree(reader) if tree_pointer in INLINE else None,
        })
    blend_count = sum(count * (index * 2 + 1) for index, count in enumerate(blends))
    blend_data = reader.read(f"{blend_count}H") if blend_pointer in INLINE else ()
    if (positions is None or layer is None or index_data is None or
            len(rigid) != list_count or len(blend_data) != blend_count):
        return None
    if sum(blends) + sum(row["vertCount"] for row in rigid) != vertex_count:
        raise UnsupportedModel(f"Invalid blended vertex count in surface at {row:#x}")
    triangles = struct.unpack(f">{triangle_count * 3}H", index_data)
    if max(triangles) >= vertex_count:
        raise UnsupportedModel(f"Invalid vertex index in surface at {row:#x}")
    expanded_positions = bytearray()
    expanded_layer = bytearray()
    for index in range(vertex_count):
        position_offset = index * position_stride
        layer_offset = index * layer_stride
        x, y, z, sign = struct.unpack_from(">4h", positions, position_offset)
        expanded_positions.extend(struct.pack(
            ">4f", x * quantization, y * quantization, z * quantization,
            -1.0 if sign < 0 else 1.0,
        ))
        if not has_color:
            expanded_layer.extend(b"\xff" * 4)
        expanded_layer.extend(layer[layer_offset:layer_offset + layer_stride])
        if directions_in_positions:
            expanded_layer.extend(positions[position_offset + 8:position_offset + 16])
    return {
        "tileMode": 0, "deformedRaw": int(bool(blend_count)), "streamFlags": 7, "pad03": 0,
        "vertCount": vertex_count, "triCount": triangle_count, "triIndices": triangles,
        "vertexInfo": {
            "blend0": blends[0], "blend1": blends[1], "blend2": blends[2], "blend3": blends[3],
            "vertsBlend": blend_data,
        },
        "verts0": b64(expanded_positions), "vb0StreamSource": 0, "vb0DataOffset": 0,
        "verts1": b64(expanded_layer), "vb1StreamSource": 0, "vb1DataOffset": 0,
        "vertList": rigid, "indexDataOffset": 0, "partBits": part_bits,
    }


def find_lods(data, start, end):
    result = []
    for match in LOD_HEADER.finditer(data, start, end):
        row = match.end(2) + 1
        if row + 88 > end:
            continue
        flags, vertex_count, triangle_count, list_count = values(data, row, "HHHB")
        quantization = values(data, row + 60, "f")[0]
        if (flags & 0x20 and not flags & ~0xFD and vertex_count and triangle_count and
                (list_count or flags & 0x40) and math.isfinite(quantization) and quantization > 0):
            result.append((match.start(), row, match[1][0]))
    return result


def read_lods(data, root, start, end):
    source_lod_count = data[root + 229]
    declarations = [values(data, root + 68 + index * 40, "fHHI") for index in range(source_lod_count)]
    candidates = find_lods(data, start, end)
    result = []
    next_offset = start
    missing_surfaces = 0
    for index, (dist, count, first, pointer) in enumerate(declarations):
        if not count:
            continue
        if pointer not in INLINE:
            missing_surfaces += count
            continue
        matching = next((row for row in candidates if row[0] >= next_offset and row[2] == count), None)
        if matching is None:
            missing_surfaces += count
            continue
        header, row_base, _ = matching
        reader = Reader(data, row_base + count * 88, end)
        surfaces = []
        for num in range(count):
            surface = decode_surface(reader, row_base + num * 88)
            if surface is None:
                missing_surfaces += 1
            else:
                surfaces.append((first + num, surface))
        if surfaces:
            result.append((index, dist, values(data, header + 12, "6I"), surfaces))
        next_offset = reader.offset
    if not result:
        raise UnsupportedModel("Source model declares no supported surface providers")
    return result, next_offset, candidates[0][0], missing_surfaces


def source_lod_distances(data, root):
    return [values(data, root + 68 + index * 40, "f")[0] for index in range(data[root + 229])]


def read_collision(data, reader, root, num_bones, num_surfaces):
    count, pointer, contents, bone_pointer = values(data, root + 232, "4I")
    mip_pointer = values(data, root + 276, "I")[0]
    coll_surfs = []
    if pointer:
        if pointer not in INLINE:
            raise UnsupportedModel("Shared XModel collision-surface table")
        for _ in range(count):
            *extent, bone, surface_contents, flags = reader.read("6f3i")
            coll_surfs.append({
                "bounds": bounds(extent), "boneIndex": bone,
                "contents": surface_contents, "surfaceFlags": flags,
            })
    if bone_pointer not in INLINE:
        raise UnsupportedModel("Shared XModel bone bounds")
    bone_info = []
    for _ in range(num_bones):
        *extent, radius = reader.read("7f")
        if min(extent[3:]) < 0 or not all(math.isfinite(x) for x in (*extent, radius)):
            raise UnsupportedModel("Invalid XModel bone bounds")
        bone_info.append({"bounds": bounds(extent), "radiusSquared": radius})
    inv_mip = reader.read(f"{num_surfaces}H") if mip_pointer in INLINE else [0] * num_surfaces
    return coll_surfs, contents, bone_info, inv_mip


def read_materials(pointers, start, end, material_map, fallback):
    inline_count = sum(pointer in INLINE for pointer in pointers)
    # The material reader maps the exact roots consumed by the inline
    # handle cells in serialized order, starting just after the handles.
    owned = [name for offset, name in material_map["inline"] if start <= offset < end]
    inline_names = iter(owned) if len(owned) == inline_count else iter(())
    material_names = []
    for pointer in pointers:
        if pointer in INLINE:
            name = next(inline_names, None)
        else:
            name = material_map["aliases"].get(pointer)
        material_names.append(name or fallback)
    return material_names


def native_model(data, root, end, source_name, script_strings, material_map, fallback, target_name):
    reader = model_body(data, root, end, source_name)
    skeleton = read_skeleton(data, reader, root, script_strings)
    source_surface_count = data[root + 6]
    if values(data, root + 64, "I")[0] not in INLINE:
        raise UnsupportedModel("Shared model material-handle table")
    pointers = reader.read(f"{source_surface_count}I")
    xmodel_radius, *extent = values(data, root + 248, "7f")
    if not all(math.isfinite(value) for value in extent) or min(extent[3:]) < 0:
        raise UnsupportedModel("Invalid XModel bounds")
    lods, lod_end, material_end, missing_surfaces = read_lods(data, root, reader.offset, end)
    materials = read_materials(pointers, reader.offset, material_end, material_map, fallback)
    reader.offset = lod_end
    try:
        coll_surfs, contents, bone_info, inv_mip = read_collision(
            data, reader, root, skeleton["numBones"], source_surface_count)
    except UnsupportedModel:
        if not missing_surfaces:
            raise
        # A skipped source LOD can separate the last decoded surface from
        # the trailing bone info. Keep the model bounded using its own BBox.
        coll_surfs, contents = [], 0
        bone_info = [{"bounds": bounds(extent), "radiusSquared": xmodel_radius ** 2}] * skeleton["numBones"]
        inv_mip = [0] * source_surface_count
    source_coll_lod = data[root + 230] if coll_surfs else 255
    parts = math.ceil(max(len(row[3]) for row in lods) / MAX_STATIC_LOD_SURFACES)
    models = []
    for part in range(parts):
        name = target_name + (f"__part{part}" if part else "")
        first_surface = part * MAX_STATIC_LOD_SURFACES
        model_surfaces = []
        model_materials = []
        model_mips = []
        material_slots = []
        model_lods = []
        lod_indices = []
        model_coll_lod = 255
        for index, distance, bits, all_surfaces in lods:
            rows = all_surfaces[first_surface:first_surface + MAX_STATIC_LOD_SURFACES]
            surfaces = [surface for _, surface in rows]
            if not surfaces:
                continue
            count = len(surfaces)
            root_name = f"{name}_lod{index}"
            model_surfaces.append({
                "version": 1, "name": root_name, "numSurfs": count, "pad0A": 0,
                "partBits": bits, "surfaces": surfaces,
            })
            model_lods.append({
                "dist": distance, "numSurfs": count, "surfIndex": len(model_materials),
                "partBits": bits, "modelSurfs": root_name,
            })
            lod_indices.append(index)
            if rows[-1][0] >= source_surface_count:
                raise UnsupportedModel("XModel LOD surface range exceeds its material handles")
            model_materials.extend(materials[i] for i, _ in rows)
            model_mips.extend(inv_mip[i] for i, _ in rows)
            material_slots.extend(i for i, _ in rows)
            # The PS3 line trace reads the selected LOD's rigid groups and
            # their corresponding XModelCollSurfs. A per-group tree is optional.
            if index == source_coll_lod and sum(len(surface["vertList"]) for surface in surfaces) == len(coll_surfs):
                model_coll_lod = len(model_lods) - 1
        lod_count = len(model_lods)
        model_lods.extend({
            "dist": 1_000_000, "numSurfs": 0, "surfIndex": 0, "partBits": TARGET_PART_BITS,
            "modelSurfs": None,
        } for _ in range(4 - lod_count))
        models.append(({
            "version": 1, "name": name, "numSurfs": len(model_materials), "lodRampType": 0,
            "scale": 1, "noScalePartBits": values(data, root + 12, "6I"),
            **skeleton, "materials": model_materials, "lods": model_lods, "maxLoadedLod": 0,
            "numLods": lod_count, "collLod": model_coll_lod, "flags": 0,
            "contents": contents if model_coll_lod != 255 else 0,
            "collSurfs": coll_surfs if model_coll_lod != 255 else [], "boneInfo": bone_info,
            "radius": xmodel_radius, "bounds": bounds(extent), "invHighMipRadius": model_mips,
            "memUsage": 0, "physPreset": None, "physCollmap": None,
        }, model_surfaces, material_slots, lod_indices))
    return models, missing_surfaces


def geometry_signature(data, root):
    return (
        data[root + 4:root + 7],
        data[root + 248:root + 276],
        tuple(values(data, root + 72 + index * 40, "2H") for index in range(data[root + 229])),
    )


def shared_variant(data, root, end, source_name, native_by_geometry, material_map, fallback):
    # These one-bone material variants share their skeleton and all native
    # LOD providers. The packed bone pointer also determines the base
    # provider's virtual material handles. Require a verified in-table
    # material alias before assigning a matching prior geometry provider.
    if data[root + 4:root + 6] != b"\x01\x01":
        raise UnsupportedModel("Unsupported shared skeleton")
    bone, _, _, _, classification, base_matrix = values(data, root + 36, "6I")
    if not 0x40000000 < bone < 0x50000000:
        raise UnsupportedModel("Shared skeleton has no source virtual address")
    if classification != bone + 2 or base_matrix != ((classification - 1 + 1 + 3) & ~3) + 1:
        raise UnsupportedModel("Shared one-bone skeleton layout differs")
    lod_pointers = [values(data, root + 76 + 40 * i, "I")[0] for i in range(data[root + 229])]
    if not all(base_matrix + 32 < pointer < 0x50000000 for pointer in lod_pointers):
        raise UnsupportedModel("Variant has unresolved mixed LOD providers")
    alias_base = base_matrix + 32
    providers = []
    for provider_root, provider_end, provider_name, outputs, missing in native_by_geometry.get(
            geometry_signature(data, root), ()):
        reader = material_handle_reader(data, provider_root, provider_end, provider_name)
        pointers = reader.read(f"{data[provider_root + 6]}I")
        if not any(pointer in INLINE for pointer in pointers) and lod_pointers[0] == alias_base + len(pointers) * 4:
            providers.append((provider_root, outputs, missing))
            continue
        for pointer in pointers:
            difference = pointer - alias_base
            if difference >= 0 and difference % 4 == 0 and difference // 4 < len(pointers):
                if pointers[difference // 4] in INLINE:
                    providers.append((provider_root, outputs, missing))
                    break
    if len(providers) != 1:
        raise UnsupportedModel("Shared LOD providers lack a unique verified owner")
    provider_root, provider_outputs, missing = providers[0]

    reader = material_handle_reader(data, root, end, source_name)
    pointers = reader.read(f"{data[root + 6]}I")
    materials = read_materials(pointers, reader.offset, end, material_map, fallback)
    same_collision = (
        data[root + 230] == data[provider_root + 230] and
        data[root + 232:root + 236] == data[provider_root + 232:provider_root + 236] and
        data[root + 240:root + 244] == data[provider_root + 240:provider_root + 244]
    )
    source_dists = source_lod_distances(data, root)
    variants = []
    for i, (provider, _, material_slots, lod_indices) in enumerate(provider_outputs):
        part_suffix = f"__part{i}" if i else ""
        variant = {
            **provider, "name": f"iw6/mp_swamp/{source_name}{part_suffix}",
            "materials": [materials[slot] for slot in material_slots],
            "lods": [
                {**row, "dist": source_dists[lod_indices[index]]} if row["numSurfs"] else row
                for index, row in enumerate(provider["lods"])
            ],
        }
        if not same_collision:
            variant.update(collLod=255, contents=0, collSurfs=[])
        variants.append((variant, [], material_slots, lod_indices))
    return variants, missing


def model_rules(lod_requests, placements, collision_flags):
    result = subprocess.run(
        ["dotnet", "fsi", "--exec", Path(__file__).with_name("ModelRules.fsx")],
        input=json.dumps({"lods": lod_requests, "placements": placements, "collisionFlags": collision_flags},
                         allow_nan=False),
        text=True, stdout=subprocess.PIPE, check=True,
    )
    decisions = json.loads(result.stdout)
    flags = dict(zip(collision_flags, decisions["collisionFlags"], strict=True))
    return {row["key"]: row["distances"] for row in decisions["lods"]}, decisions["placements"], flags


def route_static_vertex_attributes(exports, placed_names):
    static_names = placed_names.difference(
        MP_SWAMP_EFFECT_MODELS.values(),
        (name for _, name in MP_SWAMP_BREACH_MODELS.values()),
        (name for _, name, _ in MP_SWAMP_REQUIRED_MODELS.values()),
        MP_SWAMP_SCENERY_MODELS.values(),
    )
    static_lods, other_lods = set(), set()
    for source_name, outputs in exports:
        for model, _, _, _ in outputs:
            lods = static_lods if source_name in static_names and model["numBones"] == 1 else other_lods
            lods.update(row["modelSurfs"] for row in model["lods"][:model["numLods"]])

    # A material variant can reference another model's surface asset. Route it
    # only when all owners are eligible. CPU traces and marks still read the
    # position and index streams in main memory.
    local_lods = static_lods - other_lods
    for _, outputs in exports:
        for _, lods, _, _ in outputs:
            for lod in lods:
                if lod["name"] not in local_lods:
                    continue
                for surface in lod["surfaces"]:
                    if not surface["deformedRaw"]:
                        surface["streamFlags"] &= ~VERTS1_IN_LARGE


def placement_record(name, placement, properties):
    # IW4 AnglesToAxis uses the same forward, left and up basis as the
    # transposed placement matrix recovered from the source draw table.
    (fx, fy, fz), (lx, ly, lz), (_, _, uz) = placement["axis"]
    hyp = math.hypot(fx, fy)
    pitch = math.degrees(math.atan2(-fz, hyp))
    if hyp > 0.003:
        yaw = math.degrees(math.atan2(fy, fx))
        roll = math.degrees(math.atan2(lz, uz))
    else:
        yaw = math.degrees(math.atan2(-lx, ly))
        roll = 0.0
    origin = " ".join(f"{value:.9g}" for value in placement["origin"])
    authored = "".join(f'"{key}" "{value}"\n' for key, value in properties.items())
    return (f'{{\n"classname" "misc_model"\n"model" "{name}"\n"origin" "{origin}"\n'
            f'"angles" "{pitch:.9g} {yaw:.9g} {roll:.9g}"\n'
            f'"modelscale" "{placement["scale"]:.9g}"\n{authored}}}\n')


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, allow_nan=False, separators=(",", ":")) + "\n", encoding="utf-8")


def export_axe_animations(data, script_strings, output):
    # These Ghosts viewmodel clips use the same packed track buckets and
    # stream order as IW4 PS3. They contain no root delta or index stream.
    for root, end, name in MP_SWAMP_AXE_ANIMATIONS:
        header = Reader(data, root, root + 0x58)
        name_pointer, = header.read("I")
        byte_count, short_count, int_count, random_bytes, random_ints, frames = header.read("6H")
        flags, no_quat = header.read("2B")
        bone_counts = header.take(10)
        bone_count, notify_count, asset_type, ik_type = header.read("4B")
        random_shorts, index_count, framerate, frequency = header.read("IIff")
        *streams, indices, notify, delta = header.read("10I")
        reader = Reader(data, header.offset, end)
        if (name_pointer not in INLINE or reader.name() != name or index_count or indices or delta or ik_type or
                notify not in INLINE or any(pointer not in INLINE for pointer in streams)):
            raise ValueError(f"Unsupported Fog XAnim layout for {name}")
        names = [script_strings[index] for index in reader.read(f"{bone_count}H")]
        notifies = [{"name": script_strings[index], "time": time}
                    for index, time in (reader.read("H2xf") for _ in range(notify_count))]
        packed = {
            "quantizedBytes": b64(reader.take(byte_count)),
            "quantizedShorts": reader.read(f"{short_count}h"),
            "quantizedInts": reader.read(f"{int_count}i"),
            "randomizedQuantizedShorts": reader.read(f"{random_shorts}h"),
            "randomizedQuantizedBytes": b64(reader.take(random_bytes)),
            "randomizedQuantizedInts": reader.read(f"{random_ints}i"),
        }
        if reader.offset != end:
            raise ValueError(f"Unexpected Fog XAnim payload size for {name}")
        write_json(output / "xanim_native" / f"{name}.json", {
            "format": "iw4-ps3-xanim", "version": 1, "name": name,
            "dataByteCount": byte_count, "dataShortCount": short_count, "dataIntCount": int_count,
            "randomDataByteCount": random_bytes, "randomDataShortCount": random_shorts,
            "randomDataIntCount": random_ints, "indexCount": index_count, "numFrames": frames,
            "flags": flags, "deltaFlags": no_quat, "boneCounts": b64(bone_counts),
            "boneNameCount": bone_count, "notifyCount": notify_count, "assetType": asset_type,
            "pad1F": 0, "framerate": framerate, "frequency": frequency,
            "names": names, "notify": notifies, "packedDataStreams": packed,
            "indices": [], "deltaPart": None,
        })


def export_models(source, placement_path, output, material_path, fallback, iw4_library,
                  inventory_only=False, include_scenery=False):
    data = source.read_bytes()
    if hashlib.sha256(data).hexdigest() != MP_SWAMP_SHA256:
        raise ValueError("Unsupported IW6 PS3 model source fingerprint")
    placement_json = json.loads(placement_path.read_text(encoding="utf-8"))
    placements = placement_json if isinstance(placement_json, list) else placement_json["placements"]
    sources = MP_SWAMP_EFFECT_MODELS | {
        ptr_offset(row["model_source_offset"]): row["model_name"]
        for row in placements if row["model_source_offset"] is not None
    }
    offsets = sorted(sources)
    models_in_zone_order = list(zip(offsets, offsets[1:] + [0x3CF853D], strict=True))
    for root, (end, name) in MP_SWAMP_BREACH_MODELS.items():
        sources[root] = name
        models_in_zone_order.append((root, end))
    for root, (end, source_name, _) in MP_SWAMP_REQUIRED_MODELS.items():
        sources[root] = source_name
        models_in_zone_order.append((root, end))
    models_in_zone_order.sort()
    if include_scenery:
        sources.update(MP_SWAMP_SCENERY_MODELS)
        scenery_offsets = sorted(MP_SWAMP_SCENERY_MODELS)
        models_in_zone_order.extend(zip(scenery_offsets, scenery_offsets[1:] + [0x408BD76], strict=True))
    if inventory_only:
        requests = []
        for root, end in models_in_zone_order:
            try:
                reader = material_handle_reader(data, root, end, sources[root])
            except UnsupportedModel:
                continue
            start = reader.offset
            pointers = reader.read(f"{data[root + 6]}I")
            requests.append({
                "source_model_offset": root, "source_model_name": sources[root],
                "material_table_offset": start,
                "material_pointers": [f"0x{pointer:08x}" for pointer in pointers],
            })
        write_json(output / "model-material-requests.json", requests)
        return

    reference_path = iw4_library / "xmodel_native" / f"{IW4_PLAYER_BODY}.json"
    player_body = json.loads(reference_path.read_text(encoding="utf-8"))
    if player_body["name"] != IW4_PLAYER_BODY:
        raise ValueError(f"Unexpected stock XModel in {reference_path}")
    materials = json.loads(material_path.read_text(encoding="utf-8")) if material_path else {}
    aliases = materials.get("aliases", materials)
    material_map = {
        "aliases": {ptr_offset(key): value for key, value in aliases.items()},
        "inline": sorted((ptr_offset(key), value) for key, value in materials.get("inlineModels", {}).items()),
    }
    script_strings = source_strings(data)
    models = {}
    native_by_geometry = {}
    failures = {}
    fallbacks = Counter()
    missing_surface_counts = {}
    placed_names = {row["model_name"] for row in placements}
    ground_lit = {row["model_name"] for row in placements if row.get("source_flags", 0) & 0x20}
    shared_variants = {}
    exports = []
    lod_requests = []
    for root, end in models_in_zone_order:
        source_name = sources[root]
        required_model = MP_SWAMP_REQUIRED_MODELS.get(root)
        target_name = required_model[2] if required_model else f"iw6/mp_swamp/{source_name}"
        if not SAFE_NAME.fullmatch(source_name) or any(part in ("", ".", "..") for part in source_name.split("/")):
            raise ValueError(f"Invalid source model name {source_name!r}")
        try:
            outputs, missing_surfaces = native_model(
                data, root, end, source_name, script_strings, material_map, fallback, target_name)
        except (IndexError, ValueError, struct.error) as error:
            if required_model:
                raise UnsupportedModel(f"Cannot convert required XModel {source_name}: {error}") from error
            try:
                outputs, missing_surfaces = shared_variant(
                    data, root, end, source_name, native_by_geometry, material_map, fallback)
            except UnsupportedModel:
                failures[source_name] = str(error)
                continue
            shared_variants[source_name] = outputs[0][0]["lods"][0]["modelSurfs"]
        else:
            native_by_geometry.setdefault(geometry_signature(data, root), []).append(
                (root, end, source_name, outputs, missing_surfaces))
        if required_model and (len(outputs) != 1 or missing_surfaces or fallback in outputs[0][0]["materials"]):
            raise UnsupportedModel(f"Incomplete geometry or material bindings for required XModel {source_name}")
        if target_name == MYERS_BODY:
            rebase_myers_body(outputs[0][0], player_body)
        elif source_name == "weapon_axe_iw6":
            model, surfaces, _, _ = outputs[0]
            align_world_axe(model, surfaces)
        if len(outputs) == 1 and source_name in placed_names:
            model, _, _, lod_indices = outputs[0]
            distances = source_lod_distances(data, root)
            if lod_indices[-1] < len(distances) - 1:
                lod_requests.append({
                    "key": model["name"], "sourceDistances": distances, "availableIndices": lod_indices,
                })
        exports.append((source_name, outputs))
        models[source_name] = [model["name"] for model, _, _, _ in outputs]
        if missing_surfaces:
            missing_surface_counts[source_name] = missing_surfaces

    route_static_vertex_attributes(exports, placed_names)
    collision_flags = sorted({row["surfaceFlags"] for _, outputs in exports
                              for model, _, _, _ in outputs for row in model["collSurfs"]})
    lod_rules, placement_rules, flags = model_rules(lod_requests, placements, collision_flags)
    for source_name, outputs in exports:
        for model, surfaces, _, _ in outputs:
            if model["name"] in lod_rules:
                for row, distance in zip(model["lods"][:model["numLods"]], lod_rules[model["name"]], strict=True):
                    row["dist"] = distance
            model["flags"] = int(source_name in ground_lit)
            # Variants can share the source rows with their provider model.
            model["collSurfs"] = [{**row, "surfaceFlags": flags[row["surfaceFlags"]]} for row in model["collSurfs"]]
            write_json(output / "xmodel_native" / (model["name"] + ".json"), model)
            for surface in surfaces:
                write_json(output / "xmodelsurfs_native" / (surface["name"] + ".json"), surface)
            fallbacks[source_name] += model["materials"].count(fallback)
    export_axe_animations(data, script_strings, output)
    entities = []
    omitted = Counter()
    for placement, properties in zip(placements, placement_rules, strict=True):
        original_name = placement["model_name"]
        names = models.get(original_name)
        if names:
            entities.extend(placement_record(name, placement, properties) for name in names)
        else:
            omitted[original_name] += 1
    output.mkdir(parents=True, exist_ok=True)
    (output / "static-models.ents").write_text("".join(entities), encoding="utf-8")
    write_json(output / "static-model-export.json", {
        "source_instances": len(placements), "converted_instances": len(entities),
        "converted_model_definitions": sum(map(len, models.values())),
        "omitted_instances_by_model": omitted, "omitted_model_reasons": failures,
        "omitted_surface_slots_by_model": missing_surface_counts,
        "shared_geometry_variants": shared_variants,
        "effect_models": {name: models[name] for name in MP_SWAMP_EFFECT_MODELS.values() if name in models},
        "scenery_models": {name: models[name] for name in MP_SWAMP_SCENERY_MODELS.values() if name in models},
        "models_by_source_name": models,
        "fallback_material_slots_by_model": {name: count for name, count in fallbacks.items() if count},
    })


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="inflated IW6 PS3 map zone")
    parser.add_argument("placements", type=Path, help="recovered IW6 static-model placements")
    parser.add_argument("output", type=Path, help="IW4 native source root")
    parser.add_argument("--materials", type=Path, help="IW6-to-IW4 material names by pointer and root offset")
    parser.add_argument("--iw4-library", type=Path, help="retail IW4 source library with the player body rig")
    parser.add_argument("--fallback-material", default=FALLBACK_MATERIAL, help="owned IW4 XModel material")
    parser.add_argument("--inventory-only", action="store_true", help="write model-material-requests.json only")
    parser.add_argument("--include-scenery", action="store_true", help="also export the map's clutter-only models")
    args = parser.parse_args()
    if not args.inventory_only and args.iw4_library is None:
        parser.error("--iw4-library is required for model export")
    try:
        export_models(args.source, args.placements, args.output, args.materials,
                      args.fallback_material, args.iw4_library, args.inventory_only, args.include_scenery)
    except (KeyError, OSError, ValueError, struct.error) as error:
        parser.exit(1, f"Model export failed: {error}\n")


if __name__ == "__main__":
    main()
