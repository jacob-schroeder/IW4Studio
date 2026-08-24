using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Math;

namespace IW4.Assets.Export.XModel;

/// <summary>
/// The fixed PS3 XSurface streams used by MTL_WORLDVERT_TEX_2_NRM_2.
/// Verts0 is position XYZW and Verts1 is colour, half UV0, packed normal and
/// packed tangent.  All multi-byte values are big endian.
/// </summary>
public static class XSurfaceVertexCodec
{
    public const int StreamStride = 0x10;

    public static void WriteVertex(
        Span<byte> verts0, Span<byte> verts1, int vertexIndex,
        Vector3 position, Vector2 uv0, Vector4 color, Vector3 normal,
        Vector3 tangent)
    {
        if (!float.IsFinite(uv0.X) || !float.IsFinite(uv0.Y) ||
            !float.IsFinite(color.X) || !float.IsFinite(color.Y) ||
            !float.IsFinite(color.Z) || !float.IsFinite(color.W) ||
            color.X is < 0 or > 1 || color.Y is < 0 or > 1 ||
            color.Z is < 0 or > 1 || color.W is < 0 or > 1)
            throw new ArgumentOutOfRangeException("Vertex UV or colour is not representable.");
        Half hu = (Half)uv0.X, hv = (Half)uv0.Y;
        if (!Half.IsFinite(hu) || !Half.IsFinite(hv))
            throw new ArgumentOutOfRangeException("Vertex UV cannot be represented by the PS3 half stream.");
        int offset = checked(vertexIndex * StreamStride);
        if (offset < 0 || offset > verts0.Length - StreamStride ||
            offset > verts1.Length - StreamStride)
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        WriteSingle(verts0, offset, position.X);
        WriteSingle(verts0, offset + 4, position.Y);
        WriteSingle(verts0, offset + 8, position.Z);
        WriteSingle(verts0, offset + 12, 1f);
        verts1[offset] = QuantizeColor(color.X);
        verts1[offset + 1] = QuantizeColor(color.Y);
        verts1[offset + 2] = QuantizeColor(color.Z);
        verts1[offset + 3] = QuantizeColor(color.W);
        BinaryPrimitives.WriteUInt16BigEndian(verts1[(offset + 4)..], BitConverter.HalfToUInt16Bits(hu));
        BinaryPrimitives.WriteUInt16BigEndian(verts1[(offset + 6)..], BitConverter.HalfToUInt16Bits(hv));
        BinaryPrimitives.WriteUInt32BigEndian(verts1[(offset + 8)..], EncodeDirection(normal));
        BinaryPrimitives.WriteUInt32BigEndian(verts1[(offset + 12)..], EncodeDirection(tangent));
    }

    public static bool TryReadPosition(IReadOnlyList<byte> verts0, int vertexIndex, out Vector3 position)
    {
        position = default;
        if (!TryOffset(verts0, vertexIndex, out int offset)) return false;
        position = new Vector3(ReadSingle(verts0, offset), ReadSingle(verts0, offset + 4), ReadSingle(verts0, offset + 8));
        return IsFinite(position);
    }

    public static bool TryReadColor(IReadOnlyList<byte> verts1, int vertexIndex, out Vector4 color)
    {
        color = default;
        if (!TryOffset(verts1, vertexIndex, out int offset)) return false;
        color = new Vector4(verts1[offset] / 255f, verts1[offset + 1] / 255f, verts1[offset + 2] / 255f, verts1[offset + 3] / 255f);
        return true;
    }

    public static bool TryReadUv0(IReadOnlyList<byte> verts1, int vertexIndex, out Vector2 uv0)
    {
        uv0 = default;
        if (!TryOffset(verts1, vertexIndex, out int offset)) return false;
        uv0 = new Vector2((float)BitConverter.UInt16BitsToHalf(ReadUInt16(verts1, offset + 4)), (float)BitConverter.UInt16BitsToHalf(ReadUInt16(verts1, offset + 6)));
        return float.IsFinite(uv0.X) && float.IsFinite(uv0.Y);
    }

    public static bool TryReadNormal(IReadOnlyList<byte> verts1, int vertexIndex, out Vector3 normal) => TryReadDirection(verts1, vertexIndex, 8, out normal);
    public static bool TryReadTangent(IReadOnlyList<byte> verts1, int vertexIndex, out Vector3 tangent) => TryReadDirection(verts1, vertexIndex, 12, out tangent);

    public static uint EncodeDirection(Vector3 value)
    {
        if (!TryNormalize(value, out Vector3 unit)) throw new ArgumentOutOfRangeException(nameof(value));
        int x = QuantizeSigned(unit.X, 11, 5);
        int y = QuantizeSigned(unit.Y, 11, 5);
        int z = QuantizeSigned(unit.Z, 10, 6);
        return (uint)(x & 0x7ff) | ((uint)(y & 0x7ff) << 11) | ((uint)(z & 0x3ff) << 22);
    }

    public static bool TryDecodeDirection(uint packed, out Vector3 value)
    {
        value = new PackedSigned11_11_10(packed).DecodeRsxNormalized();
        return TryNormalize(value, out value);
    }

    private static bool TryReadDirection(IReadOnlyList<byte> stream, int vertexIndex, int attributeOffset, out Vector3 value)
    {
        value = default;
        if (!TryOffset(stream, vertexIndex, out int offset)) return false;
        return TryDecodeDirection(ReadUInt32(stream, offset + attributeOffset), out value);
    }
    private static bool TryOffset(IReadOnlyList<byte> stream, int vertexIndex, out int offset)
    {
        offset = -1;
        if (vertexIndex < 0) return false;
        try { offset = checked(vertexIndex * StreamStride); }
        catch (OverflowException) { return false; }
        return offset <= stream.Count - StreamStride;
    }
    private static void WriteSingle(Span<byte> bytes, int offset, float value) => BinaryPrimitives.WriteInt32BigEndian(bytes[offset..], BitConverter.SingleToInt32Bits(value));
    private static float ReadSingle(IReadOnlyList<byte> bytes, int offset) => BitConverter.Int32BitsToSingle((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
    private static ushort ReadUInt16(IReadOnlyList<byte> bytes, int offset) => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    private static uint ReadUInt32(IReadOnlyList<byte> bytes, int offset) => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    private static byte QuantizeColor(float value) => (byte)System.Math.Clamp((int)MathF.Round(value * 255f, MidpointRounding.AwayFromZero), 0, 255);
    private static int QuantizeSigned(float value, int bits, int shift) => System.Math.Clamp((int)MathF.Round(value * 32767f / (1 << shift), MidpointRounding.AwayFromZero), -(1 << (bits - 1)), (1 << (bits - 1)) - 1);
    private static bool TryNormalize(Vector3 value, out Vector3 unit)
    {
        unit = default;
        if (!IsFinite(value) || value.LengthSquared() <= 0f) return false;
        unit = Vector3.Normalize(value);
        return IsFinite(unit);
    }
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
