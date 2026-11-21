"""Read the compressed PS3 Fog GfxLightGridTree in world grid coordinates."""

from itertools import product
import struct

from irradiance import PALETTE_DATA, WORLD_LIGHTGRID


# PS3 0x5130d0 initializes the layouts used by the leaf command and voxel masks.
MASK_LAYOUTS = (
    (0, 1, 4, 5), (2, 3, 6, 7), (8, 9, 12, 13), (10, 11, 14, 15),
    (0, 2, 8, 10), (1, 3, 9, 11), (4, 6, 12, 14), (5, 7, 13, 15),
    (0, 1, 2, 3), (1, 4, 3, 6), (4, 5, 6, 7), (2, 3, 8, 9),
    (6, 7, 12, 13), (8, 9, 10, 11), (9, 12, 11, 14), (12, 13, 14, 15),
)

# Ghosts uses absolute XY modulo four to index this table at PS3 0x6dc5d8.
SLOTS = (0, 1, 4, 5, 2, 3, 6, 7, 8, 9, 12, 13, 10, 11, 14, 15)


class BitReader:
    def __init__(self, data, offset):
        self.data = data
        self.position = offset * 8

    def read(self, width):
        byte, shift = divmod(self.position, 8)
        self.position += width
        end = self.byte_end()
        if end > len(self.data):
            raise ValueError("Truncated Ghosts light-grid bitstream")
        value = int.from_bytes(self.data[byte:end], 'little')
        return (value >> shift) & ((1 << width) - 1)

    def byte_end(self):
        return (self.position + 7) // 8


def mask(bits, voxel=False):
    mode = bits.read(2 if voxel else 1)
    if mode == 0:
        return bits.read(16)
    if voxel and mode == 1:
        return 0xffff
    slots = MASK_LAYOUTS[bits.read(4)]
    invert = mode == 3 if voxel else bits.read(1)
    subset = bits.read(len(slots))
    selected = sum(1 << slot for i, slot in enumerate(slots) if subset & (1 << i))
    return selected ^ 0xffff if invert else selected


def absolute(base, value):
    return base + value - 1 if value else 0


def node_field(bits, flags, max_width, default_width):
    if flags & 1:
        base = bits.read(max_width)
        width = bits.read((max_width - 1).bit_length()) + 1
    else:
        base, width = 0, default_width
    default = bits.read(width) if flags & 2 else 0
    table = ()
    if flags & 4:
        # The first palette entry is implicitly zero (Ghosts 0x511da0).
        count = bits.read(4) + 1
        table = (0, *(absolute(base, bits.read(width)) for _ in range(count - 1)))
    return base, width, default, table


def leaf_field(bits, flags, node):
    base, width, default, table = node
    table_width = max(1, (len(table) - 1).bit_length()) if table else 0
    if flags & 1:
        first = bits.read(width)
        value_width = bits.read(max(1, (width - 1).bit_length())) + 1
    elif table:
        first, value_width = 1, table_width
    else:
        first, value_width = 1 + (base == 0), width
    first = absolute(base, first)
    if flags & 2:
        value = bits.read(value_width)
    elif table and not flags & 1:
        value = default
    else:
        node_default = table[default] if table else absolute(base, default)
        value = node_default - first + 1 if node_default else 0
    values = [value] * 16
    if flags & 4:
        while True:
            op = bits.read(2)
            if op == 0:
                values = [bits.read(value_width) for _ in values]
                break
            if op == 1:
                slots = [bits.read(4) for _ in range(bits.read(1) + 2)]
            else:
                selected = mask(bits)
                slots = [i for i in range(16) if selected & (1 << i)]
            if op == 2:
                for i in slots:
                    values[i] = bits.read(value_width)
                break
            value = bits.read(value_width)
            for i in slots:
                values[i] = value
            if not bits.read(1):
                break
    if table and value_width >= table_width:
        return [table[v] for v in values]
    return [absolute(first, v) for v in values]


def grid_corners(zone):
    # Fog's tree directly follows the two fallback RGB rows in the GfxLightGrid.
    tree = WORLD_LIGHTGRID + 428
    levels = zone[tree]
    count, leaf_count, *bounds = struct.unpack_from(">2I6i", zone, tree + 4)
    color_bits, light_bits, nodes_ptr, leaf_length, leaf_ptr = struct.unpack_from(">5I", zone, tree + 48)
    if levels != 6 or nodes_ptr != 0xffffffff or leaf_ptr != 0xffffffff:
        raise ValueError("Unsupported Fog light-grid tree")
    nodes_start = PALETTE_DATA + struct.unpack_from(">I", zone, WORLD_LIGHTGRID + 84)[0]
    nodes = struct.unpack_from(f">{count}I", zone, nodes_start)
    leaf_start = nodes_start + count * 4
    data = memoryview(zone)[leaf_start:leaf_start + leaf_length]

    def blocks(index, level, x, y, z):
        word = nodes[index]
        children, start = word >> 24, word & 0xffffff
        if level == 0:
            bits = BitReader(data, start)
            flags = bits.read(8)
            sizes = [bits.read(6) for _ in range(children.bit_count())]
            colors = node_field(bits, flags, color_bits, 16)
            lights = node_field(bits, flags >> 3, light_bits, 1)
            offset = bits.byte_end()
        rank = 0
        for octant in range(8):
            if not children & (1 << octant):
                continue
            bx = x + ((octant & 1) << level)
            by = y + (((octant >> 1) & 1) << level)
            bz = z + (((octant >> 2) & 1) << level)
            if level:
                yield from blocks(start + rank, level - 1, bx, by, bz)
            else:
                size = sizes[rank]
                bits = BitReader(data, offset)
                flags = bits.read(8) if size else 0
                indices = leaf_field(bits, flags, colors)
                primary = leaf_field(bits, flags >> 3, lights)
                lower = mask(bits, voxel=True) if flags & 0x40 else 0
                upper = mask(bits, voxel=True) if flags & 0x80 else 0
                offset += size
                if bits.byte_end() != offset:
                    raise ValueError(f"Invalid Ghosts light-grid leaf ending at {offset:#x}")
                yield bx, by, bz, indices, primary, lower, upper
            rank += 1

    n = 0
    for bx, by, bz, indices, primary, lower, upper in blocks(0, levels, 0, 0, 0):
        n += 1
        for dx, dy in product(range(4), repeat=2):
            x, y, z = bounds[0] + bx * 4 + dx, bounds[1] + by * 4 + dy, bounds[2] + bz
            if x <= bounds[3] and y <= bounds[4] and z <= bounds[5]:
                slot = SLOTS[(y & 3) * 4 + (x & 3)]
                voxel = ((lower >> slot) & 1) | (((upper >> slot) & 1) << 1)
                yield (x, y, z), indices[slot], primary[slot], voxel
    if n != leaf_count:
        raise ValueError(f"Expected {leaf_count} Ghosts light-grid leaves, decoded {n}")
