#!/usr/bin/env python3
"""Export the PS3 mp_swamp CreateFX registry as native IW4 effects."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess


REGISTRY = Path("maps/mp/mp_swamp_fx.gsc")
PLACEMENTS = Path("maps/createfx/mp_swamp_fx.gsc")
LOAD = re.compile(r'^[ \t]*level\._effect\[\s*"([^"]+)"\s*\]\s*=\s*loadfx\(\s*"([^"]+)"\s*\);[^\n]*\n?', re.M)
PLACEMENT_BLOCK = re.compile(
    r'^[ \t]*ent\s*=\s*createOneshotEffect\(\s*"([^"]+)"\s*\);'
    r'[^\n]*\n(?:[ \t]*ent\.v\[[^\n]*\n)*', re.M)
VECTOR_LINE = re.compile(r'^(ent\.v\[ "(?:origin|angles)" \] = \()([^()\n]+)\);$', re.M)
CREATEFX_BANNER = "//_createfx generated. Do not touch!!\n"
CREATEFX_BUFFER_SIZE = 0x18000
SOURCE_SHA256 = "101c19b32f44f996a43d4bc277b761c656d30b7b4615c2360fad0a5007d8d6f8"
STRING_POOL = 0x3FFFFE66
INLINE = (0xFFFFFFFF, 0xFFFFFFFE)
SOURCE_ELEMENT_BYTES = 260
IW4_ELEMENT_BYTES = 252
IW4_EFFECT_BYTES = 32
EFFECT_HEADER = struct.Struct(">I6i20xI")

# IW6 alias addresses refer to Material pointer slots, whose owning FxElemDef
# table has 260-byte rows. Store source physical child offsets so the material
# exporter still owns the target asset name.
MATERIAL_SLOTS = {
    0x400D2DE9: 0xA36C26,  # Spirit victim killstreak, row 3 (row 0 alias is 0x400d2add).
    0x40124D2D: 0xACB358,  # Cave steam marker (gfx_test01).
    0x401257B9: 0xACF0F8,  # Mist ground, row 0.
    0x40126265: 0xAD0468,  # Campfire, rows 0, 1, and 3.
    0x40126369: 0xAD0813,
    0x40126571: 0xAD1679,
    0x40137BD9: 0xAE2DC3,  # Water impact visual-array slots 0 and 1.
    0x40137BDD: 0xAE2F99,
    0x40137E49: 0xAE3817,  # Waterfall drip visual, after its impact FX and inline name.
    0x40138931: 0xAE6F0E,  # Frog splash, row 2, reused by the crocodile ripples.
    0x40164965: 0xB34F4A,  # Blinking light, row 2.
    0x40166195: 0xB36C4F,  # Mist glow sphere, row 0.
    0x401961E9: 0xB679E1,  # Falling dust, after its inline name.
    0x401966FD: 0xB690FC,  # Television dust motes, row 0.
    0x40199635: 0xB6CF0B,  # Vehicle fire, row 3.
    0x4019C161: 0xB844C7,  # Black widow thread, row 1, after its inline name.
}
INLINE_FX_NAMES = {
    0x4013794E: 0xAE2B33,
    0x4015956D: 0xB16AB5,
    0x40165799: 0xB35419,
    0x401960FD: 0xB67761,
    0x4019BF69: 0xB6D96D,
    0x401AACB9: 0xB91A74,
    0x401AB341: 0xB966F4,
    0x401AB8D1: 0xB96CB6,
    0x401ACF95: 0xBA98CF,
    0x401AD1DD: 0xBA9B4A,
    0x401AD429: 0xBA9DCA,
    0x401ADB99: 0xBAA56B,
}
# The following offsets resume at the next element's inline velocity table.
# The owl head is the final child of its effect and needs no resume offset.
INLINE_MODELS = {
    0xB18F50: ("crocodile_stat_float", 0xB2B421),
    0xB6E929: ("black_widow_descend", 0xB83B07),
    0xB91DE1: ("owl_fly_wing_mid", None),
    0xB9761C: ("owl_fly_wing_up", 0xB9BDFF),
    0xB9BF1F: ("owl_fly_wing_dwn", 0xBA0C49),
    0xBA0FA9: ("owl_fly_wing_mu", 0xBA4EF8),
    0xBA5018: ("owl_fly_wing_md", 0xBA965B),
    0xBAB0E2: ("owl_sit_body", 0xBAF2A0),
    0xBAF870: ("owl_sit_head", None),
}
MODEL_ALIASES = {
    0x401AADA5: "owl_fly_wing_mid",
    0x401ABDC9: "owl_fly_wing_mu",
    0x401ABECD: "owl_fly_wing_md",
}


class Unsupported(ValueError):
    pass


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def read_text_at(data, offset, end):
    stop = data.find(b"\0", offset, end)
    if stop < 0:
        raise Unsupported(f"unresolved source string at {offset:#x}")
    try:
        return data[offset:stop].decode("ascii"), stop + 1
    except UnicodeDecodeError as error:
        raise Unsupported(f"non-ASCII source string at {offset:#x}") from error


def fx_roots(data, name):
    encoded = name.encode("ascii") + b"\0"
    candidates = set()
    for match in re.finditer(re.escape(encoded), data):
        pos = match.start()
        offset = pos - EFFECT_HEADER.size
        if offset >= 0 and struct.unpack_from(">I", data, offset)[0] in INLINE:
            candidates.add((offset, match.end()))
        if pos < 0x20000:
            key = struct.pack(">I", STRING_POOL + pos)
            for row in re.finditer(re.escape(key), data):
                candidates.add((row.start(), row.start() + EFFECT_HEADER.size))
    roots = []
    for offset, elements in sorted(candidates):
        _, flags, size, life, looping, one_shot, emission, table_ref = EFFECT_HEADER.unpack_from(data, offset)
        count = looping + one_shot + emission
        if (min(flags, life, looping, one_shot, emission) < 0 or count > 128 or
                not EFFECT_HEADER.size <= size <= 1_000_000 or
                table_ref not in (INLINE if count else (0,))):
            continue
        roots.append((offset, elements, (looping, one_shot, emission), flags, life))
    return roots


def vec(data, offset):
    return dict(zip(("x", "y", "z"), struct.unpack_from(">3f", data, offset)))


def value_range(data, offset, code="f"):
    base, amplitude = struct.unpack_from(f">2{code}", data, offset)
    return {"base": base, "amplitude": amplitude}


def vec_range(data, offset):
    return {"base": vec(data, offset), "amplitude": vec(data, offset + 12)}


def velocity(data, offset):
    return {"velocity": vec_range(data, offset), "totalDelta": vec_range(data, offset + 24)}


def visual_state(data, offset):
    *color, delta, rotation, width, height, scale = struct.unpack_from(">4B5f", data, offset)
    return {
        "color": dict(zip(("r", "g", "b", "a"), color)),
        "rotationDelta": delta, "rotationTotal": rotation, "size0": width, "size1": height, "scale": scale,
    }


def sample_rows(data, cursor, pointer, intervals, stride, fields, decode):
    if pointer == 0:
        if intervals != 0:
            raise Unsupported("sample intervals without source payload")
        return [], cursor
    if pointer not in INLINE:
        raise Unsupported(f"aliased sample payload {pointer:#x}")
    samples = []
    half = stride // 2
    for index in range(intervals + 1):
        start = cursor + index * stride
        samples.append({fields[0]: decode(data, start), fields[1]: decode(data, start + half)})
    return samples, cursor + stride * (intervals + 1)


def convert_flags(flags, source_type):
    result = flags & 0x7F00FEFE
    if flags & 0x200000:
        result |= 0x100
    if source_type == 8 and flags & 0x80000000:
        result -= 0x80000000
    return result


def read_element(data, offset, decoded_type, samples, visuals, references):
    visual, visual_array, marks = visuals
    velocity_samples, state_samples = samples
    _, count, velocity_intervals, state_intervals = struct.unpack_from(">4B", data, offset + 180)
    looping_interval, spawn_count = struct.unpack_from(">2i", data, offset + 8)
    atlas = struct.unpack_from(">6Bh", data, offset + 172)
    sort, lighting, item_clip, fade = struct.unpack_from(">4B", data, offset + 252)
    float_ranges = {
        "spawnRange": 16, "fadeInRange": 24, "fadeOutRange": 32,
        "spawnOffsetRadius": 84, "spawnOffsetHeight": 92, "initialRotation": 148, "gravity": 156,
        "reflectionFactor": 164, "emitDist": 232, "emitDistVariance": 240,
    }
    triplets = {"spawnOrigin": 60, "spawnAngles": 100, "angularVelocity": 124}
    impact, death, emitted = references
    (flags,) = struct.unpack_from(">I", data, offset)
    return {
        "flags": convert_flags(flags, data[offset + 180]),
        "spawn": {"loopingIntervalMsec": looping_interval, "count": spawn_count},
        **{name: value_range(data, offset + field) for name, field in float_ranges.items()},
        **{name: [value_range(data, offset + field + i * 8) for i in range(3)]
           for name, field in triplets.items()},
        "spawnFrustumCullRadius": struct.unpack_from(">f", data, offset + 40)[0],
        "spawnDelayMsec": value_range(data, offset + 44, "i"),
        "lifeSpanMsec": value_range(data, offset + 52, "i"),
        "atlas": dict(zip(("behavior", "index", "fps", "loopCount", "colIndexBits",
                           "rowIndexBits", "entryCount"), atlas)),
        "elemType": decoded_type,
        "visualCount": count,
        "velIntervalCount": velocity_intervals,
        "visStateIntervalCount": state_intervals,
        "velSamples": velocity_samples,
        "visSamples": state_samples,
        "visuals": visual,
        "visualArray": visual_array,
        "markVisualArray": marks,
        "collBounds": {"midPoint": vec(data, offset + 196), "halfSize": vec(data, offset + 208)},
        "effectOnImpact": {"name": impact},
        "effectOnDeath": {"name": death},
        "effectEmitted": {"name": emitted},
        "extended": None,
        "sortOrder": sort,
        "lightingFrac": lighting,
        "useItemClip": item_clip,
        "fadeInfo": fade,
    }


def material_visual(pointer, cursor, overlays):
    if pointer in INLINE:
        name = overlays["physical"].get(cursor)
        if cursor not in overlays["materialEnds"]:
            raise Unsupported(f"unmapped inline Material at {cursor:#x}")
        cursor = overlays["materialEnds"][cursor]
    else:
        name = overlays["aliases"].get(pointer)
    return name, cursor


def fx_reference(data, pointer, cursor):
    if pointer == 0:
        return None, cursor
    if pointer in INLINE:
        return read_text_at(data, cursor, cursor + 256)
    inline_name = INLINE_FX_NAMES.get(pointer)
    if inline_name is not None:
        name, _ = read_text_at(data, inline_name, inline_name + 256)
        return name, cursor
    logical = pointer - STRING_POOL
    if 0xE000 <= logical < 0xF400:
        name, _ = read_text_at(data, logical, logical + 256)
        if name.startswith(("fx/", "vfx/")):
            return name, cursor
    return None, cursor


def sound_reference(data, pointer, cursor):
    if pointer in INLINE:
        return read_text_at(data, cursor, cursor + 256)
    logical = pointer - STRING_POOL
    if 0xE000 <= logical < 0xF400:
        name, _ = read_text_at(data, logical, logical + 256)
        if re.fullmatch(r"[A-Za-z0-9_.-]+", name):
            return name, cursor
    return None, cursor


def model_visual(data, pointer, cursor, last_child):
    if pointer not in INLINE:
        return MODEL_ALIASES.get(pointer), cursor
    if cursor not in INLINE_MODELS:
        raise Unsupported(f"unmapped inline XModel at {cursor:#x}")
    name, end = INLINE_MODELS[cursor]
    encoded = name.encode("ascii") + b"\0"
    if data[cursor + 0x128:cursor + 0x128 + len(encoded)] != encoded:
        raise Unsupported(f"unexpected inline XModel at {cursor:#x}")
    if end is None and not last_child:
        raise Unsupported(f"unmapped XModel tail at {cursor:#x}")
    return name, end if end is not None else cursor


def visual_rows(data, kind, count, pointer, cursor, overlays, last_child):
    if kind in (8, 9) and count == 1 and not pointer:
        return ({"visual": {"kind": "noChild", "reserved": 0}}, [], []), cursor, None
    if kind == 11 or not count:
        if pointer in INLINE:
            raise Unsupported(f"unsupported inline visual type {kind} at {cursor:#x}")
        return (None, [], []), cursor, f"unavailable visual type {kind}"
    if count > 1:
        if pointer not in INLINE:
            raise Unsupported(f"visual array alias {pointer:#x}")
        pointers = struct.unpack_from(f">{count}I", data, cursor)
        cursor += 4 * count
    else:
        pointers = (pointer,)

    visuals = []
    missing = []
    for index, source in enumerate(pointers):
        if kind == 12:
            name, cursor = fx_reference(data, source, cursor)
            visual = {"kind": "effect", "effectDef": {"name": name}}
        elif kind == 7:
            name, cursor = model_visual(data, source, cursor, last_child and index == count - 1)
            name = overlays["models"].get(name)
            visual = {"kind": "model", "name": name}
        elif kind == 10:
            name, cursor = sound_reference(data, source, cursor)
            visual = {"kind": "sound", "name": name}
        else:
            name, cursor = material_visual(source, cursor, overlays)
            visual = {"kind": "material", "name": name}
        if not name:
            missing.append(f"{source:#x}")
        visuals.append({"visual": visual})
    if missing:
        return (None, [], []), cursor, f"unmapped visual reference(s) {', '.join(missing)}"
    return (visuals[0], [], []) if count == 1 else (None, visuals, []), cursor, None


def decode_fx(data, name, overlays):
    roots = fx_roots(data, name)
    if len(roots) != 1:
        raise Unsupported(f"found {len(roots)} source FxEffectDef roots")
    root, elements, original_counts, flags, life = roots[0]
    cursor = elements + sum(original_counts) * SOURCE_ELEMENT_BYTES
    result = []
    dropped = {}
    lost_references = {}
    counts = [0, 0, 0]
    index = 0
    for group, count in enumerate(original_counts):
        for _ in range(count):
            offset = elements + index * SOURCE_ELEMENT_BYTES
            kind, visual_count, vel_count, vis_count, vel, vis, visual = struct.unpack_from(">4B3I", data, offset + 180)
            source_refs = struct.unpack_from(">3I", data, offset + 220)
            (extended,) = struct.unpack_from(">I", data, offset + 248)
            try:
                if not (kind <= 3 or 6 <= kind <= 14):
                    raise Unsupported(f"unsupported IW6 element type {kind}")
                if extended:
                    raise Unsupported(f"unsupported extended element at {offset:#x}")
                velocities, cursor = sample_rows(data, cursor, vel, vel_count, 96, ("local", "world"), velocity)
                states, cursor = sample_rows(data, cursor, vis, vis_count, 48, ("base", "amplitude"), visual_state)
                target_type = kind if kind <= 3 else kind - 2
                last_child = index + 1 == sum(original_counts) and not any(ref in INLINE for ref in source_refs)
                visuals, cursor, missing = visual_rows(
                    data, target_type, visual_count, visual, cursor, overlays, last_child)
                references = []
                for ptr in source_refs:
                    ref, cursor = fx_reference(data, ptr, cursor)
                    references.append(ref)
                if missing:
                    dropped[str(index)] = missing
                else:
                    unresolved = [f"{ptr:#x}" for ptr, ref in zip(source_refs, references) if ptr and ref is None]
                    if unresolved:
                        lost_references[str(index)] = unresolved
                    row = read_element(data, offset, target_type, (velocities, states), visuals, references)
                    result.append(row)
                    counts[group] += 1
            except Unsupported as failure:
                dropped.update((str(i), str(failure)) for i in range(index, sum(original_counts)))
                return root, make_fx(name, result, counts, flags, life), dropped, lost_references
            index += 1
    return root, make_fx(name, result, counts, flags, life), dropped, lost_references


def make_fx(name, elements, counts, flags, life):
    local_size = IW4_EFFECT_BYTES + len(name.encode("ascii")) + 1
    for row in elements:
        local_size += IW4_ELEMENT_BYTES + 96 * len(row["velSamples"]) + 48 * len(row["visSamples"])
        local_size += 4 * len(row["visualArray"]) + 8 * len(row["markVisualArray"])
    return {
        "format": "iw4-fx-native-graph", "version": 1, "name": name,
        "flags": flags & 1, "totalSize": local_size, "msecLoopingLife": life if counts[0] else 0,
        "elemDefCountLooping": counts[0], "elemDefCountOneShot": counts[1], "elemDefCountEmission": counts[2],
        "elemDefs": elements,
    }


def effect_references(asset):
    for row in asset["elemDefs"]:
        for key in ("effectOnImpact", "effectOnDeath", "effectEmitted"):
            if row[key]["name"]:
                yield row[key]["name"]
        if row["elemType"] == 12:
            for arm in row["visualArray"] or ([row["visuals"]] if row["visuals"] else []):
                yield arm["visual"]["effectDef"]["name"]


def run_effect_rules(definitions, inventory, replaced):
    output = subprocess.run(["dotnet", "fsi", "--exec", Path(__file__).with_name("EffectRules.fsx")],
                            input=json.dumps(inventory), stdout=subprocess.PIPE, text=True, check=True)
    changes = json.loads(output.stdout)
    for effect in changes:
        name = effect["name"]
        if name not in replaced and name in definitions:
            raise ValueError(f"Generated effect would replace the source definition {name}")
        definitions[name] = make_fx(name, effect["elements"], effect["counts"], effect["flags"], effect["life"])
    return bool(changes)


def remove_unused_effects(definitions, requested, names):
    for name in names - set(requested):
        if not any(name in effect_references(asset) for other, asset in definitions.items() if other != name):
            del definitions[name]


def apply_effect_rules(definitions, requested):
    descents = [name for name in definitions if name.rsplit("/", 1)[-1] == "vfx_black_widow_descent"]
    for descent_name in descents:
        thread_name = descent_name + "_thread"
        if thread_name not in definitions:
            continue
        inventory = {"descent": definitions[descent_name], "thread": definitions[thread_name]}
        if run_effect_rules(definitions, inventory, {descent_name}):
            remove_unused_effects(definitions, requested, {thread_name})

    prefix = "vfx/moments/mp_swamp/vfx_owl_"
    stages = {"takeoff": "takeoff_p", "glide": "glide_p", "fly": "fly_p", "shrink": "shrink_p"}
    poses = {"sit": "sit_anim", "flap": "fly_anim", "glideModel": "glide_anim", "shrinkModel": "shrink_anim"}
    names = {role: prefix + suffix for role, suffix in (stages | poses).items()}
    if all(name in definitions for name in names.values()):
        inventory = {role: definitions[name] for role, name in names.items()}
        if run_effect_rules(definitions, {"owl": inventory}, {names[role] for role in stages}):
            remove_unused_effects(definitions, requested, {names[role] for role in poses if role != "sit"})


def original_script(path, cache):
    if not cache.exists():
        cache.write_text(path.read_text(encoding="ascii"), encoding="ascii")
    return cache.read_text(encoding="ascii")


def write_createfx(library, registry, placements, keys):
    # The PS3 client reads each file into a 96 KiB buffer. Its line parser
    # matches statement prefixes literally, including their internal spaces.
    alphabet = "abcdefghijklmnopqrstuvwxyz012345"
    if len(keys) > len(alphabet):
        raise ValueError("PS3 IW4 CreateFX supports at most 32 registered effects")
    aliases = {key: f"~{alphabet[i]}" for i, key in enumerate(keys)}

    def registration(row):
        key = row[1]
        if key not in aliases:
            return ""
        return row[0].replace(f'"{key}"', f'"{aliases[key]}"', 1)

    def placement(row):
        key = row[1]
        if key not in aliases:
            return ""
        block = row[0]
        if key == "vfx_owl_sit_anim":
            # The main-house owl sits 36.67 units above the wooden roof in the
            # retail placement. Use the roof height at its authored X/Y.
            block = block.replace('( -260.148, 1032.92, 484.203 )',
                                  '( -260.148, 1032.92, 447.535 )')
        # Both the constructor argument and the fxid field name this effect.
        return block.replace(f'"{key}"', f'"{aliases[key]}"').replace(" );\n", ");\n", 1)

    registry = LOAD.sub(registration, registry)
    placements = PLACEMENT_BLOCK.sub(placement, placements)
    placements = "\n".join(line.lstrip() for line in placements.splitlines() if line.strip()) + "\n"
    placements = VECTOR_LINE.sub(lambda row: row[1] + row[2].replace(" ", "") + ");", placements)
    placements = CREATEFX_BANNER + placements.removeprefix(CREATEFX_BANNER)
    scripts = {REGISTRY: registry.encode("ascii"), PLACEMENTS: placements.encode("ascii")}
    for path, data in scripts.items():
        if len(data) + 1 > CREATEFX_BUFFER_SIZE:
            raise ValueError(f"{path} needs {len(data) + 1} bytes including NUL, IW4 allows {CREATEFX_BUFFER_SIZE}")
    for path, data in scripts.items():
        (library / path).write_bytes(data)


def export(zone, library, material_map):
    data = zone.read_bytes()
    if hashlib.sha256(data).hexdigest() != SOURCE_SHA256:
        raise ValueError("This reader requires the decoded PS3 mp_swamp zone revision it was recovered from")
    source = material_map.parent
    raw_maps = json.loads(material_map.read_text(encoding="utf-8"))
    overlays = {name: {int(key, 16): value for key, value in rows.items()}
                for name, rows in raw_maps.items()}
    overlays["materialEnds"] = {key: int(value, 16) for key, value in overlays["materialEnds"].items()}
    for alias, material_offset in MATERIAL_SLOTS.items():
        name = overlays["physical"].get(material_offset)
        if name:
            overlays["aliases"].setdefault(alias, name)
    overlays["models"] = {}
    for model, _ in INLINE_MODELS.values():
        name = f"iw6/mp_swamp/{model}"
        if (library / "xmodel_native" / f"{name}.json").is_file():
            overlays["models"][model] = name

    registry_path = library / REGISTRY
    placement_path = library / PLACEMENTS
    registry = original_script(registry_path, source / "fx-registry-original.gsc")
    placements = original_script(placement_path, source / "fx-placements-original.gsc")
    requested = dict(LOAD.findall(registry))
    manifest_path = source / "fx-conversion.json"
    previous_effects = set()
    if manifest_path.exists():
        previous_effects.update(json.loads(manifest_path.read_text(encoding="utf-8"))["effects"])
    definitions = {}
    missing = {}
    dropped = {}
    lost_references = {}
    pending = list(dict.fromkeys(requested.values()))
    for name in pending:
        try:
            _, asset, omitted, unresolved = decode_fx(data, name, overlays)
            if omitted:
                dropped[name] = omitted
            if unresolved:
                lost_references[name] = unresolved
            if omitted and not asset["elemDefs"]:
                raise Unsupported("no source element can be linked")
            definitions[name] = asset
            pending.extend(ref for ref in effect_references(asset) if ref and ref not in pending)
        except Unsupported as failure:
            missing[name] = str(failure)

    # Runner chains require their target definitions. Remove unavailable chains
    # before resolving optional impact, death, and emission references.
    skipped = True
    while skipped:
        skipped = False
        for name, asset in list(definitions.items()):
            runners = [row for row in asset["elemDefs"] if row["elemType"] == 12]
            arms = [entry for row in runners for entry in (row["visualArray"] or [row["visuals"]])]
            if any(arm["visual"]["effectDef"]["name"] not in definitions for arm in arms):
                missing[name] = "runner target is unavailable"
                del definitions[name]
                skipped = True
    for name, asset in definitions.items():
        for row in asset["elemDefs"]:
            for key in ("effectOnImpact", "effectOnDeath", "effectEmitted"):
                ref = row[key]["name"]
                if ref and ref not in definitions:
                    lost_references.setdefault(name, {}).setdefault(key, []).append(ref)
                    row[key]["name"] = None

    apply_effect_rules(definitions, requested.values())

    for name in previous_effects - definitions.keys():
        (library / "fx" / f"{name}.json").unlink(missing_ok=True)

    for name, asset in definitions.items():
        write_json(library / "fx" / f"{name}.json", asset)
    keep = [key for key, name in requested.items() if name in definitions]
    write_createfx(library, registry, placements, keep)
    original_placements = PLACEMENT_BLOCK.findall(placements)
    linked_visuals = [
        arm["visual"] for asset in definitions.values() for row in asset["elemDefs"]
        for arm in row["visualArray"] or ([row["visuals"]] if row["visuals"] else [])
    ]
    write_json(manifest_path, {
        "effects": list(definitions),
        "registered_effects": [name for key, name in requested.items() if key in keep],
        "models": sorted({visual["name"] for visual in linked_visuals if visual["kind"] == "model"}),
        "embedded_sound_aliases": sorted({visual["name"] for visual in linked_visuals if visual["kind"] == "sound"}),
        "kept_placements": sum(key in keep for key in original_placements),
        "source_placements": len(original_placements),
        "missing": missing,
        "dropped_elements": dropped,
        "lost_references": lost_references,
    })


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("asset_library", type=Path)
    parser.add_argument("material_map", type=Path)
    args = parser.parse_args()
    export(args.zone, args.asset_library, args.material_map)
