"""Export a supported IW6 PS3 clip map as IW4 v22 BSP collision lumps."""

import argparse
from collections import defaultdict
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess


MP_SWAMP_SHA256 = "101c19b32f44f996a43d4bc277b761c656d30b7b4615c2360fad0a5007d8d6f8"
MP_SWAMP_CLIP_MAP = 0x3CF853D
MP_SWAMP_PLANES = 0x1475AFD
MP_SWAMP_NODES = 0x3DE50EB
# The two lawn chairs and open folding chair by the campsite trailer.
MP_SWAMP_CAMPSITE_CHAIRS = ((49, 0x32002F9), (50, 0x32002F9), (124, 0x40826E2))
INLINE = 0xFFFFFFFF


class ZoneReader:
    def __init__(self, data, offset):
        self.data = data
        self.offset = offset

    def read(self, size):
        result = self.data[self.offset:self.offset + size]
        if len(result) != size:
            raise ValueError(f"Truncated source array at {self.offset:#x}")
        self.offset += size
        return result

    def rows(self, fmt, count):
        record = struct.Struct(">" + fmt)
        return list(record.iter_unpack(self.read(record.size * count)))

    def string(self):
        end = self.data.index(0, self.offset)
        return self.read(end - self.offset + 1)[:-1]


def packed_index(pointer, address, stride, count):
    index, remainder = divmod(pointer - address, stride)
    if remainder != 0 or not 0 <= index < count:
        raise ValueError(f"Source pointer {pointer:#x} falls outside table at {address:#x}")
    return index


def read_materials(reader, count, materials_address):
    rows = reader.rows("III", count)
    source_flags = [flags for _, flags, _ in rows]
    decisions = subprocess.run(
        ["dotnet", "fsi", "--exec", Path(__file__).with_name("CollisionRules.fsx")],
        input=json.dumps(source_flags), text=True, stdout=subprocess.PIPE, check=True)
    target_flags = json.loads(decisions.stdout)
    string_file_start = reader.offset
    string_address = materials_address + count * 12
    strings = {}
    for name_address, _, _ in rows:
        if name_address == INLINE:
            address = string_address + reader.offset - string_file_start
            strings[address] = reader.string()

    result = []
    unresolved = set()
    for (name_address, _, contents), flags in zip(rows, target_flags, strict=True):
        if name_address == INLINE:
            name = strings[string_address]
            string_address += len(name) + 1
        else:
            name = strings.get(name_address)
            if name is None:
                unresolved.add(name_address)
                name = f"iw6_clip_{name_address:08x}".encode("ascii")
        if len(name) >= 64:
            raise ValueError(f"Collision material name exceeds the disk field: {name!r}")
        result.append((name, flags, contents))
    return result, len(unresolved)


def read_leaf_brushes(reader, node_count, brush_count):
    nodes = reader.rows("BxhiI4xHH", node_count)
    terminal_brushes = []
    for _, count, _, pointer, _, _ in nodes:
        if count > 0:
            if pointer != INLINE:
                raise ValueError("Expected inline indices for terminal collision brush nodes")
            terminal_brushes.append([brush[0] for brush in reader.rows("H", count)])
        else:
            terminal_brushes.append([])
    if sum(map(len, terminal_brushes)) != brush_count:
        raise ValueError("Collision brush node spans do not cover the leaf-brush table")
    return nodes, terminal_brushes


def leaf_brush_list(nodes, terminal_brushes, root):
    if root == 0:
        return []
    visited = set()
    brushes = []
    pending = [root]
    while pending:
        index = pending.pop()
        if index in visited or not 0 < index < len(nodes):
            raise ValueError(f"Invalid collision leaf-brush node {index}")
        visited.add(index)
        _, count, _, _, child0, child1 = nodes[index]
        if count > 0:
            brushes.extend(terminal_brushes[index])
            continue
        if count not in (-1, 0) or child0 == 0 or child1 == 0:
            raise ValueError(f"Invalid branches in collision leaf-brush node {index}")
        pending.extend((index + child1, index + child0))
        if count == -1:
            pending.append(index + 1)
    return list(dict.fromkeys(brushes))


