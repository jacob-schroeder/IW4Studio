"""Project retail Ghosts Fog light-grid coefficients into IW4 palette colors."""

from itertools import product
from math import ldexp, sqrt
import struct


WORLD_LIGHTGRID = 0x14756CB
PALETTE_OFFSETS = 0x24A5E0F
PALETTE_DATA = 0x24E04EB


def _sample_bases():
    # Match the slice, row, then column order and rotated cube in GfxLightGridCodec.
    diagonal = sqrt(2) / 3
    vertical = sqrt(6) / 6
    for z, y, x in product(range(4), repeat=3):
        if 0 < x < 3 and 0 < y < 3 and 0 < z < 3:
            continue
        u, v, w = (index * (2 / 3) - 1 for index in (x, y, z))
        nx = u * diagonal + w / 3
        ny = -u * diagonal / 2 + v * vertical + w / 3
        nz = -u * diagonal / 2 - v * vertical + w / 3
        length = sqrt(nx * nx + ny * ny + nz * nz)
        x, y, z = nx / length, ny / length, nz / length
        # Ghosts evaluates coefficients against these pre-scaled SH terms.
        yield (1, x, y, z, x * z, y * z, x * y, 3 * z * z - 1, x * x - y * y)


_SAMPLE_BASES = tuple(_sample_bases())


def _coefficients(record, low, high):
    (header,) = struct.unpack_from(">H", record)
    offset = 2
    channels = []
    for shift in (10, 5, 0):
        exponent = (header >> shift & 31) - 8
        scale = ldexp(1.0, exponent)
        if exponent <= low:
            width, size = 8, 9
        else:
            width, size = (12, 14) if exponent <= high else (16, 18)
            offset += offset & 1
        if offset + size > len(record):
            raise ValueError("Truncated IW6 palette coefficients")
        if width == 8:
            dc = record[offset] * (scale / 256)
            directional = record[offset + 1:offset + 9]
        elif width == 12:
            # PS3 0x50e5fc reads high bytes and a nibble indexed by coefficient number.
            dc = struct.unpack_from(">H", record, offset)[0] * (scale / 65536)
            directional = []
            for i in range(1, 9):
                packed = record[offset + 9 + i // 2]
                nibble = (packed >> (4 if i & 1 else 0)) & 15
                directional.append((record[offset + 1 + i] << 4) | nibble)
        else:
            dc, *directional = struct.unpack_from(">9H", record, offset)
            dc *= scale / 65536
        denominator = 1 << (width - 1)
        channels.append((dc, *((value / denominator - 1) * scale for value in directional)))
        offset += size
    return channels


def palette_color(zone, index):
    """Read an authored Fog palette row and return 56 directional RGB byte triples."""
    low, high = struct.unpack_from(">2b", zone, WORLD_LIGHTGRID + 68)
    count, _, length = struct.unpack_from(">3I", zone, WORLD_LIGHTGRID + 76)
    if not 0 <= index < count:
        raise ValueError(f"IW6 palette index {index} is outside its source table")
    # Ghosts PS3 0x54a78c–0x54a7bc copies the default (0) and sky (1)
    # colors directly. Only the remaining indices use compressed coefficients.
    if index in (0, 1):
        start = WORLD_LIGHTGRID + (260 if index == 0 else 92)
        return zone[start:start + 168]
    offset = struct.unpack_from(">I", zone, PALETTE_OFFSETS + 4 * index)[0]
    end = (struct.unpack_from(">I", zone, PALETTE_OFFSETS + 4 * (index + 1))[0]
           if index + 1 < count else length)
    if offset & 1 or not offset + 2 <= end <= length or PALETTE_DATA + end > len(zone):
        raise ValueError(f"Invalid IW6 palette record span for index {index}")
    channels = _coefficients(zone[PALETTE_DATA + offset:PALETTE_DATA + end], low, high)
    result = bytearray()
    for basis in _SAMPLE_BASES:
        for channel in channels:
            irradiance = sum(coefficient * term for coefficient, term in zip(channel, basis))
            result.append(int(min(255, 127.5 * sqrt(max(0, irradiance)))))
    return bytes(result)
