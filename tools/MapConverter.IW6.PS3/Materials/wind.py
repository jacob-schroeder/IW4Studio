"""Bind retail Ghosts model wind vertex programs to compatible IW4 passes."""

from functools import cache
from hashlib import sha256
import json
from pathlib import Path
import re
from struct import iter_unpack, pack_into, unpack_from


# Owned technique ranges in the supported mp_swamp zone. The third value is
# the packed address of the first inline technique, recovered from aliases.
# The tent's separate technique set references the cloth owners here.
SOURCE_RANGES = {
    0xc1cbe6: (0xc29be3, 0x401b7561),  # ivy
    0xc864c3: (0xc8e2c0, 0x401c02e1),  # grass
    0xc8e2c0: (0xc9ac1a, 0x401c1039),  # moss
    0xc9ac1a: (0xca263e, 0x401c1e99),  # moss_nc
    0xca3359: (0xca9c43, 0x401c34b5),  # cloth
    0xca9c43: (0xcaf64e, 0x401c4715),  # moss_alpha
}
VERTEX_HEADER = b"\0\0\x1b\x5b\0\0\0\x06"
INLINE = 0xffffffff
INSERT = 0xfffffffe
SOURCE_TECHNIQUES = 39
MODEL_STREAMS = {
    (b"POSITION", 2113): (0, 0),
    (b"COLOR", 2116): (1, 3),
    (b"TEXCOORD0", 2121): (2, 8),
    (b"NORMAL", 2115): (3, 2),
    (b"TEXCOORD2", 2123): (4, 10),
}
WIND_PARMS = (b"flagParms", b"flagWave0Parms", b"flagWave1Parms", b"flagWave2Parms")
IW4_VERTEX_CONSTANT_SLOTS = 64  # r_draw_lit cache at context+0x160, next field at +0x360


@cache
def vertex_layout(program):
    _, _, size, count, table, descriptor, upload_size, upload = unpack_from(">8I", program)
    if (not program.startswith(VERTEX_HEADER) or size != len(program) or
            not 32 <= table <= table + count * 48 <= descriptor <= upload or upload + upload_size != size):
        raise ValueError("Invalid Cg vertex program")

    def label(offset):
        return program[offset:program.index(0, offset, upload)] if offset else b""

    streams, outputs, uniforms = set(), set(), {}
    for index in range(count):
        kind, resource, variability, register, name, _, _, semantic, direction, _, referenced, _ = (
            unpack_from(">12I", program, table + index * 48))
        if not referenced:
            continue
        if variability == 0x1005:
            key = label(semantic), resource
            if direction == 0x1001:
                streams.add(key)
            elif direction == 0x1002:
                outputs.add((key, kind, label(name).split(b".")[-1]))
        elif variability == 0x1006 and resource == 0x882 and register < 256:
            uniforms[label(name)] = register
    return streams, outputs, uniforms


def technique_slots(zone, root):
    return unpack_from(f">{SOURCE_TECHNIQUES}I", zone, root + 8)


def owned_techniques(zone, root, end, first_handle):
    position = zone.index(0, root + 8 + SOURCE_TECHNIQUES * 4, end) + 1
    # Packed offsets are one-based. Track the VIRTUAL bytes for names,
    # technique records, arguments and alias cells. Shader payloads use TEMP.
    cursor = first_handle - 1

    def read_name():
        nonlocal position, cursor
        stop = zone.index(0, position, end) + 1
        name = zone[position:stop - 1].decode("ascii")
        cursor += stop - position
        position = stop
        return name

    def read_shader(profile, header_size):
        nonlocal position, cursor
        name, program, size = unpack_from(">3I", zone, position)
        prefix, prefix_size = unpack_from(">2I", zone, position + 12) if profile == 7004 else (0, 0)
        position += header_size
        if name == INLINE:
            read_name()
        if prefix in (INLINE, INSERT):
            position += prefix_size
            if prefix == INSERT:
                cursor = (cursor + 3) & ~3
                cursor += 4
        if program not in (INLINE, INSERT):
            return None
        if (size < 32 or position + size > end or
                unpack_from(">3I", zone, position) != (profile, 6, size)):
            raise ValueError(f"Invalid Ghosts Cg program at {position:#x}")
        if program == INSERT:
            cursor = (cursor + 3) & ~3
            cursor += 4
        result = zone[position:position + size]
        position += size
        return result

    for slot, pointer in enumerate(technique_slots(zone, root)):
        if pointer != INLINE:
            continue
        cursor = (cursor + 3) & ~3
        handle = cursor + 1
        name, _, passes, declaration, vertex, pixel = unpack_from(">IHH3I", zone, position)
        counts = zone[position + 20:position + 23]
        (arguments,) = unpack_from(">I", zone, position + 28)
        if (name != INLINE or passes != 1 or declaration in (INLINE, INSERT) or
                vertex == INSERT or pixel == INSERT or arguments != (INLINE if any(counts) else 0)):
            raise ValueError(f"Unsupported Ghosts wind technique at {position:#x}")
        position += 32
        cursor += 32
        program = read_shader(7003, 16) if vertex == INLINE else vertex
        if pixel == INLINE:
            read_shader(7004, 28)
        argument_size = 8 * sum(counts)
        if argument_size:
            if any(value in (INLINE, INSERT) for _, _, value in
                   iter_unpack(">HHI", zone[position:position + argument_size])):
                raise ValueError(f"Unsupported inline Ghosts wind argument at {position:#x}")
            position += argument_size
            cursor = ((cursor + 3) & ~3) + argument_size
        yield slot, handle, read_name(), program
    if position != end:
        raise ValueError(f"Unexpected end for Ghosts wind technique set at {root:#x}")


