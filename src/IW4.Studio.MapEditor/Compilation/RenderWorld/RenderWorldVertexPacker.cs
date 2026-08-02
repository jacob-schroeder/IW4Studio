using System.Buffers.Binary;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

/// <summary>
/// Deterministic host-independent packer for the sole M3 structural profile.
/// Game-space coordinates and authored channel values are preserved; no
/// renderer coordinate conversion or lighting derivation occurs here.
/// </summary>
internal static class RenderWorldVertexPacker
{
    internal static void WritePositionRow(
        AuthoredRenderVertex vertex,
        Span<byte> destination)
    {
        if (destination.Length <
            RenderWorldStructuralProfile.PositionStride)
        {
            throw new ArgumentException(
                "A packed world-position destination requires 16 bytes.",
                nameof(destination));
        }

        WriteSingle(destination, 0x00, vertex.Position.X);
        WriteSingle(destination, 0x04, vertex.Position.Y);
        WriteSingle(destination, 0x08, vertex.Position.Z);
        WriteSingle(destination, 0x0C, 1f);
    }

    internal static void WriteVertexLayerRow(
        AuthoredRenderVertex vertex,
        Span<byte> destination)
    {
        if (destination.Length <
            RenderWorldStructuralProfile.VertexLayerStride)
        {
            throw new ArgumentException(
                "A packed world vertex-layer destination requires 28 " +
                "bytes.",
                nameof(destination));
        }

        destination[0x00] = vertex.Color.Red;
        destination[0x01] = vertex.Color.Green;
        destination[0x02] = vertex.Color.Blue;
        destination[0x03] = vertex.Color.Alpha;
        WriteSingle(
            destination,
            0x04,
            vertex.TextureCoordinates.U);
        WriteSingle(
            destination,
            0x08,
            vertex.TextureCoordinates.V);
        WriteSingle(
            destination,
            0x0C,
            vertex.LightmapCoordinates.U);
        WriteSingle(
            destination,
            0x10,
            vertex.LightmapCoordinates.V);
        BinaryPrimitives.WriteUInt32BigEndian(
            destination[0x14..],
            PackSignedNormal(vertex.Normal));
        BinaryPrimitives.WriteUInt32BigEndian(
            destination[0x18..],
            PackSignedNormal(vertex.Tangent));
    }

    /// <summary>
    /// Packs signed normalized XYZ as the RSX S11_11_10_NR word consumed by
    /// backend row 5. Midpoints round away from zero for stable symmetry.
    /// </summary>
    internal static uint PackSignedNormal(MapVector3 value)
    {
        if (!value.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Packed normal inputs must be finite.");
        }

        int x = Quantize(value.X, 1023);
        int y = Quantize(value.Y, 1023);
        int z = Quantize(value.Z, 511);
        return
            (uint)(x & 0x7FF) |
            ((uint)(y & 0x7FF) << 11) |
            ((uint)(z & 0x3FF) << 22);
    }

    private static int Quantize(float value, int scale)
    {
        double clamped = Math.Clamp((double)value, -1d, 1d);
        return checked((int)Math.Round(
            clamped * scale,
            MidpointRounding.AwayFromZero));
    }

    private static void WriteSingle(
        Span<byte> destination,
        int offset,
        float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Packed vertex scalar inputs must be finite.");
        }

        BinaryPrimitives.WriteSingleBigEndian(
            destination[offset..],
            value == 0f ? 0f : value);
    }
}
