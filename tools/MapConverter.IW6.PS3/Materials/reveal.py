"""Adapt compatible Ghosts PS3 fragment programs to IW4 pass bindings."""

from collections import Counter
from dataclasses import dataclass
import re
from struct import iter_unpack, unpack_from


REVEAL_MAP_HASH = 0x34D849D5
REVEAL_PARAMS_HASH = 0x8C67DED5
CG_FRAGMENT = b"\0\0\x1b\x5c\0\0\0\x06"
TECHNIQUE_NAME = re.compile(rb"(?:lm|l)_[A-Za-z0-9_]{3,90}\0")


@dataclass(frozen=True)
class FragmentLayout:
    parameters: tuple
    varyings: Counter
    samplers: Counter
    controls: bytes
    patches: tuple
    upload_size: int


def fragment_layout(program):
    if len(program) < 32 or not program.startswith(CG_FRAGMENT):
        raise ValueError("Unsupported PS3 fragment program header")
    _, _, size, count, table, descriptor, upload_size, upload = unpack_from(">8I", program)
    if (size != len(program) or not 32 <= table <= descriptor or
            table + count * 48 > descriptor or descriptor + 22 > upload or
            upload + upload_size != size):
        raise ValueError("Invalid PS3 fragment program layout")

    def name(offset):
        return program[offset:program.index(0, offset, upload)] if offset else b""

    parameters = []
    patch_tables = []
    for row in iter_unpack(">12I", program[table:table + count * 48]):
        kind, resource, variability, register, label, _, patch, semantic, direction, _, referenced, _ = row
        parameters.append((name(label), name(semantic), kind, resource, variability, register, direction, referenced))
        patch_tables.append(patch)

    # RSX resource IDs identify the output color, which the two Cg writers
    # label COLOR and COLOR0. The descriptor's register count may also differ.
    varyings = Counter((p[0], *p[2:]) for p in parameters if p[4] == 0x1005 and not 0x800 <= p[3] < 0x810)
    samplers = Counter(p for p in parameters if 0x800 <= p[3] < 0x810)
    controls = program[descriptor + 4:descriptor + 18] + program[descriptor + 19:descriptor + 22]
    return FragmentLayout(tuple(parameters), varyings, samplers, controls, tuple(patch_tables), upload_size)


def inline_fragment_passes(technique_data, pass_name=TECHNIQUE_NAME):
    for match in re.finditer(re.escape(CG_FRAGMENT), technique_data):
        start = match.start()
        if start + 32 > len(technique_data):
            continue
        size = unpack_from(">I", technique_data, start + 8)[0]
        end = start + size
        if size < 32 or end > len(technique_data):
            continue
        # A Cg fragment below this source technique-set root is followed by
        # its pass's eight-byte argument rows and then the technique XString.
        for count in range(1, 49):
            name_pos = end + count * 8
            name = pass_name.match(technique_data, name_pos)
            if name is None:
                continue
            rows = tuple(iter_unpack(">HHI", technique_data[end:name_pos]))
            if all(0 <= kind <= 8 and dest < 1024 for kind, dest, _ in rows):
                yield name[0][:-1].decode("ascii"), technique_data[start:end], rows
            break


def source_programs(technique_data):
    programs = {}
    for origin, program, rows in inline_fragment_passes(technique_data):
        if "b0c0v0" not in origin:
            continue
        target = origin.replace("b0c0v0", "b0c0", 1)
        if target in programs:
            raise ValueError(f"Ambiguous Ghosts reveal fragment for {target}")
        programs[target] = (program, rows)
    return programs


def pixel_binding(kind, destination, raw, layout):
    if kind in (2, 4):  # Material and code samplers retain their units and bindings.
        return kind, destination, raw
    # Code indices differ between Ghosts and IW4. Keep the IW4 index and
    # match the declared Cg parameter and constant row shape, not its ordinal.
    return kind, layout.parameters[destination], raw & 0xffff if kind == 5 else raw


def adapt_pass(stock_pass, stock_program, source_program, source_rows):
    return adapt_material_pass(stock_pass, stock_program, source_program, source_rows,
                               {REVEAL_MAP_HASH: b"revealMapSampler", REVEAL_PARAMS_HASH: b"revealParams"})


