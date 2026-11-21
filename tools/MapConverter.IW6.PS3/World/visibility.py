"""Translate the authored Fog render partition into IW4Studio's BSP spatial lump."""

import struct

from World.export_world import STATIC_MODEL_BOUNDS, WORLD


PLANES = 0x1475AFD
NODE_WORDS = 0x14B6071
CELL_TREE_COUNTS = 0x14B89FB
CELLS = 0x14D3521
CELL_PAYLOADS_END = 0x14D9632
SORTED_SURFACES = 0x2798D73
INLINE = 0xFFFFFFFF

SOURCE_CELL = struct.Struct(">6f2IB3x3I")
SOURCE_PORTAL = struct.Struct(">12x4fIHHB3x6f")
SOURCE_TREE = struct.Struct(">6f4H2I")
POINT = struct.Struct(">3f")
BSP_POINT = struct.Struct("<3f")
BSP_CELL_SIZE = 112
RENDER_SPATIAL = 0x41
PS3_RENDER_SPATIAL = b"IW4PS3S\x01"


def pack_partition(zone, cell_count, model_count):
    plane_count, node_count = struct.unpack_from(">2I", zone, WORLD + 0x0C)
    words = list(struct.unpack_from(f">{node_count}H", zone, NODE_WORDS))
    if NODE_WORDS + 2 * node_count != CELL_TREE_COUNTS:
        raise ValueError("Unexpected IW6 render partition node count")
    plane_ids = {}
    planes = bytearray()
    leaf_count = cell_count + 1  # Word zero identifies a point outside all cells.
    cursor = 0
    while cursor < len(words):
        word = words[cursor]
        if word < leaf_count:
            cursor += 1
            continue
        source_plane = word - leaf_count
        if source_plane >= plane_count or cursor + 1 >= len(words):
            raise ValueError("Invalid IW6 render partition branch")
        if source_plane not in plane_ids:
            plane_ids[source_plane] = len(plane_ids)
            x, y, z, distance, kind = struct.unpack_from(">4fB", zone, PLANES + source_plane * 20)
            sign_bits = sum(1 << i for i, value in enumerate((x, y, z)) if value < 0)
            planes.extend(struct.pack("<4fBBH", x, y, z, distance, kind, sign_bits, 0))
        words[cursor] = leaf_count + plane_ids[source_plane]
        cursor += 2
    return (PS3_RENDER_SPATIAL + struct.pack("<3I", len(plane_ids), node_count, model_count) + planes +
            struct.pack(f"<{node_count}H", *words))