def encode_rows(fmt, rows):
    record = struct.Struct("<" + fmt)
    return b"".join(record.pack(*row) for row in rows)


def encode_brushes(brushes, bounds, contents, sides, edge_data, info, materials, plane_base, plane_count):
    # IW6 stores the native array layout. IW4 v22 interleaves minus/plus
    # axial faces by axis and writes each face's edges in that same order.
    material_by_contents = {}
    for i, (_, _, material_contents) in enumerate(materials):
        material_by_contents.setdefault(material_contents, i)

    side_rows = []
    edge_counts = bytearray()
    edges = bytearray()
    brush_rows = []
    glass_brushes = {}
    for brush_id, (brush, bounds_row, brush_contents) in enumerate(zip(brushes, bounds, contents, strict=True)):
        side_count, glass_index, side_pointer, edge_pointer, *axial_data = brush
        midpoint, half_size = bounds_row[:3], bounds_row[3:]
        if glass_index:
            glass_brushes[brush_id] = glass_index
        if side_count:
            first_side = packed_index(side_pointer, info[5], 8, len(sides))
            if first_side + side_count > len(sides):
                raise ValueError(f"Brush {brush_id} sides extend beyond the source table")
            extra_sides = sides[first_side:first_side + side_count]
        else:
            extra_sides = []
        first_edge = packed_index(edge_pointer, info[7], 1, len(edge_data)) if edge_pointer else 0
        start = len(edges)
        face_edges = []
        disk_sides = []
        for axis in range(3):
            for direction in range(2):
                axial_index = axis + direction * 3
                bound = midpoint[axis] + (half_size[axis] if direction else -half_size[axis])
                material = axial_data[axial_index]
                disk_sides.append(struct.pack("<fi", bound, material))
                face_edges.append((axial_data[6 + axial_index], axial_data[12 + axial_index]))
        for plane_pointer, material, adjacent, count in extra_sides:
            plane = packed_index(plane_pointer, plane_base, 20, plane_count)
            disk_sides.append(struct.pack("<ii", plane, material))
            face_edges.append((adjacent, count))
        for adjacent, count in face_edges:
            if len(edges) - start > 255:
                raise ValueError(f"Brush {brush_id} exceeds the IW4 adjacency offset range")
            edge_counts.append(count)
            if count == 0:
                continue
            offset = first_edge + adjacent
            if not edge_pointer or offset + count > len(edge_data):
                raise ValueError(f"Brush {brush_id} edge span is outside the source table")
            edges.extend(edge_data[offset:offset + count])
        side_rows.extend(disk_sides)
        brush_rows.append((6 + side_count, material_by_contents[brush_contents[0]]))
    return {
        "05-BrushSides.bin": b"".join(side_rows),
        "06-BrushSideEdgeCounts.bin": bytes(edge_counts),
        "07-BrushEdges.bin": bytes(edges),
        "08-Brushes.bin": encode_rows("hh", brush_rows),
    }, glass_brushes


