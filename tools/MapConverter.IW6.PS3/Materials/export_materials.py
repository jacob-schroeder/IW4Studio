"""Export the Ghosts PS3 Swamp world materials and native texture payloads."""
import argparse
from contextlib import ExitStack
from dataclasses import dataclass
from functools import cache
import json
from math import isfinite
from pathlib import Path
import re
import subprocess
from shutil import copyfile
from struct import iter_unpack, unpack_from
from zlib import decompress

if __package__:
    from . import surface_fragments, reveal, sky, wind
else:
    import surface_fragments
    import reveal
    import sky
    import wind

OWNED = 0xffffffff
INSERT = 0xfffffffe
ASSET_TABLE = 0x504a
ASSET_COUNT = 0x811
ASSET_HANDLE = 0x40005015
FIRST_WORLD_MATERIAL = 0xe91eff
WORLD_ROOT = 0x147560f
MATERIAL_MEMORY = 0x26bac2f
MATERIAL_MEMORY_COUNT = 438
MATERIAL_MEMORY_HANDLE = 0x40944431
WORLD_SKY_IMAGE = 0x1475aa0
WORLD_SKY_HANDLE = 0x402b7a01
# These terrain sets alias the ten lit pixel programs owned by the matching
# sets without hb0. The physical spans end at the next technique-set root.
TERRAIN_LIT_PASS_OWNERS = {
    0x128769f: (0x118b419, 0x1198b58),  # Dirt and blood pool.
    0x1329a29: (0x1303638, 0x13154a9),  # Dirt, mud reveal and blood pool.
}
MAP_IMAGE_IMPORT_ALIASES = {
    0x400d9bf9: (0xa4aeb7, ",$identitynormalmap", 5),  # mq/mtl_mp_mmyers_alpha_a, texture 1
    0x40138da5: (0xae6a12, ",$white", 2),              # Frog splash row 1, texture 0
}
EFFECT_MATERIAL_START = 0xa34000
EFFECT_MATERIAL_END = 0xbb0000
EFFECT_MATERIAL_PREFIXES = (b"gfx_", b"vfx_", b"fx_", b"cloud_", b"spark_")
MODEL_MATERIAL_HANDLES = {
    0x401ab061: 0xb91f45,   # Both owl variants share this source binding.
    0x401cba31: 0xcd1f9c,  # Both paper stacks share office_paper_piece01_iw6's material.
    0x411eb00d: 0x3bf1342,  # Both hook slots reference the preceding arch chain's rusty metal.
}
MATERIAL_ROOT_SIZE = 0xb8
IMAGE_ROOT_SIZE = 0x50
TEXTURE_ROW = ">IBBBBI"
STATE_SIZE = 0x24
IMAGE_PROFILE = re.compile(
    rb"[\x80-\xbe]\x01\x02[\x00\x01]\x00...\x00\x01\x00\x01\x00\x01[\x00-\x03]", re.S
)
BASE_TEXTURES = {"colorMap": 0xa0ab1041, "normalMap": 0x59d30d0f,
                 "specularMap": 0x34ecccb3}
STOCK_MATERIALS = {
    "world": "wc/ch_stonewall_02",
    "world_layer_base": "w/ch_stonewall_02",
    "world_cutout": "wc/window_curtain01_tan",
    "decal": "wc/com_concrete_core_dec",
    "glass": "wc/ct_glass01",
    "water": "wc/water_stream_scrolling",
    "water_distortion": "wc/water_stream_rolling_dist",
    "sky": "wc/sky_sp_arcadia",
    "model": "mc/mtl_tree_green_pine_bark",
    "model_blend": "mc/mtl_ch46e_glass",
    "cutout": "mc/mtl_desertshrubs",
    "effect": "gfx_wind_hl_atlas",
    "effect_add": "gfx_fire_roar_pm_atlas",
}
# Owned records in the retail common multiplayer zones. Technique names are
# supplied where the packed reference or material variant identifies the family.
SHARED_MATERIALS = {
    "common_mp": {
        "gfx_blood_drops_atlas": (0xf24cf9, "effect_zfeather_blend"),
        "gfx_blood_exit_wound_atlas": (0xf24ffd, "effect_zfeather_blend"),
        "gfx_distortion_heat": (0x135b3f1, "distortion_scale"),
        "gfx_test01": (0x13f59aa, ""),
        "spark_rndhit_64_sort": (0x10f16b9, "particle_cloud_spark_add"),
        "vfx_blood_ball_cg": (0x1014bc0, "effect_zfeather_blend"),
        "vfx_blood_body_atlas_cg": (0xf25c27, "effect_zfeather_blend_tab"),
        "vfx_blood_spray_bits": (0x1052f54, "effect_zfeather_blend_lin"),
        "vfx_smk_white_anim_cg": (0xf006e6, "effect_zfeather_blend"),
    },
    "common_core_mp": {
        "mq/mtl_mp_head_e": (0x391b134, "mq_l_sm_rim_r0c0n0sf0"),
        "mq/mtl_shiny_lense_eye": (0x37f45e6, "mq_l_sm_b0c0n0s0p0"),
        "mq/mtl_viewmodel_gs_hostage_skin": (0x380a5b0, "mq_l_sm_r0c0n0sf0"),
        "vfx_fire_fuelflame_1": (0xebc4e0, "effect_zfeather_add_lin_nofog"),
        "vfx_lensflare_glow": (0xec3a1a, "effect_add_lin_nofog"),
    },
}
COMMON_MP_IMAGES = {
    "sparks_rndhit_64": 0x10f10f0,
    "vfx_spark_round_atlas": 0x135342b,
}
COMMON_CORE_MP_IMAGES = {
    "viewmodel_devgru_b_nml_360": 0x3a448c8,
}
# Change only the fragment program on native IW4 passes with matching inputs.
# The two non-stock fragments are owned by the corresponding mp_swamp passes.
EFFECT_VARIANTS = {
    "effect_add_lin_nofog": ("effect_add", "vertcol_simple_add_lin", None),
    "effect_zfeather_add_lin_nofog": ("zfeather_add", "zfeather_premul_nf_lin", None),
    "effect_zfeather_blend_lin_nofog": ("zfeather", "zfeather_lin", 0xae2792),
    "particle_cloud_spark_replace": ("particle_cloud_spark_premul", "particle_cloud_spark", 0xad2c2d),
}
FX_IMAGE_ALIASES = {
    0x400B2B69: (0x9F752B, "vfx_smk_veiny_loop"),
    0x401260C1: (0xACF835, "flare_halogen_glw"),
}
# Swamp IW6 technique-set handles requiring native IW4 unlit passes.
UNLIT_CUTOUT = "w/bg_facades_afghan_house"
UNLIT_TEMPLATES = {
    0x400088C5: "w/ch_dec_darkstain_02",       # w_unlit_multiply_lin
    0x4000893D: "wc/tree_godray",              # wc_unlit_falloff_screen_lin
    0x4000894D: UNLIT_CUTOUT,                  # w_unlit_atest_lin_nocast
    0x40008A75: UNLIT_CUTOUT,                  # wc_unlit_atest_lin_nocast
    0x40008CC5: "wc/ch_dec_darkstain_02",      # wc_unlit_multiply_lin
    0x40008CE5: "w/hdrportal_lighten",         # w_unlit_distfalloff_replace
    0x40008E4D: "wc/ch_hotspot01",             # wc_unlit_add_lin
    0x40008F0D: "w/caulk_shadow",              # w_tools_r0c0
}


def u32(data, offset):
    return unpack_from(">I", data, offset)[0]


