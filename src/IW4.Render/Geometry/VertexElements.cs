using System.Buffers.Binary;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Geometry;

internal static class VertexElementDecoder
{
    internal const int WorldVertexStride = 0x10;

    internal static bool TryReadBackendTexCoord(
        IReadOnlyList<byte> bytes,
        int offset,
        byte componentCount,
        RsxVertexElementType rsxType,
        int componentA,
        int componentB,
        out float u,
        out float v)
    {
        u = 0;
        v = 0;
        if (componentCount < 2 ||
            componentA < 0 ||
            componentB < 0 ||
            componentA >= componentCount ||
            componentB >= componentCount)
            return false;

        switch (rsxType)
        {
            case RsxVertexElementType.Float32:
                int floatOffsetA = offset + componentA * sizeof(float);
                int floatOffsetB = offset + componentB * sizeof(float);
                if (floatOffsetA + sizeof(float) > bytes.Count ||
                    floatOffsetB + sizeof(float) > bytes.Count)
                    return false;
                u = ReadSingleBigEndian(bytes, floatOffsetA);
                v = ReadSingleBigEndian(bytes, floatOffsetB);
                return true;

            case RsxVertexElementType.Float16:
                int halfOffsetA = offset + componentA * sizeof(ushort);
                int halfOffsetB = offset + componentB * sizeof(ushort);
                if (halfOffsetA + sizeof(ushort) > bytes.Count ||
                    halfOffsetB + sizeof(ushort) > bytes.Count)
                    return false;
                u = (float)BitConverter.UInt16BitsToHalf(ReadUInt16BigEndian(bytes, halfOffsetA));
                v = (float)BitConverter.UInt16BitsToHalf(ReadUInt16BigEndian(bytes, halfOffsetB));
                return true;

            default:
                return false;
        }
    }

    internal static float ReadSingleBigEndian(IReadOnlyList<byte> bytes, int offset)
    {
        if (bytes is byte[] array)
            return BinaryPrimitives.ReadSingleBigEndian(array.AsSpan(offset, sizeof(float)));

        Span<byte> scratch = stackalloc byte[sizeof(float)];
        for (int i = 0; i < scratch.Length; i++)
            scratch[i] = bytes[offset + i];
        return BinaryPrimitives.ReadSingleBigEndian(scratch);
    }

    private static ushort ReadUInt16BigEndian(IReadOnlyList<byte> bytes, int offset)
    {
        if (bytes is byte[] array)
            return BinaryPrimitives.ReadUInt16BigEndian(array.AsSpan(offset, sizeof(ushort)));

        Span<byte> scratch = stackalloc byte[sizeof(ushort)];
        for (int i = 0; i < scratch.Length; i++)
            scratch[i] = bytes[offset + i];
        return BinaryPrimitives.ReadUInt16BigEndian(scratch);
    }
}
