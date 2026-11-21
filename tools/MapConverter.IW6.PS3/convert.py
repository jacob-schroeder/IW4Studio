"""Build the retail Ghosts PS3 mp_swamp map for the IW4 PS3 custom-map loader."""

import argparse
import json
from pathlib import Path
import re
from shutil import copyfile
import struct
import subprocess
import sys
import tempfile
import zlib

from audio import RESIDENT_SOUND_ALIASES, RUNTIME_SOUND_ALIASES, ambient_loops, stage_axe_sounds, stage_stock_sounds
from build_bsp import assemble
from fastfiles import inflate, resident_loadscreen
from Materials.export_materials import Target


HERE = Path(__file__).resolve().parent
MAP = "mp_swamp"


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True)


def export(script, *args):
    run(sys.executable, "-B", HERE / script, *args)


def raw_text(zone, name):
    tag = name.encode("ascii") + b"\0"
    pos = zone.index(tag)
    owner, size, plain, buf = struct.unpack_from(">4I", zone, pos - 16)
    if (owner, buf) != (0xffffffff, 0xffffffff):
        raise ValueError(f"Invalid source RawFile {name}")
    start = pos + len(tag)
    data = zlib.decompress(zone[start:start+size])
    if len(data) != plain:
        raise ValueError(f"Invalid decompressed length on {name}")
    return data.decode("ascii").replace("\r\n", "\n")


def stage_vision(zone, raw, stock):
    vision_name = f"vision/{MAP}.vision"
    keys = set()
    for template in ("mp_rust", "mp_terminal"):
        keys.update(re.findall(r'(?m)^\s*(r_\w+)\s+"', (stock / f"vision/{template}.vision").read_text()))
    vision = [(k, v) for k, v in re.findall(r'(?m)^\s*(r_\w+)\s+"([^"]+)"', raw_text(zone, vision_name))
              if k in keys]
    (raw / "vision").mkdir(exist_ok=True)
    (raw / vision_name).write_text("".join(f'{k} "{v}"\n' for k, v in vision))


GAMEPLAY_MODELS = ("com_bomb_objective", "com_bomb_objective_d", "mil_tntbomb_mp")


def stage_stock_material(target, name):
    target.copy_source(f"materials/{name}.json")
    for texture in target.stock_material(name)["textures"]:
        target.stock_image(texture["image"])


def gameplay_models(raw, stock):
    # Supply stock IW4 bomb-site and pickup visuals. The faction bundle supplies the care package.
    target = Target(None, raw, stock)
    for model_name in GAMEPLAY_MODELS:
        model = json.loads(target.copy_source(f"xmodel_native/{model_name}.json").read_bytes())
        for lod in model["lods"]:
            if lod["modelSurfs"]:
                target.copy_source(f"xmodelsurfs_native/{lod['modelSurfs']}.json")
        for material in set(model["materials"]):
            stage_stock_material(target, material)


def stage_breach_effect(raw, stock):
    target = Target(None, raw, stock)
    effects = ["explosions/grenadeexp_concrete_1"]
    for name in effects:
        path = target.copy_source(f"fx/{name}.json")
        for row in json.loads(path.read_bytes())["elemDefs"]:
            for mark in row["markVisualArray"]:
                for slot in ("material0", "material1"):
                    if mark[slot]:
                        stage_stock_material(target, mark[slot]["name"])
            for arm in row["visualArray"] or [row["visuals"]]:
                visual = arm and arm["visual"]
                if not visual:
                    continue
                if visual["kind"] == "material":
                    stage_stock_material(target, visual["name"])
                elif visual["kind"] == "effect":
                    child = visual["effectDef"]["name"]
                    if child not in effects:
                        effects.append(child)
    return effects


def prepare(args, work):
    source = args.fastfile
    inputs = work / "source"
    inputs.mkdir()
    zone = work / f"{MAP}.zone"
    load_zone = work / f"{MAP}_load.zone"
    inflate(source, zone)
    inflate(source.with_name(f"{MAP}_load.ff"), load_zone)
    raw = work / "iw4/raw"
    run("dotnet", "fsi", HERE / "Entities/convert.fsx", "--", zone, work)
    export("World/export_world.py", zone, work / "world")
    export("Collision/export_collision.py", zone, work / "collision")
    placements = work / "world/staticmodels.json"
    export("Models/export_models.py", zone, placements, inputs, "--inventory-only", "--include-scenery")
    shared_zones = []
    for package in ("common_mp", "common_core_mp"):
        shared_fastfile = args.base_image_packages / f"{package}.ff"
        shared_zone = work / f"{package}.zone"
        inflate(shared_fastfile, shared_zone)
        shared_zones.extend(("--shared-zone", shared_fastfile, shared_zone))
    export("Materials/export_materials.py", source, zone, source.parent, raw, inputs / "material-map.json",
           "--iw4-library", args.iw4_library, "--base-image-packages", args.base_image_packages,
           "--world-layer-map", inputs / "world-layer-map.json",
           "--model-materials", inputs / "model-material-requests.json",
           "--model-material-map", inputs / "model-material-map.json",
           "--effect-material-map", inputs / "fx-material-map.json", *shared_zones)
    export("Models/export_models.py", zone, placements, raw, "--materials", inputs / "model-material-map.json",
           "--iw4-library", args.iw4_library, "--include-scenery")
    export("Entities/export_scenery.py", zone, placements, raw, work / "scenery")
    export("Lighting/export_lighting.py", zone, raw, work / "lighting")
    export("Lighting/export_grid.py", zone, work / "lighting")
    export("MapUi/export_ui.py", zone, raw, args.iw4_library, inputs / "map-ui.json", "--factions", "us/russia")
    zones = zone.read_bytes()
    createfx = (raw / f"maps/createfx/{MAP}_fx.gsc").read_text()
    stage_axe_sounds(zones, raw, args.iw4_library)
    sounds = ambient_loops(zones, createfx, raw, args.iw4_library)
    (inputs / "ambient-loops.json").write_text(json.dumps(sounds, indent=2))
    stage_vision(zones, raw, args.iw4_library)
    resident_loadscreen(load_zone.read_bytes(), MAP, raw)
    export("Effects/export_effects.py", zone, raw, inputs / "fx-material-map.json")


