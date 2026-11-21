"""Bind the Ghosts candle tint, dust and reveal shading to native IW4 passes."""

import json
from pathlib import Path
import re

if __package__:
    from . import reveal
else:
    import reveal


PASS_NAME = re.compile(rb"(?:lp|lm|l|vertcol)_[a-zA-Z0-9_]{3,96}\0")
DUST_INPUTS = {"dustMap": b"dustSampler", "dustParms": b"dustParms",
               "dustEyeParms": b"dustEyeParms", "dustTint": b"dustTint"}


def convert(programs, feature, stock, iw4, raw, prop_hash, write_json):
    material = stock.copy()
    set_name = stock["techniqueSet"]
    native = json.loads((iw4 / f"techsets/{set_name}.techset.json").read_bytes())
    token, native_token, required_slot, inputs = {
        "dust": ("_du_dm_", "_", "dustMap", DUST_INPUTS),
        "tint": ("_ct_", "_", "colorTint", {"colorTint": b"colorTint"}),
        "reveal": ("c0v0", "c0", "revealMap", {"revealMap": b"revealMapSampler", "revealParams": b"revealParams"}),
    }[feature]
    labels = {prop_hash(name): cg_name for name, cg_name in inputs.items()}
    replacements = {}
    used = set()
    for source_name, fragment, rows in programs:
        name = source_name.replace(token, native_token)
        if name == source_name or name not in native["techniques"] or name in replacements:
            continue
        technique = json.loads((iw4 / f"techniques/{name}.tech.json").read_bytes())
        if technique["version"] != 1 or len(technique["passes"]) != 1:
            continue
        donor = technique["passes"][0]
        path = Path("shader_bin_ps3/pixel") / Path(donor["pixelShader"]).with_suffix(".cg")
        extras = {value: labels[value] for kind, _, value in rows if kind in (3, 7) and value in labels}
        required = prop_hash(required_slot)
        if required not in extras:
            continue
        # The IW4 vertex shader keeps its matrices, detail UV scale and fog inputs.
        pixel_rows = [row for row in rows if row[0] not in (0, 1, 4)]
        adapted = reveal.adapt_material_pass(donor, (iw4 / path).read_bytes(), fragment, pixel_rows,
                                            extras, allow_float2=feature == "dust")
        if adapted is None:
            continue
        converted = "iw6_" + source_name
        metadata = json.loads((iw4 / path.with_suffix(".cg.json")).read_bytes())
        if metadata["programBytes"] != "AAAAAAAAAAAAAAAA":
            raise ValueError(f"Unsupported pixel program header on {name}")
        adapted["pixelShader"] = converted + ".hlsl"
        metadata["name"] = adapted["pixelShader"]
        converted_path = path.with_name(converted + ".cg")
        write_json(raw / converted_path.with_suffix(".cg.json"), metadata)
        (raw / converted_path).write_bytes(fragment)
        write_json(raw / f"techniques/{converted}.tech.json",
                   {**technique, "name": converted, "passes": [adapted]})
        replacements[name] = converted
        used.update(extras)
    if not replacements:
        return material, ()
    converted_set = f"iw6_{feature}_{set_name}"
    write_json(raw / f"techsets/{converted_set}.techset.json", {
        **native, "name": converted_set, "techniques": [replacements.get(t, t) for t in native["techniques"]]})
    material["techniqueSet"] = converted_set
    return material, tuple(name for name in inputs if prop_hash(name) in used)