def encode_leaf_graph(leaves, models, nodes, terminal_brushes, brush_count, aabb_count, extra_leaf_brushes):
    leaf_rows = []
    brush_rows = []
    for leaf_index, (first_aabb, aabbs, root) in enumerate(leaves):
        brushes = leaf_brush_list(nodes, terminal_brushes, root)
        if first_aabb + aabbs > aabb_count or any(brush >= brush_count for brush in brushes):
            raise ValueError("BSP leaf points outside the collision arrays")
        brushes.extend(extra_leaf_brushes.get(leaf_index, ()))
        leaf_rows.append((0, first_aabb, aabbs, len(brush_rows), len(brushes), 0))
        brush_rows.extend((brush,) for brush in brushes)

    model_rows = []
    for index, model in enumerate(models):
        midpoint, half_size = model[:3], model[3:6]
        # The IW4 collision decoder expands disk bounds by one unit.
        minimum = [mid - half + 1.0 for mid, half in zip(midpoint, half_size)]
        maximum = [mid + half - 1.0 for mid, half in zip(midpoint, half_size)]
        if any(low > high for low, high in zip(minimum, maximum)):
            raise ValueError(f"Invalid bounds for brush model {index}")
        first_aabb, aabbs, root = model[8:]
        brushes = sorted(leaf_brush_list(nodes, terminal_brushes, root))
        first_brush = brushes[0] if brushes else 0
        if (first_aabb + aabbs > aabb_count or first_brush + len(brushes) > brush_count or
                brushes != list(range(first_brush, first_brush + len(brushes)))):
            raise ValueError(f"Brush model {index} does not use contiguous disk spans")
        # The packaging caller fills the layered/unlayered render surface ranges.
        model_rows.append((*minimum, *maximum, first_aabb, aabbs, first_brush, len(brushes)))
    return {
        "1c-Leafs.bin": encode_rows("6i", leaf_rows),
        "1d-LeafBrushes.bin": encode_rows("I", brush_rows),
        "25-Models.bin": encode_rows("6f8x4i", model_rows),
    }


def append_campsite_chair_clips(zone, lumps, materials, planes, node_rows, brush_count):
    # IW4's static XModel traces are point-only. Use the source bounds as
    # player capsule blockers for these clutter placements.
    player_clip = next(index for index, (name, _, contents) in enumerate(materials)
                       if name == b"clip_player" and contents == 0x08010000)
    # Clockwise adjacent sides of an axial box, ordered by -X, +X, -Y, +Y, -Z, +Z.
    box_edges = ((2, 4, 3, 5), (5, 3, 4, 2), (5, 1, 4, 0),
                 (0, 4, 1, 5), (0, 2, 1, 3), (3, 1, 2, 0))
    extra_leaf_brushes = defaultdict(list)
    for brush_index, (dynent_index, model_root) in enumerate(MP_SWAMP_CAMPSITE_CHAIRS, brush_count):
        # DynEntityDef pose and the XModel's local midpoint/half-size.
        row = 0x4077BA7 + dynent_index * 100
        kind, qx, qy, qz, qw, ox, oy, oz = struct.unpack_from(">I7f", zone, row)
        if (kind, qx, qy) != (1, 0, 0):
            raise ValueError(f"Expected upright clutter for campsite chair {dynent_index}")
        x, y, z, hx, hy, hz = struct.unpack_from(">6f", zone, model_root + 252)
        magnitude_squared = qz * qz + qw * qw
        cosine = (qw * qw - qz * qz) / magnitude_squared
        sine = 2 * qz * qw / magnitude_squared
        center = (ox + cosine * x - sine * y, oy + sine * x + cosine * y, oz + z)
        half = (abs(cosine) * hx + abs(sine) * hy, abs(sine) * hx + abs(cosine) * hy, hz)
        axial_sides = [(mid + sign * extent, player_clip)
                       for mid, extent in zip(center, half) for sign in (-1, 1)]
        side_count = len(axial_sides)
        lumps["05-BrushSides.bin"] += encode_rows("fi", axial_sides)

        if sine:
            # The four world-axis bounds act as bevels. The rotated planes
            # establish the chair footprint and own its horizontal face edges.
            face_numbers = (6, 7, 8, 9, 4, 5)
            edges = [()] * 4 + [
                tuple(face_numbers[i] for i in box_edges[face]) for face in (4, 5, 0, 1, 2, 3)
            ]
            rotated_planes = []
            for (nx, ny), extent in (((cosine, sine), hx), ((-sine, cosine), hy)):
                midpoint = nx * center[0] + ny * center[1]
                rotated_planes.extend((sign * nx, sign * ny, 0, sign * midpoint + extent) for sign in (-1, 1))
            first_plane = len(lumps["04-Planes.bin"]) // 16
            lumps["04-Planes.bin"] += encode_rows("4f", rotated_planes)
            lumps["05-BrushSides.bin"] += encode_rows(
                "ii", ((first_plane + index, player_clip) for index in range(len(rotated_planes))))
            side_count += len(rotated_planes)
        else:
            edges = box_edges

        lumps["06-BrushSideEdgeCounts.bin"] += bytes(map(len, edges))
        lumps["07-BrushEdges.bin"] += bytes(side for face in edges for side in face)
        lumps["08-Brushes.bin"] += struct.pack("<hh", side_count, player_clip)

        # Link the brush to every original world BSP leaf overlapped by its bounds.
        pending = [0]
        while pending:
            node = pending.pop()
            if node < 0:
                extra_leaf_brushes[-1 - node].append(brush_index)
                continue
            plane_index, front, back = node_rows[node]
            *normal, distance = planes[plane_index]
            offset = sum(a * b for a, b in zip(center, normal)) - distance
            radius = sum(a * abs(b) for a, b in zip(half, normal))
            if offset + radius >= 0:
                pending.append(front)
            if offset - radius <= 0:
                pending.append(back)

    return extra_leaf_brushes