def adapt_material_pass(stock_pass, stock_program, source_program, source_rows, extra_slots, *, allow_float2=False):
    donor = fragment_layout(stock_program)
    source = fragment_layout(source_program)
    controls = bytearray(source.controls)
    if allow_float2:
        # A float2 input consumes only XY with either RSX texcoord2D setting.
        # Require identical control bits for every other varying and component.
        mask = sum(1 << (p[3] - 0xc94) for p in source.parameters
                   if p[2] == 0x416 and 0xc94 <= p[3] < 0xc9e and p[4] == 0x1005)
        source_2d = int.from_bytes(controls[10:12], "big")
        donor_2d = int.from_bytes(donor.controls[10:12], "big")
        controls[10:12] = ((source_2d & ~mask) | (donor_2d & mask)).to_bytes(2, "big")
    if (donor.varyings != source.varyings or donor.controls != controls or
            any(kind not in (3, 4, 5, 6, 7) for kind, _, _ in source_rows)):
        return None

    source_bindings = {}
    extra_arguments = []
    extra_samplers = Counter()
    extra_hashes = set()
    for kind, dest, raw in source_rows:
        if kind == 4:
            continue  # Keep the stock vertex program and its code arguments.
        if kind in (6, 7) and dest >= len(source.parameters):
            return None
        if raw in extra_slots:
            name = extra_slots[raw]
            argument = {"destination": dest, "argumentRaw": raw - (1 << 32) if raw >= (1 << 31) else raw}
            if kind == 3:
                params = Counter({p: count for p, count in source.samplers.items()
                                  if p[0] == name and p[3] == 0x800 + dest})
                if sum(params.values()) != 1 or any(p[3] == 0x800 + dest for p in donor.samplers):
                    return None
                extra_samplers.update(params)
                extra_arguments.append({"type": 2, **argument})
            elif kind == 7 and source.parameters[dest][0] == name and source.patches[dest]:
                patch = source.patches[dest]
                if patch > len(source_program) - 4:
                    return None
                count = unpack_from(">I", source_program, patch)[0]
                if not count or patch + 4 + 4 * count > len(source_program):
                    return None
                offsets = unpack_from(f">{count}I", source_program, patch + 4)
                if any(offset == 0 or offset % 16 or offset + 16 > source.upload_size for offset in offsets):
                    return None
                extra_arguments.append({"type": 6, **argument})
            else:
                return None
            if raw in extra_hashes:
                return None
            extra_hashes.add(raw)
            continue
        key = pixel_binding(kind - 1, dest, raw, source)
        if key in source_bindings:
            return None
        source_bindings[key] = dest

    if (extra_hashes != extra_slots.keys() or donor.samplers - source.samplers or
            source.samplers - donor.samplers != extra_samplers):
        return None
    stable_start = stock_pass["perPrimArgCount"] + stock_pass["perObjArgCount"]
    arguments = []
    for index, arg in enumerate(stock_pass["arguments"]):
        kind, dest, raw = arg["type"], arg["destination"], arg["argumentRaw"]
        if kind in (2, 4, 5, 6, 7):
            if kind in (5, 6, 7) and dest >= len(donor.parameters):
                return None
            key = pixel_binding(kind, dest, raw & 0xffffffff, donor)
            mapped = source_bindings.pop(key, None)
            if mapped is None or (mapped != dest and index < stable_start):
                return None
            arg = {**arg, "destination": mapped}
        arguments.append(arg)
    if source_bindings:
        return None
    # Stable material setters walk the sorted property tables forward, so the
    # extra sampler and constant arguments must retain the native hash order.
    for argument in extra_arguments:
        kind, raw = argument["type"], argument["argumentRaw"] & 0xffffffff
        for index in range(stable_start, len(arguments)):
            next_arg = arguments[index]
            if next_arg["type"] > kind or (next_arg["type"] == kind and
                                           next_arg["argumentRaw"] & 0xffffffff > raw):
                arguments.insert(index, argument)
                break
        else:
            arguments.append(argument)
    return {**stock_pass, "stableArgCount": stock_pass["stableArgCount"] + len(extra_hashes), "arguments": arguments}
