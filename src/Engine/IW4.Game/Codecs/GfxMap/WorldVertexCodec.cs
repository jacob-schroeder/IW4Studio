using System.Buffers.Binary;
using IW4.Game.Math;

namespace IW4.Game.Codecs.GfxMap;

/// <summary>The PS3 world position and unlayered attribute streams.</summary>
public static class WorldVertexCodec
{
    public const int PositionStride = 16;
    public const int LayerStride = 28;

    public static void WriteVertex(
        Span<byte> positionRow,
        Span<byte> layerRow,
        Vec3 position,
        Vec3 normal,
        Vec3 tangent,
        Vec3 binormal,
        byte red, byte green, byte blue, byte alpha,
        float textureU, float textureV,
        float lightmapU, float lightmapV)
    {
        if (positionRow.Length < PositionStride || layerRow.Length < LayerStride)
            throw new ArgumentException("The world vertex streams do not contain one complete row.");
        RequireFinite(position);
        RequireFinite(normal);
        RequireFinite(tangent);
        RequireFinite(binormal);
        float crossX = normal.Y * tangent.Z - normal.Z * tangent.Y;
        float crossY = normal.Z * tangent.X - normal.X * tangent.Z;
        float crossZ = normal.X * tangent.Y - normal.Y * tangent.X;
        float dot = crossX * binormal.X + crossY * binormal.Y + crossZ * binormal.Z;
        WriteSingle(positionRow, 0, position.X);
        WriteSingle(positionRow, 4, position.Y);
        WriteSingle(positionRow, 8, position.Z);
        WriteSingle(positionRow, 12, dot < 0 ? -1 : 1);
        layerRow[0] = red;
        layerRow[1] = green;
        layerRow[2] = blue;
        layerRow[3] = alpha;
        WriteSingle(layerRow, 4, textureU);
        WriteSingle(layerRow, 8, textureV);
        WriteSingle(layerRow, 12, lightmapU);
        WriteSingle(layerRow, 16, lightmapV);
        BinaryPrimitives.WriteUInt32BigEndian(layerRow[20..], PackSignedNormal(normal));
        BinaryPrimitives.WriteUInt32BigEndian(layerRow[24..], PackSignedNormal(tangent));
    }

    private static uint PackSignedNormal(Vec3 value)
    {
        int x = QuantizeNormal(value.X, 1023);
        int y = QuantizeNormal(value.Y, 1023);
        int z = QuantizeNormal(value.Z, 511);
        return (uint)(x & 0x7ff) | ((uint)(y & 0x7ff) << 11) | ((uint)(z & 0x3ff) << 22);
    }

    private static int QuantizeNormal(float value, int scale) => checked((int)System.Math.Round(
        System.Math.Clamp((double)value, -1.0, 1.0) * scale,
        MidpointRounding.AwayFromZero));

    private static void RequireFinite(Vec3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException("A world vertex contains a non-finite vector.");
    }

    private static void WriteSingle(Span<byte> row, int offset, float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException("A render vertex contains a non-finite scalar.");
        BinaryPrimitives.WriteSingleBigEndian(row[offset..], value == 0.0f ? 0.0f : value);
    }
}