@cache
def property_hash(name):
    value = 0
    for char in name.encode("ascii"):
        value = ((value * 33) ^ (char | 0x20)) & 0xffffffff
    return value


def sampler_state(value):
    return {"filter": ("disabled", "nearest", "linear", "aniso2x", "aniso4x")[value & 7],
            "mipMap": ("disabled", "nearest", "linear")[(value >> 3) & 3],
            "clampU": bool(value & 0x20), "clampV": bool(value & 0x40), "clampW": bool(value & 0x80)}


def slot_hash(entry):
    return property_hash(entry["name"]) if "name" in entry else entry["nameHash"]


def bind_constants(material, entries):
    def literal(entry):
        return material.constants.get(slot_hash(entry), entry["literal"])
    return [{**entry, "literal": literal(entry)} for entry in entries]


def string(data, offset):
    end = data.find(b"\x00", offset, offset + 256)
    if end < 0:
        raise ValueError(f"Missing asset name at zone offset {offset:#x}")
    return data[offset:end].decode("ascii"), end + 1


def align(value, boundary):
    return (value + boundary - 1) & -boundary


def json_out(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def asset_name(name):
    name = name.lstrip(",")
    if name.startswith("*"):
        return "iw6/mp_swamp/blend_" + name[1:]
    return name


def mip_bytes(format_byte, width, height, depth, levels, cubemap):
    base = format_byte & 0x9f
    result = 0
    for level in range(levels):
        w, h, d = (max(1, value >> level) for value in (width, height, depth))
        if base in (0x86, 0x87, 0x88):
            result += ((w + 3) // 4) * ((h + 3) // 4) * d * (8 if base == 0x86 else 16)
        elif base in (0x81, 0x85, 0x8b, 0x9c, 0x9e, 0x9f):
            result += w * h * d * {0x81: 1, 0x8b: 2}.get(base, 4)
        else:
            raise ValueError(f"Unsupported resident RSX image format {format_byte:#x}")
    return align(result, 128) * (6 if cubemap else 1)


def profile(data, root):
    records = unpack_from(">HHIHHIHHIHHI", data, root + 0x2c)
    cumulative = 0
    parts = []
    for w, h, field in zip(records[::3], records[1::3], records[2::3]):
        current = field & 0x3ffffff
        if w == h == field == 0:
            parts.append((0, 0, 0, 0))
        elif w > 0 and h > 0 and current > cumulative and all(x[3] for x in parts):
            parts.append((w, h, field, current - cumulative))
            cumulative = current
        else:
            return ()
    return tuple(parts)


def stream_table(fastfile, zone):
    count = u32(fastfile, 0x18)
    if fastfile[:8] != b"IWff0100" or u32(fastfile, 8) != 558 or count % 4:
        raise ValueError("Expected an IW6 PS3 fastfile and four rows per streamed image")
    rows = list(iter_unpack(">III", fastfile[0x1c:0x1c + count * 12]))
    descriptors = []
    for match in IMAGE_PROFILE.finditer(zone):
        pos = match.start()
        if zone[pos + 0x18] not in (3, 4, 5) or zone[pos + 0x1a] != 3 or u32(zone, pos + 0x28):
            continue
        name = u32(zone, pos + 0x4c)
        if name not in (OWNED, INSERT) and not 0x40000000 <= name < 0x43000000:
            continue
        parts = profile(zone, pos)
        if parts and parts[0][3]:
            descriptors.append((pos, parts))
    if len(descriptors) * 4 != count:
        raise ValueError(f"Found {len(descriptors)} streamed image descriptors for {count // 4} package entries")
    result = {}
    for i, (pos, parts) in enumerate(descriptors):
        entries = rows[i * 4:i * 4 + 4]
        if any(bool(part[3]) != bool(entry[0]) for part, entry in zip(parts, entries)):
            raise ValueError(f"Stream profile mismatch for image #{i} at {pos:#x}")
        result[pos] = (parts, entries)
    return result


@dataclass
class Texture:
    hash: int
    sampler: int
    semantic: int
    pointer: int
    image: int | None = None


@dataclass
class Material:
    root: int
    name: str
    sort: int
    atlas: tuple
    surface_bits: int
    camera: int
    state_flags: int
    technique_set: int
    textures: list
    constants: dict
    layers: tuple
    end: int


class Source:
    def __init__(self, zone, streams, image_namespace="iw6/mp_swamp", package_directory=None):
        self.zone = zone
        self.streams = streams
        self.image_namespace = image_namespace
        self.package_directory = package_directory
        self.image_aliases = {}
        self.imported_images = {}
        self.imported_materials = {}
        self.layer_sources = {}

    @cache
    def technique_sets(self):
        handles = [ASSET_HANDLE + i * 8 for i in range(ASSET_COUNT)
                   if u32(self.zone, ASSET_TABLE + i * 8) == 8]
        pattern = re.compile(rb"\xff{4}.{160}(,?[A-Za-z0-9_$][A-Za-z0-9_$./-]{1,130})\0", re.S)
        roots = []
        start = 0
        while match := pattern.search(self.zone, start, WORLD_ROOT):
            start = match.start() + 1
            layout, *refs = unpack_from(">40I", self.zone, match.start() + 4)
            if layout not in range(0, 0x800, 0x100):
                continue
            if all(ref in (0, OWNED, INSERT) or 0x40000000 <= ref < 0x43000000 for ref in refs):
                roots.append((match.start(), match[1].decode("ascii").lstrip(",")))
        if len(roots) != len(handles):
            raise ValueError(f"Expected {len(handles)} map technique sets, found {len(roots)}")
        return dict(zip(handles, roots, strict=True))

    @cache
    def image_record(self, pos):
        zone = self.zone
        fmt, levels, dimension, cube = zone[pos:pos + 4]
        width, height, depth = unpack_from(">HHH", zone, pos + 8)
        end = pos + IMAGE_ROOT_SIZE
        name_pointer = u32(zone, pos + 0x4c)
        if name_pointer in (OWNED, INSERT):
            name, end = string(zone, end)
        else:
            name = None
        if name and name.startswith(","):
            return name, end, 0, end
        if not fmt & 0x80 or not levels or dimension not in (1, 2, 3):
            raise ValueError(f"Invalid GfxImage at zone offset {pos:#x}")
        size = 0
        if u32(zone, pos + 0x28):
            size = mip_bytes(fmt, width, height, depth, levels, cube)
        return name, end, size, end + size

    @cache
    def material(self, pos):
        zone = self.zone
        root = zone[pos:pos + MATERIAL_ROOT_SIZE]
        if u32(root, 0) != OWNED:
            raise ValueError(f"Invalid Material at zone offset {pos:#x}")
        name, end = string(zone, pos + MATERIAL_ROOT_SIZE)
        textures = []
        constants = {}
        layer_refs = ()
        if not name.startswith(","):
            texture_count, constant_count, state_count = root[0x47:0x4a]
            techset, texptr, constantptr, stateptr, layerptr = unpack_from(">5I", root, 0xa0)
            if techset in (OWNED, INSERT):
                raise ValueError(f"Unexpected inline technique set at Material {pos:#x}")
            if texptr in (OWNED, INSERT):
                for _ in range(texture_count):
                    hash_value, _, _, sampler, semantic, pointer = unpack_from(TEXTURE_ROW, zone, end)
                    textures.append(Texture(hash_value, sampler, semantic, pointer))
                    end += 12
                for tex in textures:
                    if tex.pointer not in (OWNED, INSERT):
                        continue
                    tex.image = end
                    _, _, _, end = self.image_record(end)
            if constantptr in (OWNED, INSERT):
                for _ in range(constant_count):
                    key = u32(zone, end)
                    color = unpack_from(">4f", zone, end + 16)
                    if all(map(isfinite, color)):
                        constants[key] = color
                    end += 32
            if stateptr in (OWNED, INSERT):
                inline_states = sum(u32(zone, end + index * STATE_SIZE) in (OWNED, INSERT)
                                    for index in range(state_count))
                end += state_count * STATE_SIZE
                end += inline_states * 12
            if layerptr in (OWNED, INSERT):
                count = root[0x4d]
                layer_refs = unpack_from(f">{count}I", zone, end)
                end += count * 4
        return Material(pos, name, root[5], (root[6], root[7]), u32(root, 0x18),
                        root[0x4b], root[0x4a], u32(root, 0xa0), textures, constants, layer_refs, end)

    def named_materials(self, start, end, prefix=None):
        zone = self.zone
        for match in re.finditer(rb"\xff{4}", zone[start:end]):
            pos = start + match.start()
            root = zone[pos:pos + MATERIAL_ROOT_SIZE]
            if len(root) != MATERIAL_ROOT_SIZE:
                continue
            tail = zone[pos + MATERIAL_ROOT_SIZE:pos + MATERIAL_ROOT_SIZE + 128]
            name = tail.partition(b"\x00")[0]
            if prefix is not None and not name.lstrip(b",").startswith(prefix):
                continue
            if re.fullmatch(rb",?[A-Za-z0-9_*~$-][A-Za-z0-9_./*~$&+()-]*", name) is None:
                continue
            pointers = unpack_from(">6I", root, 0xa0)
            if not pointers[0] and not name.startswith(b","):
                continue
            if any(x not in (0, OWNED, INSERT) and not 0x40000000 <= x < 0x43000000 for x in pointers):
                continue
            if root[5] > 70 or root[0x47] > 32 or root[0x49] > 16:
                continue
            yield self.material(pos)

    def world_materials(self):
        zone = self.zone
        slots = [ASSET_HANDLE + i * 8 for i in range(1790, ASSET_COUNT)
                 if u32(zone, ASSET_TABLE + i * 8) == 5]
        named = list(self.named_materials(FIRST_WORLD_MATERIAL, WORLD_ROOT, b"w/"))
        if len(slots) != len(named):
            raise ValueError(f"Expected {len(slots)} named world materials, found {len(named)}")
        result = dict(zip(slots, named))
        cursor = MATERIAL_MEMORY + MATERIAL_MEMORY_COUNT * 8
        owned = []
        for index in range(MATERIAL_MEMORY_COUNT):
            ref = u32(zone, MATERIAL_MEMORY + index * 8)
            if ref == OWNED:
                mat = self.material(cursor)
                cursor = mat.end
                owned.append(mat)
                result[MATERIAL_MEMORY_HANDLE + index * 8] = mat
            elif ref not in result:
                raise ValueError(f"Unknown world material handle {ref:#x}")
        source_ptrs = sorted({pointer for material in owned for pointer in material.layers})
        if len(source_ptrs) != len(named):
            raise ValueError(f"Expected {len(named)} material layer names, found {len(source_ptrs)}")
        self.layer_sources = dict(zip(source_ptrs, named))
        for pointer, material in self.layer_sources.items():
            table = align(pointer - 1 + len(material.name) + 1, 4) + 1
            for i, tex in enumerate(material.textures):
                self.image_aliases[table + i * 12 + 8] = tex
        self.image_aliases[WORLD_SKY_HANDLE] = Texture(0, 0, 2, OWNED, WORLD_SKY_IMAGE)
        for handle, (root, name, semantic) in MAP_IMAGE_IMPORT_ALIASES.items():
            if self.image_record(root)[0] != name:
                raise ValueError(f"Unexpected IW6 image at {root:#x}: expected {name}")
            self.image_aliases[handle] = Texture(0, 0, semantic, OWNED, root)
        self.bind_world_variants(result.values())
        return result

    def bind_world_variants(self, materials):
        named = {mat.name: mat for mat in materials}
        for mat in named.values():
            if not mat.name.startswith("wc/"):
                continue
            variant = named.get("w/" + mat.name[3:])
            if variant is None or mat.constants != variant.constants or len(mat.textures) != len(variant.textures):
                continue
            pairs = list(zip(mat.textures, variant.textures))
            if any((a.hash, a.semantic) != (b.hash, b.semantic) for a, b in pairs):
                continue
            for packed, source in pairs:
                if (packed.pointer != 0 and packed.image is None and
                        self.image_for_texture(packed) is None and self.image_for_texture(source) is not None):
                    self.image_aliases[packed.pointer] = source

    def image_for_texture(self, texture):
        if texture is None:
            return None
        if texture.image is not None:
            return texture.image
        source = self.image_aliases.get(texture.pointer)
        if source is None or source is texture:
            return None
        return self.image_for_texture(source)

    def texture_for_material(self, material, name):
        binding = property_hash(name) if isinstance(name, str) else name
        return next((tex for tex in material.textures if tex.hash == binding), None)

    def image_for_material(self, material, name):
        texture = self.texture_for_material(material, name)
        pos = self.image_for_texture(texture)
        if pos is None and len(material.layers) > 1 and name == "colorMap":
            base = self.layer_sources[material.layers[0]]
            pos = self.image_for_texture(self.texture_for_material(base, name))
        if pos is None and texture is None and name == "colorMap":
            colors = [tex for tex in material.textures if tex.semantic in (0, 2)]
            if len(colors) == 1:
                pos = self.image_for_texture(colors[0])
        return pos

    def bind_effect_images(self):
        for handle, (root, name) in FX_IMAGE_ALIASES.items():
            if self.image_record(root)[0] != name or root not in self.streams:
                raise ValueError(f"Unexpected IW6 FX image at {root:#x}: expected {name}")
            self.image_aliases[handle] = Texture(BASE_TEXTURES["colorMap"], 0, 2, OWNED, root)


def load_shared_materials(source, shared_zones):
    for fastfile, decoded_zone in shared_zones:
        package = fastfile.stem
        zone = decoded_zone.read_bytes()
        owner = Source(zone, stream_table(fastfile.read_bytes(), zone), f"iw6/{package}", fastfile.parent)
        images = COMMON_MP_IMAGES if package == "common_mp" else COMMON_CORE_MP_IMAGES
        for name, root in images.items():
            if owner.image_record(root)[0] != name or root not in owner.streams:
                raise ValueError(f"Unexpected {package} image {name} at {root:#x}")
            source.imported_images[name] = owner, root
        if package == "common_mp":
            # The sorted campfire material shares the sprite's image descriptor.
            owner.image_aliases[0x40ec6eb5] = Texture(
                BASE_TEXTURES["colorMap"], 0x0a, 2, OWNED, COMMON_MP_IMAGES["sparks_rndhit_64"])
        else:
            # The hands' skin and sleeve use separate passes over the same UV atlas.
            sleeve = owner.material(0x380a22a)
            if sleeve.name != "mq/mtl_viewmodel_gs_hostage_sleeve":
                raise ValueError(f"Unexpected {package} hands material at {sleeve.root:#x}")
            for pointer, slot in ((0x417d84a1, "specularMap"), (0x417d84ad, "normalMap"),
                                  (0x417d84b9, "colorMap")):
                texture = owner.texture_for_material(sleeve, slot)
                if owner.image_for_texture(texture) not in owner.streams:
                    raise ValueError(f"Missing {slot} in {sleeve.name}")
                owner.image_aliases[pointer] = texture
        for name, (root, technique) in SHARED_MATERIALS[package].items():
            material = owner.material(root)
            if material.name != name or owner.image_for_material(material, "colorMap") not in owner.streams:
                raise ValueError(f"Unexpected {package} material {name} at {root:#x}")
            source.imported_materials["," + name] = owner, material, technique


def model_materials(source, records):
    materials = {}
    owners = {}
    references = {}
    tables = []
    for record in records:
        table = record["material_table_offset"]
        pointers = [int(pointer, 16) for pointer in record["material_pointers"]]
        cursor = table + 4 * len(pointers)
        inline = {}
        for i, pointer in enumerate(pointers):
            if pointer in (OWNED, INSERT):
                mat = source.material(cursor)
                materials[cursor] = mat
                inline[i] = mat
                cursor = mat.end
            elif pointer:
                references.setdefault(pointer, table)
        tables.append((table, pointers, inline))

    # XModel material aliases refer to earlier four-byte handle slots. Repeated
    # LOD handles can identify the table's virtual base when their positions
    # coincide with every inline slot's relative offset.
    for table, pointers, inline in tables:
        new = {pointer for pointer in pointers if references.get(pointer) == table}
        candidates = {}
        for i, pointer in enumerate(pointers):
            if pointer not in new:
                continue
            for slot in inline:
                if slot >= i:
                    continue
                base = pointer - slot * 4
                matched = {base + index * 4: material for index, material in inline.items()
                           if base + index * 4 in new}
                if matched and all(ptr not in matched or (ptr - base) // 4 < index
                                   for index, ptr in enumerate(pointers)):
                    candidates[base] = matched
        if candidates:
            strong = max(map(len, candidates.values()))
            matches = [matched for matched in candidates.values() if len(matched) == strong]
            if len(matches) == 1 and set(matches[0]) == new:
                owners.update(matches[0])
                base = next(base for base, matched in candidates.items() if matched is matches[0])
                first = next(iter(inline.values()))
                texture_table = align(base - 1 + len(pointers) * 4 + len(first.name) + 1, 4) + 1
                for index, tex in enumerate(first.textures):
                    if tex.image is not None:
                        source.image_aliases[texture_table + index * 12 + 8] = tex
    for pointer, root in MODEL_MATERIAL_HANDLES.items():
        if pointer in references:
            owners[pointer] = source.material(root)
    model_slots = [
        {"name": record["source_model_name"], "handles": pointers,
         "inlineRoots": [inline[i].root if i in inline else -1 for i in range(len(pointers))]}
        for record, (_, pointers, inline) in zip(records, tables, strict=True)
    ]
    return materials, owners, model_slots


def material_role(mat):
    name = mat.name.lstrip(",")
    base_name = name.partition("/")[2]
    if name.startswith("wc/sky_"):
        return "sky"
    if name.startswith(("mq/", "mqc/", "mqg/", "mvq/", "mvqc/", "m/")):
        if mat.camera == 1:
            return "model_blend"
        # The observed foliage, reeds and netting use this cull-free
        # depth-writing state. The IW4 cutout technique consumes their alpha.
        return "cutout" if mat.state_flags == 0x38 or mat.sort in (0, 14) else "model"
    if base_name == "water_stream_scrolling_slow":
        return "water"
    if base_name in {"aquarium_water_distortion", "water_distortion_w_z_feather"}:
        return "water_distortion"
    if mat.camera == 0 and mat.state_flags == 0x38:
        return "world_cutout"
    if mat.camera == 1 and "glass" in base_name:
        return "glass"
    if mat.camera == 1 or mat.sort in (6, 7, 8, 9, 10, 11, 12, 13, 14, 17, 18, 20, 24, 25, 29, 43):
        return "decal"
    # The compiled material stores layer weights in vertex RGBA. Its base
    # texture uses the stock lit pass without vertex-color modulation.
    if len(mat.layers) > 1:
        return "world_layer_base"
    return "world"


def material_rules(source, materials, effects, models):
    techniques = {handle: name for handle, (_, name) in source.technique_sets().items()}
    facts = []
    for local in materials.values():
        owner, mat, technique = source.imported_materials.get(
            local.name, (source, local, techniques.get(local.technique_set, "")))
        role = material_role(mat)
        if local.root in effects:
            role = "effect_add" if "add" in mat.name.split("_") else "effect"
        facts.append({"root": local.root, "name": mat.name, "technique": technique,
                      "role": role, "surfaceBits": mat.surface_bits,
                      "layers": [owner.layer_sources[ref].name for ref in mat.layers]})
    output = subprocess.run(["dotnet", "fsi", "--exec", Path(__file__).with_name("MaterialRules.fsx")],
                            input=json.dumps({"materials": facts, "models": models}),
                            stdout=subprocess.PIPE, text=True, check=True)
    decisions = json.loads(output.stdout)
    return {mat["root"]: mat for mat in decisions["materials"]}, decisions["bindings"]


class Target:
    def __init__(self, source, raw, iw4):
        self.source = source
        self.raw = raw
        self.iw4 = iw4
        self.images = {}
        self.materials = set()
        self.fallbacks = set()

    @cache
    def copy_source(self, relative):
        source = self.iw4 / relative
        target = self.raw / relative
        if not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            copyfile(source, target)
        return source

    @cache
    def stock_image(self, name):
        relative_name = "images/" + name.replace("*", "_")
        path = self.copy_source(relative_name + ".image.json")
        metadata = json.loads(path.read_bytes())
        if metadata.get("version") == 2:
            self.copy_source(relative_name + ".pixels.bin")
            for index, part in enumerate(metadata["streamData"]):
                if part["levelSizeAndOffset"]:
                    self.copy_source(relative_name + f".stream{index}.bin")
        else:
            self.copy_source(relative_name + ".dds")
        return name

    def bind_image(self, source, image):
        if image is None:
            return None
        name = source.image_record(image)[0]
        if name and name.startswith(","):
            name = name[1:]
            owner = self.source.imported_images.get(name)
            if owner is None:
                stock = self.iw4 / "images" / (name.replace("*", "_") + ".image.json")
                return self.stock_image(name) if stock.is_file() else None
            source, image = owner
        if not name:
            name = f"{source.image_namespace}/image_{image:x}"
        elif source is not self.source:
            name = f"{source.image_namespace}/{name}"
        return self.images.setdefault((source, image), name)

    @cache
    def stock_technique(self, name):
        path = self.copy_source(f"techniques/{name}.tech.json")
        technique = json.loads(path.read_bytes())
        for render_pass in technique["passes"]:
            for kind in ("vertex", "pixel"):
                shader = render_pass.get(kind + "Shader")
                if shader is not None:
                    shader_path = Path("shader_bin_ps3") / kind / Path(shader).with_suffix(".cg")
                    self.copy_source(shader_path)
                    self.copy_source(shader_path.with_suffix(".cg.json"))
        return technique

    @cache
    def stock_material(self, name):
        path = self.iw4 / "materials" / (name + ".json")
        material = json.loads(path.read_bytes())
        techset_path = self.copy_source("techsets/" + material["techniqueSet"] + ".techset.json")
        for name in set(json.loads(techset_path.read_bytes())["techniques"]) - {None}:
            self.stock_technique(name)
        return material

    @cache
    def stock_surface_materials(self):
        routes = {}
        for prefix in ("m", "mc", "mg", "w", "wc"):
            for path in sorted((self.iw4 / "materials" / prefix).glob("*.json")):
                data = json.loads(path.read_bytes())
                name = f"{prefix}/{path.stem}"
                routes.setdefault(data["techniqueSet"], []).append(
                    (name, data["cameraRegion"], data["stateFlags"], data["sortKey"]))
        return routes

    def native_surface(self, mat, techniques):
        region = ("litOpaque", "litTrans", "emissive", "depthHack", "none")
        for technique in techniques:
            candidates = self.stock_surface_materials().get(technique, ())
            if candidates:
                # State tables belong to their technique slots. Prefer the original
                # named donor, then match the authoring class within that family.
                name = mat.name.partition("/")[2]
                return min(candidates, key=lambda row: (
                    row[0].partition("/")[2] != name, row[1] != region[min(mat.camera, 4)],
                    (row[2] ^ mat.state_flags).bit_count(), abs(row[3] - mat.sort), row[0]))[0]
        return None

    @cache
    def stock_terrain_materials(self):
        prototypes = {}
        generated = self.iw4 / "materials/generated"
        for path in sorted(generated.glob("*.json")):
            material = json.loads(path.read_bytes())
            key = material["techniqueSet"]
            region = material["cameraRegion"]
            sort = material["sortKey"]
            prototypes.setdefault(key, {}).setdefault((region, sort), f"generated/{path.stem}")
        return prototypes

    @cache
    def terrain_prototype(self, technique, camera, sort):
        region = "litOpaque" if camera == 0 else "litTrans"
        candidates = self.stock_terrain_materials().get(technique, {})
        key = min(candidates, key=lambda k: (k[0] != region, abs(k[1] - sort)), default=None)
        if key is None or key[0] != region:
            raise ValueError(f"No native IW4 {region} material for terrain technique {technique}")
        return candidates[key]

    def terrain_material_inputs(self, prototype, mat):
        neutral_images = {"normalMap": "$identitynormalmap", "specularMap": "$black"}
        textures = [{**stock, "image": neutral_images.get(stock["semantic"], stock["image"])}
                    for stock in prototype["textures"]]
        constants = prototype["constants"].copy()
        techset, reveal_layers = self.terrain_techset(prototype["techniqueSet"], mat.technique_set)
        if reveal_layers:
            for index in reveal_layers:
                suffix = str(index) if index else ""
                name = "revealMap" + suffix
                param = "revealParams" + suffix
                textures.append({"name": name, "image": "$white", "semantic": "colorMap",
                                 "samplerState": sampler_state(0x12)})
                constants.append({"name": param, "literal": mat.constants[property_hash(param)]})
        xstrings = [self.source.layer_sources[ref].name for ref in mat.layers]
        return {**prototype, "techniqueSet": techset, "textures": textures, "constants": constants,
                "sortKey": mat.sort, "_native": {**prototype["_native"], "xstrings": xstrings}}

    @cache
    def terrain_stride(self, technique):
        techset = json.loads((self.iw4 / f"techsets/{technique}.techset.json").read_bytes())
        # Native PS3 rows after the one-UV, one-normal world declaration.
        return (28, 36, 40, 44, 48, 52, 52, 56, 60)[techset["worldVertexFormat"]]

    @cache
    def terrain_fragments(self):
        sets = self.source.technique_sets()
        start = min(root for root, name in sets.values() if name.startswith("l_sm_"))
        programs = {}
        for name, program, rows in reveal.inline_fragment_passes(self.source.zone[start:WORLD_ROOT]):
            features = tuple(sorted(re.findall(r"hb[0-3]|v[0-3]|srx", name)))
            if features:
                family = re.sub(r"hb[0-3]|v[0-3]|srx", "", name)
                programs.setdefault((family, features), set()).add((program, rows))
        return programs

    def terrain_fragment(self, technique, candidates, slots, prefix):
        if technique["version"] != 1 or len(technique["passes"]) != 1:
            return None
        render_pass = technique["passes"][0]
        shader_name = Path(render_pass["pixelShader"])
        path = Path("shader_bin_ps3/pixel") / shader_name.with_suffix(".cg")
        native_program = (self.iw4 / path).read_bytes()
        matches = []
        for program, rows in candidates:
            adapted = reveal.adapt_material_pass(render_pass, native_program, program, rows, slots)
            if adapted is not None:
                matches.append((program, adapted))
        if len(matches) != 1:
            return None
        metadata = json.loads((self.iw4 / path.with_suffix(".cg.json")).read_bytes())
        if metadata["programBytes"] != "AAAAAAAAAAAAAAAA":
            return None
        program, adapted = matches[0]
        name = prefix + technique["name"]
        adapted["pixelShader"] = str(shader_name.with_name(name + shader_name.suffix))
        self.stock_technique(technique["name"])
        metadata["name"] = adapted["pixelShader"]
        output = self.raw / path.with_name(name + path.suffix)
        json_out(output.with_suffix(".cg.json"), metadata)
        output.write_bytes(program)
        json_out(self.raw / f"techniques/{name}.tech.json", {**technique, "name": name, "passes": [adapted]})
        return name

    def terrain_aliased_passes(self, techniques, owner, slots, prefix):
        start, end = owner
        programs = reveal.inline_fragment_passes(self.source.zone[start:end])
        # Ghosts lit slots 7–16 correspond to IW4 slots 9–18. Keep separate
        # entries even when the IW4 set points its fog slot at the base pass.
        declaration = self.stock_technique(techniques[9])["passes"][0]["vertexDeclaration"]
        adapted = False
        for index, (source_name, program, rows) in zip(range(9, 19), programs, strict=True):
            name = re.sub(r"v[0-3]", "", source_name)
            path = self.iw4 / f"techniques/{name}.tech.json"
            if not path.is_file():
                continue
            technique = json.loads(path.read_bytes())
            stock_name = techniques[index]
            if (technique["version"] != 1 or len(technique["passes"]) != 1 or
                    technique["flags"] != self.stock_technique(stock_name)["flags"] or
                    technique["passes"][0]["vertexDeclaration"] != declaration):
                continue
            replacement = self.terrain_fragment(technique, ((program, rows),), slots, prefix)
            if replacement:
                techniques[index] = replacement
                adapted = True
            elif name.endswith("_dfog_sm3") and not stock_name.endswith("_dfog_sm3"):
                # The complete IW4 fog pass has its own compatible vertex
                # shader and bindings when the Ghosts fragment ABI differs.
                self.stock_technique(name)
                techniques[index] = name
        return adapted

    @cache
    def terrain_techset(self, native_name, source_handle):
        root, source_name = self.source.technique_sets()[source_handle]
        layers = tuple(int(index) for index in re.findall(r"v([0-3])", source_name))
        features = ["v" + str(index) for index in layers]
        # The PS3 Ghosts fragment names fold secondary height options into
        # their ordinary blend passes. Opaque layer-zero height still has a
        # distinct name in the serialized technique table.
        if source_name.startswith("l_sm_r0hb0"):
            features.append("hb0")
        if source_name.endswith("sr0"):
            features.append("srx")
        owner = TERRAIN_LIT_PASS_OWNERS.get(root)
        if not features and owner is None:
            return native_name, ()

        slots = {}
        for layer in layers:
            suffix = str(layer) if layer else ""
            slots[property_hash("revealMap" + suffix)] = ("revealMapSampler" + suffix).encode("ascii")
            slots[property_hash("revealParams" + suffix)] = ("revealParams" + suffix).encode("ascii")
        techset = json.loads((self.iw4 / f"techsets/{native_name}.techset.json").read_bytes())
        prefix = f"iw6_terrain_{root:x}_"
        original = techset["techniques"]
        techniques = original.copy()
        if owner is not None:
            adapted = self.terrain_aliased_passes(techniques, owner, slots, prefix)
        else:
            programs = self.terrain_fragments()
            feature_key = tuple(sorted(features))
            replacements = {}
            for name in dict.fromkeys(original[9:23]):
                candidates = programs.get((name, feature_key))
                if candidates:
                    technique = self.stock_technique(name)
                    replacement = self.terrain_fragment(technique, candidates, slots, prefix)
                    if replacement:
                        replacements[name] = replacement
            adapted = bool(replacements)
            techniques = [replacements.get(name, name) for name in original]
        if techniques == original:
            return native_name, ()
        techset["name"] = prefix + native_name
        techset["techniques"] = techniques
        json_out(self.raw / f'techsets/{techset["name"]}.techset.json', techset)
        return techset["name"], layers if adapted else ()

    @cache
    def effect_techset(self, stock_name, variant):
        technique_name, pixel_shader, source_offset = EFFECT_VARIANTS[variant]
        techset = json.loads((self.iw4 / f"techsets/{stock_name}.techset.json").read_bytes())
        technique = json.loads((self.iw4 / f"techniques/{technique_name}.tech.json").read_bytes())
        if technique_name not in techset["techniques"] or technique["version"] != 1 or len(technique["passes"]) != 1:
            raise ValueError(f"Unexpected IW4 effect pass for {variant}")
        render_pass = technique["passes"][0]
        name = "iw6_swamp_fx_" + variant
        pixel_path = Path("shader_bin_ps3/pixel") / f"{pixel_shader}.cg"
        if source_offset is None:
            self.copy_source(pixel_path)
            self.copy_source(pixel_path.with_suffix(".cg.json"))
        else:
            zone = self.source.zone
            fragment = zone[source_offset:source_offset + u32(zone, source_offset + 8)]
            stock_path = Path("shader_bin_ps3/pixel") / Path(render_pass["pixelShader"]).with_suffix(".cg")
            donor = reveal.fragment_layout((self.iw4 / stock_path).read_bytes())
            source = reveal.fragment_layout(fragment)
            if (source.varyings != donor.varyings or source.samplers != donor.samplers or
                    [row[:1] + row[2:] for row in source.parameters] !=
                    [row[:1] + row[2:] for row in donor.parameters]):
                raise ValueError(f"Incompatible Ghosts effect fragment for {variant}")
            metadata = json.loads((self.iw4 / stock_path.with_suffix(".cg.json")).read_bytes())
            pixel_shader = name
            metadata["name"] = pixel_shader + ".hlsl"
            pixel_path = pixel_path.with_name(pixel_shader + ".cg")
            json_out(self.raw / pixel_path.with_suffix(".cg.json"), metadata)
            (self.raw / pixel_path).write_bytes(fragment)
        render_pass["pixelShader"] = pixel_shader + ".hlsl"
        technique["name"] = name
        techset["name"] = name
        techset["techniques"] = [name if slot == technique_name else slot for slot in techset["techniques"]]
        json_out(self.raw / f"techniques/{name}.tech.json", technique)
        json_out(self.raw / f"techsets/{name}.techset.json", techset)
        return name

    @cache
    def feature_programs(self, handle):
        sets = self.source.technique_sets()
        root = sets[handle][0]
        end = min((pos for pos, _ in sets.values() if pos > root), default=WORLD_ROOT)
        return tuple(reveal.inline_fragment_passes(self.source.zone[root:end], surface_fragments.PASS_NAME))

    @cache
    def surface_fragment(self, handle, feature, template):
        programs = self.feature_programs(handle)
        if feature == "dust":
            # Some sets reference every pass by pointer. The same named dust
            # passes are owned by sibling sets. Reuse only a matching pass
            # name and fragment interface on the target vertex declaration.
            programs += tuple(item for other, (_, name) in self.source.technique_sets().items()
                              if other != handle and "_du_dm_" in name
                              for item in self.feature_programs(other))
        return surface_fragments.convert(programs, feature, self.stock_material(template),
                                         self.iw4, self.raw, property_hash, json_out)

    @cache
    def wind_techset(self, family, stock_name, source_handle):
        source_root, _ = self.source.technique_sets()[source_handle]
        return wind.bind(self.source.zone, family, stock_name, self.iw4, self.raw, property_hash, json_out,
                         source_root=source_root)

    @cache
    def reveal_techset(self, stock_name):
        # The standalone stone path aliases this color-weighted reveal family.
        # The painted path also needs these passes to consume its vertex mask.
        # Bound the scan by the next owned technique-set root.
        source_root = self.source.technique_sets()[0x40008aed][0]
        programs = reveal.source_programs(self.source.zone[source_root:0x10b10ab])
        stock_set = self.iw4 / f"techsets/{stock_name}.techset.json"
        techset = json.loads(stock_set.read_bytes())
        shaders = {}
        techniques = {}
        prefix = "iw6_swamp_reveal_"
        for name in dict.fromkeys(techset["techniques"]):
            if name not in programs:
                continue
            technique = json.loads((self.iw4 / f"techniques/{name}.tech.json").read_bytes())
            if technique["version"] != 1 or len(technique["passes"]) != 1:
                continue
            render_pass = technique["passes"][0]
            path = Path("shader_bin_ps3/pixel") / Path(render_pass["pixelShader"]).with_suffix(".cg")
            fragment, rows = programs[name]
            adapted = reveal.adapt_pass(render_pass, (self.iw4 / path).read_bytes(), fragment, rows)
            metadata = json.loads((self.iw4 / path.with_suffix(".cg.json")).read_bytes())
            if adapted is None or metadata["programBytes"] != "AAAAAAAAAAAAAAAA":
                continue
            shader_name = Path(render_pass["pixelShader"])
            adapted["pixelShader"] = str(shader_name.with_name(prefix + shader_name.name))
            technique["name"] = prefix + name
            technique["passes"] = [adapted]
            techniques[name] = technique
            metadata["name"] = adapted["pixelShader"]
            shaders[path.with_name(prefix + path.name)] = fragment, metadata

        # Require reveal support for all spotlight routes, including fog and shadows.
        spot_passes = techset["techniques"][15:19]
        if len(spot_passes) != 4 or any(name not in techniques for name in spot_passes):
            raise ValueError(f"No compatible reveal fragment for all spotlight passes of {stock_name}")
        for path, (fragment, metadata) in shaders.items():
            json_out(self.raw / path.with_suffix(".cg.json"), metadata)
            (self.raw / path).write_bytes(fragment)
        for technique in techniques.values():
            json_out(self.raw / f'techniques/{technique["name"]}.tech.json', technique)
        techset["techniques"] = [prefix + name if name in techniques else name for name in techset["techniques"]]
        techset["name"] = prefix + techset["name"]
        json_out(self.raw / f'techsets/{techset["name"]}.techset.json', techset)
        return techset["name"]

    def bind_reveal(self, material, result):
        texture = next((entry for entry in material.textures if entry.hash == reveal.REVEAL_MAP_HASH), None)
        params = material.constants.get(reveal.REVEAL_PARAMS_HASH)
        image = self.source.image_for_texture(texture)
        if image is None or texture.sampler != 0x0a or texture.semantic != 2 or params is None:
            raise ValueError(f"Unrecognized source reveal inputs for {material.name}")
        image_name, *_ = self.source.image_record(image)
        if not image_name or image_name.startswith(","):
            raise ValueError(f"Invalid source reveal image name for {material.name}")

        self.images.setdefault((self.source, image), image_name)
        result["techniqueSet"] = self.reveal_techset(result["techniqueSet"])
        result["textures"].insert(0, {"name": "revealMap", "image": image_name, "semantic": "colorMap",
                                      "samplerState": {"filter": "linear", "mipMap": "nearest",
                                                       "clampU": False, "clampV": False, "clampW": False}})
        result["constants"].append({"name": "revealParams", "literal": params})

    def emit_material(self, mat, decision, role=None, *, name=None, sort_key=None):
        source, mat, _ = self.source.imported_materials.get(mat.name, (self.source, mat, None))
        original = name or asset_name(mat.name)
        if original in self.materials:
            return
        stock_name = decision["stockImport"]
        if stock_name and (self.iw4 / "materials" / (stock_name + ".json")).is_file():
            material = self.stock_material(stock_name)
            for texture in material["textures"]:
                self.stock_image(texture["image"])
            json_out(self.raw / "materials" / (original + ".json"), material)
            self.materials.add(original)
            return
        if role is None:
            role = material_role(mat)
        unlit = decision["unlit"] or (UNLIT_TEMPLATES.get(mat.technique_set) if source is self.source else None)
        translucent = decision["translucentTemplate"]
        terrain_technique = decision.get("terrainTechnique")
        template = unlit or translucent or decision["litTemplate"] or decision["revealTemplate"] or STOCK_MATERIALS[role]
        effect = role in ("effect", "effect_add")
        native = None
        if (decision["nativeTechniques"] and not (unlit or translucent or decision["litTemplate"]
                or decision["revealTemplate"] or terrain_technique or sort_key is not None)):
            native = self.native_surface(mat, decision["nativeTechniques"])
            template = native or template
        if effect:
            template = decision["effect"] or template
        stock_alias = mat.name.startswith(",") and (self.iw4 / "materials" / (original + ".json")).is_file()
        if stock_alias:
            template = original
        if terrain_technique:
            template = self.terrain_prototype(terrain_technique, mat.camera, mat.sort)
        prototype = self.stock_material(template)
        if terrain_technique:
            prototype = self.terrain_material_inputs(prototype, mat)
        if decision["fragmentFeature"] and not decision["revealTemplate"]:
            prototype, inputs = self.surface_fragment(mat.technique_set, decision["fragmentFeature"], template)
            prototype = prototype.copy()
            maps = [key for key in inputs if key in ("dustMap", "revealMap")]
            prototype["textures"] = [*prototype["textures"],
                *({"name": key, "semantic": "colorMap", "image": "$black"} for key in maps)]
            names = {row.get("name") for row in prototype["constants"]}
            prototype["constants"] = [*prototype["constants"],
                *({"name": key, "literal": mat.constants[property_hash(key)]}
                  for key in inputs if key not in maps and key not in names)]
        result = prototype.copy()
        variant = decision["effectVariant"]
        if variant:
            result["techniqueSet"] = self.effect_techset(result["techniqueSet"], variant)
        if template == "cloud_dust_mote" or variant == "particle_cloud_spark_replace":
            # These two cloud materials share the same Ghosts draw state.
            result["stateBits"] = [
                {**state, "srcBlendRgb": "srcalpha", "dstBlendRgb": "invsrcalpha"}
                for state in prototype["stateBits"]
            ]
        if translucent:
            state = self.stock_material(decision["stateTemplate"])
            result["stateBits"] = state["stateBits"]
            result["stateFlags"] = state["stateFlags"]
            result["_native"] = {**prototype["_native"], "stateBitsResidual": state["_native"]["stateBitsResidual"]}
            flags = source.zone[mat.root + 4]
            result["gameFlags"] = [format(bit, "x") for bit in (1, 2, 4, 16) if flags & bit]
        if not (effect and stock_alias):
            result["textureAtlas"] = {"rows": mat.atlas[0] or 1, "columns": mat.atlas[1] or 1}
        result["surfaceTypeBits"] = decision["surfaceBits"]
        if sort_key is not None:
            result["sortKey"] = sort_key
        elif decision["sortKey"] is not None:
            result["sortKey"] = decision["sortKey"]
        elif unlit or translucent or native or (effect and not mat.name.startswith(",")):
            result["sortKey"] = mat.sort
        if template == UNLIT_CUTOUT or (decision["litTemplate"] and role == "cutout"):
            # Preserve the two-sided cutout when substituting a lit template.
            result["stateBits"] = [
                {**state, "cullFace": "none"} if state["alphaTest"] == "ge128" else state
                for state in prototype["stateBits"]
            ]
        result["constants"] = bind_constants(mat, prototype["constants"])
        if decision["windSource"]:
            result["techniqueSet"], required, omit_depth, animated_caster = self.wind_techset(
                decision["windSource"], result["techniqueSet"], mat.technique_set)
            if omit_depth:
                result["stateBitsEntry"] = [-1, *result["stateBitsEntry"][1:]]
            if animated_caster and source.zone[mat.root + 4] & 0x80:
                # The shared IW4 static-model caster (0x40) bypasses the
                # material's wind shader. Route these shadows through slot 2.
                result["gameFlags"] = [f for f in result["gameFlags"] if f not in ("40", "80")] + ["80"]
            names = {row.get("name") for row in result["constants"]}
            result["constants"].extend(
                {"name": key, "literal": mat.constants[property_hash(key)]}
                for key in required if key not in names)
        result["textures"] = installed = []
        for base in prototype["textures"]:
            entry = base.copy()
            slot = entry["name"] if "name" in entry else entry["nameHash"]
            texture = source.texture_for_material(mat, slot)
            if texture is not None:
                entry["samplerState"] = sampler_state(texture.sampler)
            image = self.bind_image(source, source.image_for_material(mat, slot))
            if image is not None:
                entry["image"] = image
            else:
                if native and texture is None and slot in ("normalMap", "specularMap"):
                    entry["image"] = "$identitynormalmap" if slot == "normalMap" else "$black"
                if slot == "colorMap":
                    self.fallbacks.add(original)
                entry["image"] = self.stock_image(entry["image"])
            installed.append(entry)
        if decision["revealTemplate"]:
            self.bind_reveal(mat, result)
        if mat.name == "wc/sky_swamp":
            sky.bind(source.zone, self.iw4, self.raw, result, property_hash("fogHeight"), json_out)
        result["textures"].sort(key=slot_hash)
        result["constants"].sort(key=slot_hash)
        json_out(self.raw / "materials" / (original + ".json"), result)
        self.materials.add(original)


@cache
def package_file(roots, number):
    for root in roots:
        path = root / f"imagefile{number}.pak"
        if path.is_file():
            return path
    raise FileNotFoundError(f"Unable to locate IW6 imagefile{number}.pak")


def read_part(file, start, end, byte_count):
    file.seek(start)
    payload = file.read(end - start)
    if len(payload) != end - start:
        raise ValueError(f"Incomplete package span at {start:#x} in {file.name}")
    offset = 0
    output = bytearray()
    while len(output) < byte_count and offset + 2 <= len(payload):
        block_bytes = unpack_from(">H", payload, offset)[0]
        offset += 2
        if block_bytes == 1:
            break
        size = block_bytes or 0x10000
        if offset + size > len(payload):
            raise ValueError(f"Incomplete package block at {start + offset:#x} in {file.name}")
        block = payload[offset:offset + size]
        output.extend(decompress(block, -15) if block_bytes else block)
        offset += size
    if len(output) < byte_count:
        raise ValueError(f"Package span at {start:#x} decoded only {len(output)} of {byte_count} bytes")
    return output[:byte_count]


def emit_images(target, roots):
    opened = {}
    with ExitStack() as stack:
        for (source, pos), name in target.images.items():
            image_packages = (source.package_directory, *roots) if source.package_directory else roots
            path = target.raw / "images" / name.replace("*", "_")
            path.parent.mkdir(parents=True, exist_ok=True)
            _, pixel_start, resident_bytes, _ = source.image_record(pos)
            root = source.zone[pos:pos + IMAGE_ROOT_SIZE]
            fmt, levels, dimensions, cube = root[:4]
            width, height, depth = unpack_from(">HHH", root, 8)
            base_width, base_height, base_depth = unpack_from(">HHH", root, 0x20)
            parts, entries = source.streams.get(pos, (((0, 0, 0, 0),) * 4, None))
            data = []
            for i, (w, h, record, byte_count) in enumerate(parts):
                data.append({"width": w, "height": h, "levelSizeAndOffset": record})
                if not byte_count:
                    continue
                number, start, end = entries[i]
                package = package_file(image_packages, number)
                if package not in opened:
                    opened[package] = stack.enter_context(package.open("rb"))
                part = read_part(opened[package], start, end, byte_count)
                path.with_name(f"{path.name}.stream{i}.bin").write_bytes(part)
            path.with_name(path.name + ".pixels.bin").write_bytes(
                source.zone[pixel_start:pixel_start + resident_bytes])
            json_out(path.with_name(path.name + ".image.json"), {
                "format": "iw4-image-source", "version": 2, "name": name,
                "imageFormat": fmt, "levelCount": levels, "dimensionCount": dimensions,
                "multiFaceControl": cube, "textureControl1": u32(root, 4),
                "width": width, "height": height, "depth": depth, "memoryLocation": root[14],
                "minLodControl": root[15], "renderTargetPitch": u32(root, 0x10),
                "pixelsOffset": 0, "mapType": root[0x18], "semantic": root[0x19], "category": root[0x1a],
                "useSrgbReads": root[0x1b] & 1, "cardMemory": u32(root, 0x1c),
                "baseWidth": base_width, "baseHeight": base_height, "baseDepth": base_depth,
                "baseLevelCount": root[0x26], "cached": root[0x27], "streamData": data,
                "payloadByteCount": resident_bytes,
            })


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fastfile", type=Path)
    parser.add_argument("zone", type=Path)
    parser.add_argument("image_packages", type=Path)
    parser.add_argument("raw", type=Path)
    parser.add_argument("material_map", type=Path)
    parser.add_argument("--base-image-packages", type=Path, action="append", default=[])
    parser.add_argument("--iw4-library", type=Path, required=True)
    parser.add_argument("--world-layer-map", type=Path, help="write native terrain vertex layouts for BSP assembly")
    parser.add_argument("--model-materials", type=Path,
                        help="XModel material-handle inventory from export_models.py")
    parser.add_argument("--model-material-map", type=Path,
                        help="write IW4 named bindings by model material handle or inline root")
    parser.add_argument("--shared-zone", type=Path, nargs=2, action="append", default=[],
                        metavar=("FASTFILE", "ZONE"), help="supply a decoded common multiplayer package")
    parser.add_argument("--effect-material-map", type=Path,
                        help="export IW6 effect materials and write name bindings by source offset")
    args = parser.parse_args()
    packages = [fastfile.stem for fastfile, _ in args.shared_zone]
    if set(packages) - SHARED_MATERIALS.keys() or len(packages) != len(set(packages)):
        parser.error("--shared-zone accepts each of common_mp and common_core_mp once")
    if args.effect_material_map and set(packages) != SHARED_MATERIALS.keys():
        parser.error("--effect-material-map requires both shared zones")
    if args.model_materials and "common_core_mp" not in packages:
        parser.error("--model-materials requires the common_core_mp shared zone")

    zone = args.zone.read_bytes()
    table = stream_table(args.fastfile.read_bytes(), zone)
    source = Source(zone, table)
    load_shared_materials(source, args.shared_zone)
    target = Target(source, args.raw, args.iw4_library)
    materials = source.world_materials()
    inline = {}
    aliases = {}
    model_slots = []
    if args.model_materials:
        if not args.model_material_map:
            parser.error("--model-materials requires --model-material-map")
        inventory = json.loads(args.model_materials.read_bytes())
        inline, aliases, model_slots = model_materials(source, inventory)
    effects = {}
    if args.effect_material_map:
        source.bind_effect_images()
        effects = {mat.root: mat for mat in source.named_materials(
            EFFECT_MATERIAL_START, EFFECT_MATERIAL_END, EFFECT_MATERIAL_PREFIXES
        )}
    by_root = {mat.root: mat for mat in (*materials.values(), *inline.values(), *aliases.values(), *effects.values())}
    decisions, bindings = material_rules(source, by_root, effects, model_slots)
    by_name = {mat.name: mat for mat in inline.values()}
    # A material can own images even when its prop is absent from the emitted models.
    for name in {item["imageOwner"] for item in decisions.values()} - by_name.keys() - {None}:
        marker = name.encode("ascii") + b"\0"
        for match in re.finditer(re.escape(marker), zone):
            root = match.start() - MATERIAL_ROOT_SIZE
            if root >= 0 and u32(zone, root) == OWNED:
                by_name[name] = source.material(root)
                break
    for root, decision in decisions.items():
        owner = by_name.get(decision["imageOwner"])
        if owner:
            mat = by_root[root]
            for tex in mat.textures:
                donor = next((entry for entry in owner.textures
                              if (entry.hash, entry.semantic) == (tex.hash, tex.semantic)), None)
                if tex.pointer and donor and source.image_for_texture(tex) is None and source.image_for_texture(donor) is not None:
                    source.image_aliases[tex.pointer] = donor
    if args.model_materials:
        for binding in bindings:
            aliases.setdefault(binding["handle"], by_root[binding["root"]])
        aliases = aliases | materials
        json_out(args.model_material_map, {
            "aliases": {f"{pointer:#x}": asset_name(mat.name) for pointer, mat in aliases.items()},
            "inlineModels": {f"{pos:#x}": asset_name(mat.name) for pos, mat in inline.items()},
        })
        print(f"Exported {len(inline)} inline model material roots and {len(aliases) - len(materials)} model aliases")
    for material in (*materials.values(), *inline.values(), *aliases.values()):
        target.emit_material(material, decisions[material.root])
    if args.effect_material_map:
        for mat in effects.values():
            role = "effect_add" if "add" in mat.name.split("_") else "effect"
            target.emit_material(mat, decisions[mat.root], role)
        json_out(args.effect_material_map, {
            "aliases": {},
            "physical": {f"{pos:#x}": asset_name(mat.name) for pos, mat in effects.items()},
            "materialEnds": {f"{pos:#x}": f"{mat.end:#x}" for pos, mat in effects.items()},
        })
        print(f"Exported {len(effects)} inline effect material roots")
    if args.world_layer_map:
        terrain = {}
        for pointer, mat in materials.items():
            technique = decisions[mat.root]["terrainTechnique"]
            if technique:
                terrain[f"{pointer:#x}"] = {"layers": len(mat.layers), "stride": target.terrain_stride(technique)}
        json_out(args.world_layer_map, terrain)
    emit_images(target, (args.image_packages, *args.base_image_packages))
    json_out(args.material_map, {f"{pointer:#x}": asset_name(mat.name)
                                 for pointer, mat in sorted(materials.items())})
    print(f"Exported {len(materials)} world material references and "
          f"{len(target.images)} image descriptors")
    print(f"Used IW4 color fallback for {len(target.fallbacks)} materials")


if __name__ == "__main__":
    main()