def link(args, work, dest):
    inputs = work / "source"
    stock = args.iw4_library
    linker = args.linker
    raw = work / "iw4/raw"
    bsp = work / f"{MAP}.d3dbsp"
    authored = (work / f"iw4/map_source/{MAP}.map").read_text()
    if not authored.startswith("iwmap 4\n"):
        raise ValueError("Invalid converted map source")
    entities = authored.removeprefix("iwmap 4\n") + (raw / "static-models.ents").read_text()
    entities += (work / "scenery/scenery.ents").read_text()
    assemble((work / f"{MAP}.zone").read_bytes(), work / "world", work / "collision",
             json.loads((inputs / "material-map.json").read_text()), entities, bsp, work / "lighting",
             args.fastfile, raw, stock, args.base_image_packages, json.loads((inputs / "world-layer-map.json").read_bytes()))
    gameplay_models(raw, stock)
    weapon_source = raw / "weapon_native/iw6_axe_mp.json"
    weapon_source.parent.mkdir(exist_ok=True)
    copyfile(HERE / "Weapons" / weapon_source.name, weapon_source)
    target = Target(None, raw, stock)
    for name in ("killiconmelee", "hud_icon_commando_knife"):
        stage_stock_material(target, name)
    breach_effects = stage_breach_effect(raw, stock)
    stock_sounds = stage_stock_sounds(raw, stock)
    loadscreen = f"loadscreen_{MAP}_load"
    run(linker, "build-load", f"{MAP}_load", loadscreen, raw, dest / f"{MAP}_load.ff")
    run(linker, "build-map-ui", MAP, loadscreen, raw, inputs / "map-ui.json", dest / f"{MAP}_ui.ff")
    opts = json.loads((work / "lighting/lighting.json").read_text())["linker_args"]
    for mode in ("war", "dom", "sd"):
        opts.extend(("--configstrings-mode", mode))
    for name in (*json.loads((inputs / "ambient-loops.json").read_text()), *stock_sounds, *RUNTIME_SOUND_ALIASES):
        opts.extend(("--sound", name))
    for name in RESIDENT_SOUND_ALIASES:
        opts.extend(("--resident-sound", name))
    for name in json.loads((inputs / "fx-conversion.json").read_text())["effects"] + breach_effects:
        opts.extend(("--fx", name))
    for name in (*GAMEPLAY_MODELS, f"iw6/{MAP}/swamp_cave_planks", f"iw6/{MAP}/swamp_wood_basement_door",
                 "mp_body_iw6_mmyers", "vh_iw6_mmyers", f"iw6/{MAP}/viewmodel_axe_iw6", f"iw6/{MAP}/weapon_axe_iw6"):
        opts.extend(("--xmodel", name))
    for name in (f"maps/mp/{MAP}.gsc", f"maps/mp/{MAP}_fx.gsc", f"maps/createfx/{MAP}_fx.gsc", f"maps/mp/{MAP}_fog.gsc", f"vision/{MAP}.vision"):
        opts.extend(("--rawfile", f"{name}={raw / name}"))
    run(linker, "build", bsp, f"maps/mp/{MAP}.d3dbsp", dest / f"{MAP}.ff",
        "--asset-library", raw, "--material", f"compass_map_{MAP}", "--static-model-collision-tree",
        "--weapon", "iw6_axe_mp", "--localize", "WEAPON_IW6_FOG_AXE", "Axe",
        "--stock-callsigns", "--compress-rawfiles", "--compact-airborne-precache", *opts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fastfile", type=Path)
    parser.add_argument("--base-image-packages", type=Path, required=True, help="Ghosts retail PS3 USRDIR")
    parser.add_argument("--iw4-library", type=Path, required=True, help="exported retail IW4 PS3 raw asset library")
    parser.add_argument("--output", type=Path, required=True, help="new deployable map folder")
    parser.add_argument("--work", type=Path, help="new workspace to retain decoded source and intermediate assets")
    parser.add_argument("--linker", type=Path, default=HERE.parent / "D3dbspLinker/bin/Debug/net10.0/D3dbspLinker")
    args = parser.parse_args()
    if args.fastfile.name != f"{MAP}.ff":
        parser.error("the supported source profile is retail mp_swamp.ff")
    if args.output.exists() or (args.work is not None and args.work.exists()):
        parser.error("output and retained work directories must be new")
    args.output = args.output.resolve()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="iw6-iw4-") as temp, tempfile.TemporaryDirectory(
        prefix=".iw6-publish-", dir=args.output.parent
    ) as staging:
        work = args.work.resolve() if args.work else Path(temp)
        work.mkdir(parents=True, exist_ok=True)
        prepare(args, work)
        folder = Path(staging) / MAP
        folder.mkdir()
        link(args, work, folder)
        folder.rename(args.output)
    print(f"Published {args.output}")


if __name__ == "__main__":
    main()
