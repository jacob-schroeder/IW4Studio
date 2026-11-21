"""Export the resident Ghosts PS3 compass image and the IW4 map UI metadata."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Materials.export_materials import (  # noqa: E402
    MATERIAL_ROOT_SIZE, Source, Target, emit_images, json_out, sampler_state, u32,
)


def export_ui(zone, raw, iw4, map_name, metadata_path, title, description, factions):
    compass_name = "compass_map_" + map_name
    marker = compass_name.encode("ascii") + b"\0"
    offset = zone.index(marker)
    if zone.find(marker, offset + len(marker)) != -1:
        raise ValueError(f"Ambiguous source compass material {compass_name}")
    source = Source(zone, {})
    material = source.material(offset - MATERIAL_ROOT_SIZE)
    image = source.image_for_material(material, "colorMap")
    if image is None:
        raise ValueError(f"Source compass material {compass_name} owns no image")
    name, _, pixels, _ = source.image_record(image)
    if (name not in (None, compass_name) or not pixels or
            zone[image + 0x19] != 0 or u32(zone, image + 0x2c)):
        raise ValueError(f"Unexpected image descriptor for {compass_name}")

    target = Target(source, raw, iw4)
    target.images[source, image] = compass_name
    emit_images(target, ())
    compass = target.stock_material("compass_map_mp_rust").copy()
    color_map, = compass["textures"]
    sampler = source.texture_for_material(material, "colorMap").sampler
    compass["textures"] = [{**color_map, "image": compass_name, "samplerState": sampler_state(sampler)}]
    json_out(raw / "materials" / (compass_name + ".json"), compass)
    json_out(metadata_path, {"Title": title, "Description": description, "Factions": factions})
    print(f"Exported {compass_name} ({pixels:,} resident PS3 bytes)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zone", type=Path)
    parser.add_argument("raw", type=Path)
    parser.add_argument("iw4_library", type=Path)
    parser.add_argument("metadata", type=Path)
    parser.add_argument("--map", default="mp_swamp")
    parser.add_argument("--title", default="Fog")
    parser.add_argument("--description", default="Fight through a fog-shrouded swamp of cabins and caves.")
    parser.add_argument("--factions", default="tf141/opfor")
    args = parser.parse_args()
    export_ui(args.zone.read_bytes(), args.raw, args.iw4_library, args.map,
              args.metadata, args.title, args.description, args.factions)


if __name__ == "__main__":
    main()