def render_spatial_lumps(zone, world_surface_count, surface_boundaries, probe_indices, model_export):
    (cell_count,) = struct.unpack_from(">I", zone, WORLD + 0x38)
    (model_count,) = struct.unpack_from(">I", zone, WORLD + 0x3A8)
    if (model_export["source_instances"] != model_count or model_export["converted_instances"] != model_count or
            model_export["omitted_instances_by_model"]):
        raise ValueError("Fog visibility requires one converted model placement for each authored placement")
    if SORTED_SURFACES + 2 * world_surface_count != STATIC_MODEL_BOUNDS:
        raise ValueError("Unexpected IW6 world surface count")
    source_order = struct.unpack_from(f">{world_surface_count}H", zone, SORTED_SURFACES)
    if sorted(source_order) != list(range(world_surface_count)):
        raise ValueError("IW6 spatial surface order is not a permutation of world surfaces")

    # Each tree range addresses slots in the permutation. Remove compiler-only
    # surfaces from both the permutation and the endpoints of those ranges.
    surface_order = []
    slot_boundaries = [0]
    for source_index in source_order:
        disk_index = surface_boundaries[source_index]
        if surface_boundaries[source_index + 1] > disk_index:
            surface_order.append(disk_index)
        slot_boundaries.append(len(surface_order))

    payload = bytearray(pack_partition(zone, cell_count, model_count))
    payload.extend(struct.pack(f"<{len(surface_order)}H", *surface_order))
    tree_counts = struct.unpack_from(f">{cell_count}I", zone, CELL_TREE_COUNTS)
    tree_offset = CELL_TREE_COUNTS + cell_count * 8  # Counts followed by the inline tree pointers.
    cell_offset = CELLS + cell_count * SOURCE_CELL.size
    cells = bytearray(cell_count * BSP_CELL_SIZE)

    for cell_index, tree_count in enumerate(tree_counts):
        if not tree_count:
            raise ValueError(f"IW6 render cell {cell_index} has no spatial tree")
        tree_end = tree_offset + tree_count * SOURCE_TREE.size
        trees = list(SOURCE_TREE.iter_unpack(zone[tree_offset:tree_end]))
        root_model_count, root_pointer = trees[0][-3:-1]
        if root_pointer != (INLINE if root_model_count else 0):
            raise ValueError(f"IW6 render cell {cell_index} has an unsupported root model list")
        model_ids = struct.unpack_from(f">{root_model_count}H", zone, tree_end)
        if any(model >= model_count for model in model_ids):
            raise ValueError(f"IW6 render cell {cell_index} references a missing model")
        tree_offset = tree_end + 2 * root_model_count

        *bounds, portal_count, portal_pointer, probe_count, probe_pointer, probe_refs, probe_ref_pointer = (
            SOURCE_CELL.unpack_from(zone, CELLS + cell_index * SOURCE_CELL.size)
        )
        if (portal_pointer != (INLINE if portal_count else 0) or probe_pointer != (INLINE if probe_count else 0) or
                probe_refs or probe_ref_pointer or probe_count > BSP_CELL_SIZE - 45):
            raise ValueError(f"Unexpected IW6 render cell {cell_index} child storage")
        payload.extend(struct.pack("<3I", tree_count, portal_count, root_model_count))
        payload.extend(struct.pack(f"<{root_model_count}H", *model_ids))

        # The donor partitions each root list into contiguous child slices.
        # The lowest nonempty child address starts at the root payload base.
        alias_base = min((row[-2] for row in trees[1:] if row[-3]), default=0)
        for tree_index, (*box, children, count, start, models, pointer, child_offset) in enumerate(trees):
            if count:
                end = start + count
                if end > world_surface_count:
                    raise ValueError(f"Invalid surface range in IW6 render cell {cell_index} tree {tree_index}")
                start, count = slot_boundaries[start], slot_boundaries[end] - slot_boundaries[start]
            model_start = 0
            if tree_index and models:
                model_start, odd_address = divmod(pointer - alias_base, 2)
                if pointer >> 28 != 4 or odd_address or model_start + models > root_model_count:
                    raise ValueError(f"Invalid model slice in IW6 render cell {cell_index} tree {tree_index}")
            first_child = 0
            if children:
                first_child, misaligned = divmod(child_offset, SOURCE_TREE.size)
                if not first_child or misaligned or tree_index + first_child + children > tree_count:
                    raise ValueError(f"Invalid child range in IW6 render cell {cell_index} tree {tree_index}")
            payload.extend(struct.pack("<6f4H2I", *box, children, count, start if count else 0,
                                       models, model_start, first_child))

        vertex_offset = cell_offset + portal_count * SOURCE_PORTAL.size
        for portal_index in range(portal_count):
            x, y, z, distance, pointer, neighbor, reserved, count, *axis = SOURCE_PORTAL.unpack_from(
                zone, cell_offset + portal_index * SOURCE_PORTAL.size)
            if pointer != INLINE or reserved or neighbor >= cell_count or not 3 <= count <= 6:
                raise ValueError(f"Invalid portal {portal_index} in IW6 render cell {cell_index}")
            payload.extend(struct.pack("<4fHBB6f", x, y, z, distance, neighbor, count, 0, *axis))
            for _ in range(count):
                payload.extend(BSP_POINT.pack(*POINT.unpack_from(zone, vertex_offset)))
                vertex_offset += POINT.size

        start = cell_index * BSP_CELL_SIZE
        mid, half = bounds[:3], bounds[3:]
        struct.pack_into("<6f", cells, start, *(m - h for m, h in zip(mid, half)),
                         *(m + h for m, h in zip(mid, half)))
        cells[start + 44] = probe_count
        cell_offset = vertex_offset + probe_count
        cells[start + 45:start + 45 + probe_count] = bytes(
            probe_indices[index] for index in zone[vertex_offset:cell_offset])

    if tree_offset != CELLS or cell_offset != CELL_PAYLOADS_END:
        raise ValueError("Unexpected end of IW6 render cell data")
    return {0x19: cells, RENDER_SPATIAL: payload}
