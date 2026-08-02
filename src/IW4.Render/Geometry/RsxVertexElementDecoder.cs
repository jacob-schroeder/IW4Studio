using System.Buffers.Binary;

namespace IW4.Render.Geometry;

/// <summary>
/// Exact host-independent float-bit decoding for the three RSX source types
/// present in active Event20 world-layer rows. Unsupported numeric types stay
/// raw and fail closed.
/// </summary>
internal static class RsxVertexElementDecoder
{
    internal static bool TryGetEvent20LayerByteWidth(
        WorldVertexSource source,
        out int byteWidth)
    {
        byteWidth = source.RsxType switch
        {
            0x02 when source.ComponentCount is 2 or 4 =>
                source.ComponentCount * sizeof(uint),
            0x04 when source.ComponentCount == 4 =>
                source.ComponentCount,
            0x06 when source.ComponentCount == 1 => sizeof(uint),
            _ => 0
        };
        return byteWidth != 0;
    }

    internal static bool TryDecodeEvent20LayerFloat4Bits(
        ReadOnlySpan<byte> sourceBytes,
        WorldVertexSource source,
        Span<uint> destinationBits)
    {
        if (destinationBits.Length < 4 ||
            !TryGetEvent20LayerByteWidth(source, out int byteWidth) ||
            sourceBytes.Length < byteWidth)
        {
            return false;
        }

        destinationBits[0] = 0;
        destinationBits[1] = 0;
        destinationBits[2] = 0;
        destinationBits[3] = 0x3f800000u;
        switch (source.RsxType)
        {
            case 0x02:
                for (int component = 0;
                     component < source.ComponentCount;
                     component++)
                {
                    destinationBits[component] =
                        BinaryPrimitives.ReadUInt32BigEndian(
                            sourceBytes.Slice(
                                component * sizeof(uint),
                                sizeof(uint)));
                }
                return true;

            case 0x04:
                for (int component = 0; component < 4; component++)
                {
                    float value = sourceBytes[component] / 255f;
                    destinationBits[component] =
                        BitConverter.SingleToUInt32Bits(value);
                }
                return true;

            case 0x06:
                uint packed = BinaryPrimitives.ReadUInt32BigEndian(sourceBytes);
                float x = (SignExtend((int)(packed & 0x7ff), 11) << 5) /
                    32767f;
                float y = (SignExtend(
                    (int)((packed >> 11) & 0x7ff),
                    11) << 5) / 32767f;
                float z = (SignExtend(
                    (int)((packed >> 22) & 0x3ff),
                    10) << 6) / 32767f;
                destinationBits[0] = BitConverter.SingleToUInt32Bits(x);
                destinationBits[1] = BitConverter.SingleToUInt32Bits(y);
                destinationBits[2] = BitConverter.SingleToUInt32Bits(z);
                return true;

            default:
                return false;
        }
    }

    private static int SignExtend(int value, int bitCount)
    {
        int shift = 32 - bitCount;
        return (value << shift) >> shift;
    }
}
