using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Render.Geometry;

internal sealed class StaticVertexDecoder(VertexSource texCoord)
{
    public bool TryReadTexCoord(XSurface surface, int vertexIndex, out Vector2 value)
    {
        value = default;
        if (texCoord.IsDisabledDefaultAttribute)
        {
            value = Vector2.Zero;
            return true;
        }

        if (!TryGetVertexOffset(vertexIndex, texCoord.Stride, texCoord.Offset, out int offset))
            return false;

        IReadOnlyList<byte>? bytes = texCoord.StreamIndex switch
        {
            0 => surface.Verts0,
            1 => surface.Verts1,
            _ => null
        };
        if (bytes is null ||
            vertexIndex < 0 ||
            offset < 0 ||
            !VertexElementDecoder.TryReadBackendTexCoord(
                bytes,
                offset,
                texCoord.FormatByte0,
                texCoord.FormatByte1,
                componentA: 0,
                componentB: 1,
                out float u,
                out float v))
        {
            return false;
        }

        value = new Vector2(u, v);
        return true;
    }

    /// <summary>
    /// Reads backend source-table row 2 source 1, the four-component RSX
    /// normalized-byte color routed to input 3 by vertcol_simple_atest.
    /// </summary>
    public bool TryReadColor(
        XSurface surface,
        int vertexIndex,
        out Vector4 value)
    {
        value = Vector4.One;
        if (!WorldVertexLayout.TryGetSource(
                StaticXSurfaceVertexLayout.BackendRow,
                source: 0x01,
                out WorldVertexSource source) ||
            source.ComponentCount != 4 ||
            source.RsxType != 0x04 ||
            !WorldVertexLayout.TryGetStreamStride(
                StaticXSurfaceVertexLayout.BackendRow,
                source.StreamIndex,
                out byte stride) ||
            !TryGetVertexOffset(
                vertexIndex,
                stride,
                source.ByteOffset,
                out int offset))
        {
            return false;
        }

        IReadOnlyList<byte>? bytes = source.StreamIndex switch
        {
            0 => surface.Verts0,
            1 => surface.Verts1,
            _ => null
        };
        if (bytes is null ||
            offset < 0 ||
            bytes.Count < 4 ||
            offset > bytes.Count - 4)
        {
            return false;
        }

        value = new Vector4(
            bytes[offset] / 255f,
            bytes[offset + 1] / 255f,
            bytes[offset + 2] / 255f,
            bytes[offset + 3] / 255f);
        return true;
    }

    /// <summary>
    /// Reads and normalizes the PS3 static-XSurface normal routed by backend
    /// source-table row 2: Verts1 + 0x08, RSX S11_11_10_NR (type 0x06).
    /// </summary>
    public bool TryReadNormal(XSurface surface, int vertexIndex, out Vector3 value)
    {
        if (!StaticXSurfaceVertexLayout.TryGetNormal(out VertexSource source))
        {
            value = default;
            return false;
        }

        return TryReadPackedDirection(surface, vertexIndex, source, out value);
    }

    /// <summary>
    /// Reads and normalizes the PS3 static-XSurface tangent routed by backend
    /// source-table row 2: Verts1 + 0x0C, RSX S11_11_10_NR (type 0x06).
    /// </summary>
    public bool TryReadTangent(XSurface surface, int vertexIndex, out Vector3 value)
    {
        if (!StaticXSurfaceVertexLayout.TryGetTangent(out VertexSource source))
        {
            value = default;
            return false;
        }

        return TryReadPackedDirection(surface, vertexIndex, source, out value);
    }

    private static bool TryReadPackedDirection(
        XSurface surface,
        int vertexIndex,
        VertexSource source,
        out Vector3 value)
    {
        value = default;
        if (source.StreamIndex != 1 ||
            source.ComponentA != 0 ||
            source.FormatByte0 != 1 ||
            source.FormatByte1 != StaticXSurfaceVertexLayout.PackedDirectionRsxType ||
            !TryGetVertexOffset(vertexIndex, source.Stride, source.Offset, out int offset) ||
            surface.Verts1.Count < sizeof(uint) ||
            offset > surface.Verts1.Count - sizeof(uint))
        {
            return false;
        }

        Span<byte> packedBytes = stackalloc byte[sizeof(uint)];
        if (surface.Verts1 is byte[] array)
        {
            array.AsSpan(offset, sizeof(uint)).CopyTo(packedBytes);
        }
        else
        {
            for (int index = 0; index < packedBytes.Length; index++)
                packedBytes[index] = surface.Verts1[offset + index];
        }

        uint packed = BinaryPrimitives.ReadUInt32BigEndian(packedBytes);
        var decoded = new Vector3(
            (SignExtend((int)(packed & 0x7ff), 11) << 5) / 32767f,
            (SignExtend((int)((packed >> 11) & 0x7ff), 11) << 5) / 32767f,
            (SignExtend((int)((packed >> 22) & 0x3ff), 10) << 6) / 32767f);
        return StaticVertexBasisTransformer.TryNormalizeDirection(decoded, out value);
    }

    private static bool TryGetVertexOffset(
        int vertexIndex,
        int stride,
        int attributeOffset,
        out int offset)
    {
        offset = -1;
        if (vertexIndex < 0 || stride <= 0 || attributeOffset < 0)
            return false;

        long candidate = (long)vertexIndex * stride + attributeOffset;
        if (candidate > int.MaxValue)
            return false;

        offset = (int)candidate;
        return true;
    }

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }
}
