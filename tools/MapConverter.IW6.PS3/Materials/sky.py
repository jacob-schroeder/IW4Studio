"""Bind the retail PS3 Fog sky fragment to IW4's sky and scene inputs."""

import json
from math import pi
from pathlib import Path
import re
from struct import pack, unpack, unpack_from
from zlib import decompress

if __package__:
    from .reveal import fragment_layout
else:
    from reveal import fragment_layout


SKY_FRAGMENT = 0x104a09c
SKY_TECHNIQUE = "iw6_swamp_sky"
FOG_SCRIPT = b"maps/createart/mp_swamp_fog.gsc\0"
FOG_CONSTANTS = (b"fogColorLinear", b"fogSunConsts", b"fogSunDir", b"fogSunColorLinear")


def float32(value):
    return unpack(">f", pack(">f", value))[0]


def fog_height(zone):
    name = zone.index(FOG_SCRIPT)
    owner, size, plain_size, payload = unpack_from(">4I", zone, name - 16)
    if owner != 0xffffffff or payload != 0xffffffff:
        raise ValueError("Unexpected Ghosts sky fog RawFile header")
    start = name + len(FOG_SCRIPT)
    script = decompress(zone[start:start + size])
    if len(script) != plain_size:
        raise ValueError("Unexpected Ghosts sky fog RawFile length")
    default = script.split(b'create_vision_set_fog( "mp_swamp" );', 1)[1]
    default = default.split(b"create_vision_set_fog", 1)[0]
    fields = dict(re.findall(rb"ent\.(\w+)\s*=\s*([^;]+);", default))

    # Ghosts PS3 writes fogHeight in its constant setup at 0x545f2c–0x545fc0.
    degrees_to_radians = float32(pi / 180)
    minimum = float32(float32(float(fields[b"skyFogMinAngle"])) * degrees_to_radians)
    maximum = float32(float32(float(fields[b"skyFogMaxAngle"])) * degrees_to_radians)
    if maximum <= minimum:
        maximum = float32(maximum + float32(0.01))
    intensity = float32(float(fields[b"skyFogIntensity"]))
    opacity = float32(float(fields[b"maxOpacity"]))
    return [minimum, float32(1 / float32(maximum - minimum)), float32(intensity * opacity), 0.0]


def bind(zone, iw4, raw, material, height_hash, write_json):
    stock_name = material["techniqueSet"]
    techset = json.loads((iw4 / f"techsets/{stock_name}.techset.json").read_bytes())
    technique = json.loads((iw4 / "techniques/sky.tech.json").read_bytes())
    if "sky" not in techset["techniques"] or technique["version"] != 1 or len(technique["passes"]) != 1:
        raise ValueError("Unexpected IW4 sky technique")
    render_pass = technique["passes"][0]
    stock_path = Path("shader_bin_ps3/pixel") / Path(render_pass["pixelShader"]).with_suffix(".cg")
    stock = fragment_layout((iw4 / stock_path).read_bytes())
    size = unpack_from(">I", zone, SKY_FRAGMENT + 8)[0]
    fragment = zone[SKY_FRAGMENT:SKY_FRAGMENT + size]
    source = fragment_layout(fragment)
    expected = (b"fogHeight", *FOG_CONSTANTS)
    if (stock.varyings != source.varyings or stock.controls != source.controls or
            [row[:1] + row[2:] for row in stock.samplers] != [row[:1] + row[2:] for row in source.samplers] or
            tuple(row[0] for row in source.parameters[1:6]) != expected):
        raise ValueError("Incompatible Ghosts sky fragment")

    donor = json.loads((iw4 / "techniques/vertcol_simple_fog_lin_dfog.tech.json").read_bytes())["passes"][0]
    donor_path = stock_path.with_name(Path(donor["pixelShader"]).with_suffix(".cg").name)
    donor_layout = fragment_layout((iw4 / donor_path).read_bytes())
    scene_args = {donor_layout.parameters[row["destination"]][0]: row
                  for row in donor["arguments"] if row["type"] == 5}
    signed_hash = height_hash - (1 << 32) if height_hash >= 1 << 31 else height_hash
    render_pass["arguments"].extend({**scene_args[name], "destination": destination}
                                    for destination, name in enumerate(FOG_CONSTANTS, 2))
    render_pass["arguments"].append({"type": 6, "destination": 1, "argumentRaw": signed_hash, "literalBits": None})
    render_pass["stableArgCount"] += len(expected)
    render_pass["pixelShader"] = SKY_TECHNIQUE + ".hlsl"
    technique["name"] = SKY_TECHNIQUE
    techset["name"] = SKY_TECHNIQUE
    techset["techniques"] = [SKY_TECHNIQUE if slot == "sky" else slot for slot in techset["techniques"]]

    shader_path = stock_path.with_name(SKY_TECHNIQUE + ".cg")
    metadata = json.loads((iw4 / stock_path.with_suffix(".cg.json")).read_bytes())
    metadata["name"] = render_pass["pixelShader"]
    write_json(raw / shader_path.with_suffix(".cg.json"), metadata)
    (raw / shader_path).write_bytes(fragment)
    write_json(raw / f"techniques/{SKY_TECHNIQUE}.tech.json", technique)
    write_json(raw / f"techsets/{SKY_TECHNIQUE}.techset.json", techset)
    material["techniqueSet"] = SKY_TECHNIQUE
    material["constants"].append({"name": "fogHeight", "literal": fog_height(zone)})