def encode_terrain(vertices, triangles, walkable_edges, borders, partitions, aabbs, material_count):
    disk_borders = []
    disk_partitions = []
    border_address = partitions[0][4]
    next_triangle = 0
    for triangle_count, border_count, segment, first_triangle, border_pointer in partitions:
        if segment or (triangle_count and first_triangle != next_triangle):
            raise ValueError("Unsupported segmented or non-sequential terrain triangles")
        disk_partitions.append((triangle_count, border_count, first_triangle, len(disk_borders)))
        if border_count:
            first_border = packed_index(border_pointer, border_address, 28, len(borders))
            if first_border + border_count > len(borders):
                raise ValueError("Terrain partition extends beyond the source border table")
            disk_borders.extend(borders[first_border:first_border + border_count])
        next_triangle += triangle_count
    if next_triangle != len(triangles):
        raise ValueError("Terrain partitions do not cover the source triangle table")
    if any(index >= len(vertices) for triangle in triangles for index in triangle):
        raise ValueError("A terrain triangle references a missing vertex")

    disk_aabbs = []
    for x, y, z, material, children, dx, dy, dz, first in aabbs:
        upper = len(aabbs) if children else len(partitions)
        if material >= material_count or not 0 <= first < upper or first + children > upper:
            raise ValueError("Terrain AABB contains an invalid material, child, or partition index")
        disk_aabbs.append((x, y, z, dx, dy, dz, material, children, first))
    return {
        "1f-CollisionVerts.bin": encode_rows("3f", vertices),
        "20-CollisionTris.bin": encode_rows("3H", triangles),
        "21-CollisionEdgeWalkable.bin": walkable_edges,
        "22-CollisionBorders.bin": encode_rows("7f", disk_borders),
        "23-CollisionPartitions.bin": encode_rows("2xBBii", disk_partitions),
        "24-CollisionAabbs.bin": encode_rows("6fHHi", disk_aabbs),
    }


