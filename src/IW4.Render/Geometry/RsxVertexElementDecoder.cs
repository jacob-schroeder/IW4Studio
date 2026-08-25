using System.Buffers.Binary;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Math;

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
            RsxVertexElementType.Float32 when
                source.ComponentCount is 2 or 4 =>
                source.ComponentCount * sizeof(uint),
            RsxVertexElementType.Unsigned8Normalized when
                source.ComponentCount == 4 =>
                source.ComponentCount,
            RsxVertexElementType.Signed11_11_10Normalized when
                source.ComponentCount == 1 => sizeof(uint),
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
            case RsxVertexElementType.Float32:
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

            case RsxVertexElementType.Unsigned8Normalized:
                for (int component = 0; component < 4; component++)
                {
                    float value = sourceBytes[component] / 255f;
                    destinationBits[component] =
                        BitConverter.SingleToUInt32Bits(value);
                }
                return true;

            case RsxVertexElementType.Signed11_11_10Normalized:
                uint packed = BinaryPrimitives.ReadUInt32BigEndian(sourceBytes);
                System.Numerics.Vector3 decoded =
                    new PackedSigned11_11_10(packed).DecodeRsxNormalized();
                destinationBits[0] = BitConverter.SingleToUInt32Bits(decoded.X);
                destinationBits[1] = BitConverter.SingleToUInt32Bits(decoded.Y);
                destinationBits[2] = BitConverter.SingleToUInt32Bits(decoded.Z);
                return true;

            default:
                return false;
        }
    }

}
