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
using IW4.Render.Transforms;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Render.Geometry;

internal sealed class WorldVertexDecoder(
    IReadOnlyList<byte> stream0,
    IReadOnlyList<byte> stream1,
    VertexSource? texCoord,
    VertexSource? blendWeights,
    VertexSource? lightmapTexCoord = null,
    VertexSource? normal = null)
{
    public bool HasTexCoord => texCoord.HasValue;
    public bool HasBlendWeights => blendWeights.HasValue;
    public bool HasLightmapTexCoord =>
        lightmapTexCoord is { } source && IsLightmapTexCoordSource(source);
    public bool HasNormal =>
        normal is { } source && IsNormalSource(source);

    public bool TryReadTexCoord(GfxSurface surface, int surfaceIndex, out Vector2 value)
    {
        return TryReadTexCoord(
            texCoord,
            surface,
            surfaceIndex,
            out value);
    }

    public bool TryReadLightmapTexCoord(
        GfxSurface surface,
        int surfaceIndex,
        out Vector2 value)
    {
        if (lightmapTexCoord is not { } source ||
            !IsLightmapTexCoordSource(source))
        {
            value = default;
            return false;
        }

        return TryReadTexCoord(
            source,
            surface,
            surfaceIndex,
            out value);
    }

    private bool TryReadTexCoord(
        VertexSource? vertexSource,
        GfxSurface surface,
        int surfaceIndex,
        out Vector2 value)
    {
        value = default;
        if (vertexSource is not { } source)
            return false;

        if (source.IsDisabledDefaultAttribute)
        {
            value = Vector2.Zero;
            return true;
        }

        int offset = source.GetOffset(surface.Triangles, surfaceIndex);
        IReadOnlyList<byte>? bytes = GetStream(source.StreamIndex);
        if (bytes is null ||
            offset < 0 ||
            !VertexElementDecoder.TryReadBackendTexCoord(
                bytes,
                offset,
                source.FormatByte0,
                source.FormatByte1,
                source.ComponentA,
                source.ComponentB,
                out float u,
                out float v))
        {
            return false;
        }

        value = source.ApplyTransform(u, v);
        return true;
    }

    public bool TryReadNormal(
        GfxSurface surface,
        int surfaceIndex,
        out Vector3 value)
    {
        value = default;
        if (normal is not { } source ||
            source.IsDisabledDefaultAttribute ||
            !IsNormalSource(source))
        {
            return false;
        }

        var rsxSource = new WorldVertexSource(
            source.StreamIndex,
            0,
            source.FormatByte0,
            source.FormatByte1);
        if (!RsxVertexElementDecoder.TryGetEvent20LayerByteWidth(
                rsxSource,
                out int byteWidth) ||
            byteWidth != sizeof(uint))
        {
            return false;
        }

        int offset = source.GetOffset(surface.Triangles, surfaceIndex);
        IReadOnlyList<byte>? bytes = GetStream(source.StreamIndex);
        Span<byte> packedBytes = stackalloc byte[sizeof(uint)];
        if (bytes is null ||
            !TryCopyBytes(bytes, offset, byteWidth, packedBytes))
        {
            return false;
        }

        Span<uint> decodedBits = stackalloc uint[4];
        if (!RsxVertexElementDecoder.TryDecodeEvent20LayerFloat4Bits(
                packedBytes,
                rsxSource,
                decodedBits))
        {
            return false;
        }

        Vector3 gameNormal = new(
            BitConverter.UInt32BitsToSingle(decodedBits[0]),
            BitConverter.UInt32BitsToSingle(decodedBits[1]),
            BitConverter.UInt32BitsToSingle(decodedBits[2]));
        Vector3 renderNormal =
            RenderCoordinateConverter.GameToRenderPosition(gameNormal);
        float lengthSquared = renderNormal.LengthSquared();
        if (!IsFinite(renderNormal) ||
            !float.IsFinite(lengthSquared) ||
            lengthSquared <= 1e-12f)
        {
            return false;
        }

        value = renderNormal / MathF.Sqrt(lengthSquared);
        return IsFinite(value);
    }

    public bool TryReadBlendWeights(GfxSurface surface, int surfaceIndex, out Vector4 value)
    {
        value = Vector4.Zero;
        if (blendWeights is not { } source)
            return false;

        int offset = source.GetOffset(surface.Triangles, surfaceIndex);
        IReadOnlyList<byte>? bytes = GetStream(source.StreamIndex);
        if (bytes is null || offset < 0 || offset + 4 > bytes.Count)
            return false;

        // RSX type 0x04 is the packed four-byte vertex color/control input.
        // Preserve byte order here; individual shader-family swizzles remain
        // a fallback until their RSX instructions are mapped.
        value = new Vector4(
            bytes[offset] / 255f,
            bytes[offset + 1] / 255f,
            bytes[offset + 2] / 255f,
            bytes[offset + 3] / 255f);
        return true;
    }

    private IReadOnlyList<byte>? GetStream(byte streamIndex)
    {
        return streamIndex switch
        {
            0 => stream0,
            1 => stream1,
            _ => null
        };
    }

    private static bool TryCopyBytes(
        IReadOnlyList<byte> source,
        int offset,
        int length,
        Span<byte> destination)
    {
        if (offset < 0 ||
            length < 0 ||
            destination.Length < length ||
            source.Count < length ||
            offset > source.Count - length)
        {
            return false;
        }

        if (source is byte[] array)
        {
            array.AsSpan(offset, length).CopyTo(destination);
            return true;
        }

        for (int index = 0; index < length; index++)
            destination[index] = source[offset + index];
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsLightmapTexCoordSource(VertexSource source) =>
        source.FormatByte0 == 4 &&
        source.FormatByte1 == RsxVertexElementType.Float32 &&
        source.ComponentA == 2 &&
        source.ComponentB == 3;

    private static bool IsNormalSource(VertexSource source) =>
        source.FormatByte0 == 1 &&
        source.FormatByte1 ==
            RsxVertexElementType.Signed11_11_10Normalized;
}