def export_collision(source, output):
    data = source.read_bytes()
    source_hash = hashlib.sha256(data).hexdigest()
    if source_hash != MP_SWAMP_SHA256:
        raise ValueError(f"Unsupported IW6 PS3 collision source SHA-256 {source_hash}")

    # Ghosts default_mp.pdb: clipMap_t B8CE, ClipInfo B8EC, cplane_s 100A7.
    root = struct.unpack_from(">68I", data, MP_SWAMP_CLIP_MAP)
    reader = ZoneReader(data, MP_SWAMP_CLIP_MAP + 384)
    plane_count, plane_base = root[2:4]
    material_count, side_count, edge_count = root[4:9:2]
    leaf_brush_node_count, leaf_brush_count = root[10:13:2]
    brush_count = root[14] >> 16

    # The shared ClipInfo pointer follows the inline tables. Its direct
    # pointers identify the destination address of each table.
    info_offset = 0x3DAAC6C
    info = struct.unpack_from(">16I", data, info_offset)
    materials, unresolved_names = read_materials(reader, material_count, info[3])
    sides = reader.rows("IHBB", side_count)
    edge_data = reader.read(edge_count)
    brush_nodes, terminal_brushes = read_leaf_brushes(reader, leaf_brush_node_count, leaf_brush_count)
    brushes = reader.rows("HHII6h12B", brush_count)
    bounds = reader.rows("6f", brush_count)
    contents = reader.rows("I", brush_count)
    if reader.offset != info_offset:
        raise ValueError("Inline collision tables end outside the source ClipInfo boundary")

    plane_reader = ZoneReader(data, MP_SWAMP_PLANES)
    planes = plane_reader.rows("4f4x", plane_count)
    if any(not math.isclose(x * x + y * y + z * z, 1.0, abs_tol=0.01) for x, y, z, _ in planes):
        raise ValueError("Invalid normals in the source shared collision plane table")

    # BSP nodes follow the static-model table and its single inline XModel.
    reader.offset = MP_SWAMP_NODES
    bsp_nodes = reader.rows("Ihh", root[21])
    leaves = reader.rows("HH32xi", root[23])
    vertices = reader.rows("3f", root[25])
    triangles = reader.rows("3H", root[27])
    walkable_edges = reader.read(((len(triangles) * 3 + 31) // 32) * 4)
    borders = reader.rows("7f", root[30])
    partitions = reader.rows("BBBxii", root[32])
    aabbs = reader.rows("3fHH3fi", root[34])
    models = reader.rows("7fIHH32xi", root[36])
    node_rows = []
    for plane_pointer, front, back in bsp_nodes:
        if not all(-len(leaves) <= child < len(bsp_nodes) for child in (front, back)):
            raise ValueError("BSP node child index is out of range")
        node_rows.append((packed_index(plane_pointer, plane_base, 20, plane_count), front, back))

    lumps, glass_brushes = encode_brushes(
        brushes, bounds, contents, sides, edge_data, info, materials, plane_base, plane_count)
    lumps.update(encode_terrain(vertices, triangles, walkable_edges, borders, partitions, aabbs, material_count))
    lumps["00-Materials.bin"] = encode_rows("64sII", materials)
    lumps["04-Planes.bin"] = encode_rows("4f", planes)
    lumps["1b-Nodes.bin"] = encode_rows("3i24x", node_rows)
    chair_clips = append_campsite_chair_clips(data, lumps, materials, planes, node_rows, brush_count)
    lumps.update(encode_leaf_graph(
        leaves, models, brush_nodes, terminal_brushes, brush_count, len(aabbs), chair_clips))

    output.mkdir(parents=True, exist_ok=True)
    for name, payload in lumps.items():
        (output / name).write_bytes(payload)
    manifest = {
        "source_sha256": source_hash,
        "source_checksum": f"{root[67]:08x}",
        "lumps": {name: len(payload) for name, payload in sorted(lumps.items())},
        "unresolved_collision_material_names": unresolved_names,
        "source_static_model_count": root[19],
        "source_dynamic_model_count": root[49] >> 16,
        "source_glass_brush_indices": glass_brushes,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="inflated IW6 PS3 map zone")
    parser.add_argument("output", type=Path, help="directory for IW4 v22 collision lumps")
    args = parser.parse_args()
    try:
        export_collision(args.source, args.output)
    except (KeyError, OSError, ValueError, struct.error) as error:
        parser.exit(1, f"Collision export failed: {error}\n")


if __name__ == "__main__":
    main()