@cache
def source_programs(zone, root):
    owners, inline_slots, vertex_fields = {}, {}, {}
    for start, (end, first_handle) in SOURCE_RANGES.items():
        for slot, handle, name, program in owned_techniques(zone, start, end, first_handle):
            if isinstance(program, int):
                program = vertex_fields.get(program)
            owners[handle] = name, program
            inline_slots[start, slot] = handle
            vertex_fields[handle + 12] = program

    programs = {}
    for slot, pointer in enumerate(technique_slots(zone, root)):
        handle = inline_slots.get((root, slot)) if pointer == INLINE else pointer
        if handle not in owners:
            continue
        name, program = owners[handle]
        if program is None:
            continue
        name = re.sub(r"_flagext(?:wv[0-2])+|_reactivemotion", "", name)
        if name.startswith("vcsh"):
            name = name.removesuffix("_atest")
        programs[name] = program
    return programs


def without_reactive_displacement(program):
    # IW4 has no reactive centers. In the retail grass programs the only
    # use is MAD(weight, centers[index], windPosition). With zero centers
    # this is MOV(windPosition), with the same destination and write mask.
    _, _, uniforms = vertex_layout(program)
    centers = {b"reactiveMotionCenters[%d]" % i: 64 + i for i in range(16)}
    if {name: slot for name, slot in uniforms.items() if name.startswith(b"reactiveMotionCenters")} != centers:
        return None
    _, _, _, count, table, _, _, upload = unpack_from(">8I", program)
    result = bytearray(program)
    folded = 0
    for offset in range(upload, len(program), 16):
        control, opcode, src, dest = unpack_from(">4I", program, offset)
        register = (opcode >> 12) & 0x3ff
        indexed = dest & 2
        if not indexed and not 64 <= register < 80:
            continue
        first = ((opcode & 0xff) << 9) | (src >> 23)
        second = (src >> 6) & 0x1ffff
        addend = ((src & 0x3f) << 11) | (dest >> 21)
        # A single vector MAD, no paired scalar operation or absolute
        # modifiers, and only the multiplier reads the indexed constant.
        if (not indexed or register != 64 or opcode >> 22 != 4 or control & (7 << 21)
                or first & 3 != 1 or second & 3 != 3 or addend & 3 != 1):
            return None
        mov = (1 << 22) | (addend >> 9)
        # Use the retail Cg MOV encoding for the two unused source operands.
        operands = ((addend & 0x1ff) << 23) | 0x6c083
        write_mask_and_dest = (dest & 0x1fffff) & ~2
        pack_into(">3I", result, offset + 4, mov, operands, 0x60400000 | write_mask_and_dest)
        folded += 1
    if folded != 1:
        return None
    for offset in range(table, table + count * 48, 48):
        name_offset = unpack_from(">I", program, offset + 16)[0]
        if program[name_offset:program.index(0, name_offset, upload)] in centers:
            pack_into(">I", result, offset + 40, 0)  # Cg referenced flag
    return bytes(result)


