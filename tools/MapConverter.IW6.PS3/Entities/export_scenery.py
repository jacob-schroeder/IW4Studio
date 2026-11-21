"""Retain the source map's visible physics clutter as nonblocking IW4 scenery."""

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct


SOURCE_SHA256 = "101c19b32f44f996a43d4bc277b761c656d30b7b4615c2360fad0a5007d8d6f8"
CLIP_MAP = 0x3CF853D
DYNAMIC_MODELS = 0x4077BA7
DYNAMIC_ROW_SIZE = 100
DYNAMIC_MODEL_SLOT = 0x415E2A65
WORLD_MODEL_SLOT = 0x409FDB79
WORLD_MODEL_ROW_SIZE = 64
INLINE = 0xFFFFFFFF
CLUTTER = 1
SCRIPTABLE = 4
INLINE_MODELS = {
    103: "dub_gametable_playing_card_deck_dyn",
    105: "dub_gametable_playing_cards_js_dyn",
    114: "dub_gametable_playing_cards_kd_dyn",
    116: "sn_cleaning_bottles_03",
    122: "sn_cleaning_bottles_02",
    123: "chair_folding_iw6",
}


def quake_angles(quaternion):
    norm = math.sqrt(sum(value * value for value in quaternion))
    if not math.isclose(norm, 1.0, abs_tol=0.01):
        raise ValueError("Source scenery has an invalid placement quaternion")
    x, y, z, w = (value / norm for value in quaternion)
    fx = 1 - 2 * (y * y + z * z)
    fy = 2 * (x * y + z * w)
    fz = 2 * (x * z - y * w)
    lx = 2 * (x * y - z * w)
    ly = 1 - 2 * (x * x + z * z)
    lz = 2 * (y * z + x * w)
    uz = 1 - 2 * (x * x + y * y)
    horizontal = math.hypot(fx, fy)
    pitch = math.atan2(-fz, horizontal)
    if horizontal > 1e-6:
        yaw = math.atan2(fy, fx)
        roll = math.atan2(lz, uz)
    else:
        yaw = math.atan2(-lx, ly)
        roll = 0.0
    return tuple(math.degrees(angle) for angle in (pitch, yaw, roll))


def scenery_poses(zone, world):
    world_names = {
        WORLD_MODEL_SLOT + row["source_instance_index"] * WORLD_MODEL_ROW_SIZE: row["model_name"]
        for row in world["placements"] if int(row["source_model_ref"], 16) == INLINE
    }
    model_names = world_names | {
        DYNAMIC_MODEL_SLOT + index * DYNAMIC_ROW_SIZE: name
        for index, name in INLINE_MODELS.items()
    }
    count, brush_count = struct.unpack_from(">2H", zone, CLIP_MAP + 0xC4)
    if (count, brush_count) != (125, 0):
        raise ValueError("Unexpected source dynamic entity counts")
    placements = []
    scriptable = 0
    for index in range(count):
        offset = DYNAMIC_MODELS + index * DYNAMIC_ROW_SIZE
        kind, *pose, model, visual_brush, physics_brush, scriptable_index, _, _, preset, hinge, link = (
            struct.unpack_from(">I7fI4H4I", zone, offset)
        )
        if kind == SCRIPTABLE:
            scriptable += 1
            continue
        if kind != CLUTTER or any((visual_brush, physics_brush, scriptable_index, hinge, link)) or not preset:
            raise ValueError(f"Unexpected dynamic scenery type or link at source row {index}")
        origin = pose[4:]
        if not all(math.isfinite(component) for component in origin):
            raise ValueError(f"Source scenery row {index} has a non-finite position")
        if model == INLINE:
            source_name = INLINE_MODELS[index]
        else:
            source_name = model_names[model]
        placements.append((source_name, origin, quake_angles(pose[:4])))
    return placements, scriptable


def visual_model(library, name):
    root = library / "xmodel_native"
    model = json.loads((root / f"{name}.json").read_text(encoding="utf-8"))
    if model["name"] != name:
        raise ValueError(f"Native scenery model name mismatch: {name}")
    if model["collLod"] == 255 or not model["collSurfs"]:
        return name
    # The disk linker includes models with native collision in its static
    # collision tree. A visual variant can share the untouched render payloads.
    variant = name + "_scenery"
    model.update(name=variant, collLod=255, contents=0, collSurfs=[], physPreset=None, physCollmap=None)
    (root / f"{variant}.json").write_text(json.dumps(model, separators=(",", ":")) + "\n", encoding="utf-8")
    return variant


def entity(model, origin, angles):
    origin_text = " ".join(f"{value:.9g}" for value in origin)
    angles_text = " ".join(f"{value:.9g}" for value in angles)
    return (f'{{\n"classname" "misc_model"\n"model" "{model}"\n'
            f'"origin" "{origin_text}"\n"angles" "{angles_text}"\n}}\n')


def export_scenery(zone_path, world_path, library, output):
    zone = zone_path.read_bytes()
    if hashlib.sha256(zone).hexdigest() != SOURCE_SHA256:
        raise ValueError("This reader requires the decoded PS3 mp_swamp zone revision it was recovered from")
    world = json.loads(world_path.read_text(encoding="utf-8"))
    models = json.loads((library / "static-model-export.json").read_text(encoding="utf-8"))["models_by_source_name"]
    placements, scriptable = scenery_poses(zone, world)
    skipped = Counter()
    used = {}
    entities = []
    placed = 0
    for source, origin, angles in placements:
        if source not in models:
            skipped[source] += 1
            continue
        for model in models[source]:
            if model not in used:
                used[model] = visual_model(library, model)
            entities.append(entity(used[model], origin, angles))
        placed += 1
    output.mkdir(parents=True, exist_ok=True)
    (output / "scenery.ents").write_text("".join(entities), encoding="utf-8")
    (output / "scenery-export.json").write_text(json.dumps({
        "source_clutter_placements": len(placements),
        "converted_clutter_placements": placed,
        "render_entities": len(entities),
        "script_controlled_dynamic_entities_excluded": scriptable,
        "omitted_by_source_model": skipped,
        "collision_free_variants": {name: variant for name, variant in used.items() if name != variant},
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("world_placements", type=Path)
    parser.add_argument("asset_library", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    export_scenery(args.zone, args.world_placements, args.asset_library, args.output)