def adapt_pass(render_pass, stock_program, program, property_hash):
    streams, outputs, uniforms = vertex_layout(program)
    _, stock_outputs, stock_uniforms = vertex_layout(stock_program)
    if not streams <= MODEL_STREAMS.keys() or not stock_outputs <= outputs:
        return None

    # Match dynamically bound uniforms by Cg name. Matrix rows keep the source
    # register layout and the code argument's original update frequency.
    stock_names = {register: name for name, register in stock_uniforms.items() if b"[" not in name}
    remaining = set(uniforms) - {name for name in uniforms if b"[" in name}
    arguments = []
    for row in render_pass["arguments"]:
        if row["type"] == 3:
            name = stock_names.get(row["destination"])
            if name not in remaining:
                return None
            remaining.remove(name)
            row = {**row, "destination": uniforms[name]}
        elif row["type"] in (0, 1):
            return None
        arguments.append(row)
    if remaining - {b"gameTime", *WIND_PARMS}:
        return None
    if b"gameTime" in remaining:
        # Native IW4 PS3 flag passes bind gameTime as a stable code constant.
        arguments.append({"type": 3, "destination": uniforms[b"gameTime"], "argumentRaw": 0x00070001})
    required = [name.decode("ascii") for name in WIND_PARMS if name in remaining]
    for name in required:
        hash_value = property_hash(name)
        raw = hash_value - (1 << 32) if hash_value >= 1 << 31 else hash_value
        arguments.append({"type": 0, "destination": uniforms[name.encode("ascii")], "argumentRaw": raw})

    for row in arguments:
        if row["type"] in (0, 1, 3):
            rows = row["argumentRaw"] & 0xff if row["type"] == 3 else 1
            if row["destination"] + rows > IW4_VERTEX_CONSTANT_SLOTS:
                return None

    declaration = render_pass["vertexDeclaration"]
    stream_count = declaration["streamCount"]
    route = declaration["routing"]
    known_streams = {(row["source"], row["destination"]) for row in route[:stream_count]}
    missing_streams = sorted({MODEL_STREAMS[stream] for stream in streams} - known_streams)
    new_count = stream_count + len(missing_streams)
    if new_count > len(route):
        return None
    route = (route[:stream_count] + [{"source": source, "destination": destination}
                                   for source, destination in missing_streams] + route[new_count:])
    stable = render_pass["perPrimArgCount"] + render_pass["perObjArgCount"]
    arguments[stable:] = sorted(arguments[stable:], key=lambda row: (row["type"], row["argumentRaw"] & 0xffffffff))
    return {**render_pass, "arguments": arguments,
            "stableArgCount": len(arguments) - stable,
            "vertexDeclaration": {**declaration, "streamCount": new_count, "routing": route}}, required


def bind(zone, family, stock_name, iw4, raw, property_hash, write_json, *, source_root):
    programs = source_programs(zone, source_root)
    techset = json.loads((iw4 / f"techsets/{stock_name}.techset.json").read_bytes())
    prefix = f"iw6_wind_{source_root:x}_"
    replacements, constants = {}, set()
    slots = techset["techniques"]
    for name in dict.fromkeys(slots):
        if name is None:
            continue
        source_name = name
        if family in ("grass", "moss", "cloth"):
            source_name = name.replace("_nc", "")
        program = programs.get(source_name)
        if program is None and name in slots[19:23]:
            # IW4's local-light vertex stage is shared between spot and omni.
            # Preserve the omni fragment and validate its own inputs below.
            program = programs.get(source_name.replace("_omni_", "_spot_", 1))
        if program is None and source_name.startswith("lp_spot_"):
            # These IW4 spot passes abbreviate the fragment's specular inputs.
            # Match the Ghosts spot pass, then validate its vertex interface below.
            program = programs.get(source_name.replace("sf0_", "n0s0_", 1))
        if program is not None and family == "grass":
            program = without_reactive_displacement(program)
        if program is None:
            continue
        technique = json.loads((iw4 / f"techniques/{name}.tech.json").read_bytes())
        if technique["version"] != 1 or len(technique["passes"]) != 1:
            continue
        render_pass = technique["passes"][0]
        shader_path = Path("shader_bin_ps3/vertex") / Path(render_pass["vertexShader"]).with_suffix(".cg")
        adapted = adapt_pass(render_pass, (iw4 / shader_path).read_bytes(), program, property_hash)
        if adapted is None:
            continue
        vertex, required = adapted
        constants.update(required)
        replacement = prefix + name
        # Different passes can reference the same complete Cg program.
        shader_name = "iw6_wind_vs_" + sha256(program).hexdigest()
        vertex["vertexShader"] = shader_name + ".hlsl"
        technique["name"] = replacement
        technique["passes"] = [vertex]
        replacements[name] = replacement
        shader_path = shader_path.with_name(shader_name + ".cg")
        write_json(raw / shader_path.with_suffix(".cg.json"), {
            "format": "iw4-ps3-shader", "version": 1, "name": vertex["vertexShader"],
            "kind": 0, "programBytes": "",
        })
        (raw / shader_path).write_bytes(program)
        write_json(raw / f"techniques/{replacement}.tech.json", technique)
    if not replacements:
        return stock_name, (), False, False
    techset["name"] = prefix + stock_name
    animated_caster = techset["techniques"][2] in replacements
    techset["techniques"] = [replacements.get(slot, slot) for slot in techset["techniques"]]
    # A stock depth pass writes the undeformed mesh in front of moving
    # color geometry. Match the absence of that pass in the source set.
    omit_depth = unpack_from(">I", zone, source_root + 8)[0] == 0
    if omit_depth:
        techset["techniques"][0] = None
    write_json(raw / f'techsets/{techset["name"]}.techset.json', techset)
    return techset["name"], tuple(sorted(constants)), omit_depth, animated_caster
