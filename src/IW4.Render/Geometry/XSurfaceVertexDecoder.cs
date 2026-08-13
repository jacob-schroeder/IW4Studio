using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Math;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Assets.XModel.Export;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.Geometry;

/// <summary>
/// Decodes the recovered PS3 static-XSurface vertex streams described by
/// backend source-table row 2 (the static-model-cache declaration).
/// </summary>
internal sealed class XSurfaceVertexDecoder
{
    internal const int RsxVertexInputCount = 16;
    internal const int RsxVertexInputComponentCount = 4;
    internal const int BackendRow =
        (int)MaterialVertexDeclarationType.StaticModelCache;
    internal const MaterialStreamSource DefaultTexCoordSource =
        MaterialStreamSource.TexCoord0;

    private const int PositionStride = 0x10;
    private const MaterialStreamSource ColorSource =
        MaterialStreamSource.Color;
    private const MaterialStreamSource NormalSource =
        MaterialStreamSource.Normal;
    private const MaterialStreamSource TangentSource =
        MaterialStreamSource.Tangent;
    private const RsxVertexElementType PackedDirectionRsxType =
        RsxVertexElementType.Signed11_11_10Normalized;
    private const float MaximumReasonableCoordinate = 1_000_000f;

    private readonly VertexSource _texCoord;

    private XSurfaceVertexDecoder(VertexSource texCoord)
    {
        _texCoord = texCoord;
    }

    internal static bool TryCreate(
        MaterialStreamSource texCoordSource,
        out XSurfaceVertexDecoder? decoder)
    {
        decoder = null;
        if (!WorldVertexLayout.TryGetSource(
                BackendRow,
                texCoordSource,
                out WorldVertexSource source))
        {
            return false;
        }

        if (WorldVertexLayout.TryGetStreamStride(
                BackendRow,
                source.StreamIndex,
                out byte stride))
        {
            decoder = new XSurfaceVertexDecoder(new VertexSource(
                source.StreamIndex,
                stride,
                source.ByteOffset,
                source.ComponentCount,
                source.RsxType));
            return true;
        }

        if (!source.IsUnavailableSourceTuple)
            return false;

        decoder = new XSurfaceVertexDecoder(new VertexSource(
            source.StreamIndex,
            0,
            source.ByteOffset,
            source.ComponentCount,
            source.RsxType));
        return true;
    }

    internal static UvRoute CreateUvRoute(MaterialStreamSource texCoordSource)
    {
        if (!WorldVertexLayout.TryGetSource(
                BackendRow,
                texCoordSource,
                out WorldVertexSource source) ||
            !WorldVertexLayout.TryGetStreamStride(
                BackendRow,
                source.StreamIndex,
                out byte stride))
        {
            return UvRoute.StaticModel(texCoordSource);
        }

        UvBaseMode baseMode = source.StreamIndex == 1
            ? UvBaseMode.Stream1ZeroBase
            : UvBaseMode.Stream0LocalIndexOnly;
        return new UvRoute(
            "static model tc0",
            MaterialVertexDeclarationType.StaticModelCache.ToString(),
            texCoordSource,
            source.StreamIndex,
            stride,
            source.ByteOffset,
            source.ComponentCount,
            source.RsxType,
            baseMode,
            0,
            1,
            1f,
            1f,
            0f,
            0f);
    }

    internal static bool TryReadRsxVertexInputs(
        XSurface surface,
        int vertexIndex,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        Span<Vector4> values,
        out string blocker)
    {
        if (values.Length != RsxVertexInputCount)
        {
            throw new ArgumentException(
                $"RSX vertex input destination must contain exactly {RsxVertexInputCount} values.",
                nameof(values));
        }

        values.Fill(new Vector4(0f, 0f, 0f, 1f));
        blocker = string.Empty;
        foreach (ShaderVertexInputBinding binding in bindings)
        {
            int destination = (byte)binding.Destination;
            if (destination >= values.Length)
            {
                blocker = $"dest0x{destination:X2}:OUT_OF_RANGE";
                return false;
            }
            if (binding.IsDisabledDefaultAttribute)
                continue;
            if (binding.StreamIndex > 1)
            {
                blocker =
                    $"dest0x{destination:X2}:STREAM{binding.StreamIndex}_UNAVAILABLE";
                return false;
            }

            int offset;
            try
            {
                offset = checked(
                    vertexIndex * binding.Stride + binding.Offset);
            }
            catch (OverflowException)
            {
                blocker =
                    $"dest0x{destination:X2}:VERTEX_OFFSET_OVERFLOW";
                return false;
            }
            IReadOnlyList<byte> stream = binding.StreamIndex == 0
                ? surface.Verts0
                : surface.Verts1;
            if (!TryDecodeRsxVertexInput(
                    stream,
                    offset,
                    binding.ComponentCount,
                    binding.RsxType,
                    out Vector4 value,
                    out string decodeBlocker))
            {
                blocker =
                    $"dest0x{destination:X2}:{decodeBlocker}:offset0x{offset:X}";
                return false;
            }
            values[destination] = value;
        }

        return bindings.Count > 0;
    }

    internal static bool TryReadPosition(
        XSurface surface,
        int vertexIndex,
        out Vector3 value)
    {
        return XSurfaceVertexCodec.TryReadPosition(
            surface.Verts0,
            vertexIndex,
            out value) && IsReasonablePosition(value);
    }

    internal bool TryReadTexCoord(
        XSurface surface,
        int vertexIndex,
        out Vector2 value)
    {
        value = default;
        if (_texCoord.IsDisabledDefaultAttribute)
        {
            value = Vector2.Zero;
            return true;
        }

        if (!TryGetVertexOffset(
                vertexIndex,
                _texCoord.Stride,
                _texCoord.Offset,
                out int offset))
        {
            return false;
        }

        IReadOnlyList<byte>? bytes = _texCoord.StreamIndex switch
        {
            0 => surface.Verts0,
            1 => surface.Verts1,
            _ => null
        };
        if (_texCoord.StreamIndex == 1 && _texCoord.Offset == 4 &&
            _texCoord.FormatByte0 == 2 &&
            _texCoord.FormatByte1 == RsxVertexElementType.Float16)
            return XSurfaceVertexCodec.TryReadUv0(surface.Verts1, vertexIndex, out value);
        if (bytes is null ||
            !VertexElementDecoder.TryReadBackendTexCoord(
                bytes,
                offset,
                _texCoord.FormatByte0,
                _texCoord.FormatByte1,
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

    internal bool TryReadColor(
        XSurface surface,
        int vertexIndex,
        out Vector4 value)
    {
        value = Vector4.One;
        if (!TryGetSource(ColorSource, out VertexSource source) ||
            source.FormatByte0 != 4 ||
            source.FormatByte1 != RsxVertexElementType.Unsigned8Normalized ||
            !TryGetVertexOffset(
                vertexIndex,
                source.Stride,
                source.Offset,
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
        if (source.StreamIndex == 1 && source.Offset == 0)
            return XSurfaceVertexCodec.TryReadColor(surface.Verts1, vertexIndex, out value);
        if (bytes is null ||
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

    internal bool TryReadNormal(
        XSurface surface,
        int vertexIndex,
        out Vector3 value)
    {
        value = default;
        return TryGetSource(NormalSource, out VertexSource source) &&
            source.StreamIndex == 1 && source.Offset == 8 &&
            source.FormatByte0 == 1 && source.FormatByte1 == PackedDirectionRsxType &&
            XSurfaceVertexCodec.TryReadNormal(surface.Verts1, vertexIndex, out value);
    }

    internal bool TryReadTangent(
        XSurface surface,
        int vertexIndex,
        out Vector3 value)
    {
        value = default;
        return TryGetSource(TangentSource, out VertexSource source) &&
            source.StreamIndex == 1 && source.Offset == 12 &&
            source.FormatByte0 == 1 && source.FormatByte1 == PackedDirectionRsxType &&
            XSurfaceVertexCodec.TryReadTangent(surface.Verts1, vertexIndex, out value);
    }

    private static bool TryGetSource(
        MaterialStreamSource sourceSlot,
        out VertexSource source)
    {
        source = default;
        if (!WorldVertexLayout.TryGetSource(
                BackendRow,
                sourceSlot,
                out WorldVertexSource backendSource) ||
            !WorldVertexLayout.TryGetStreamStride(
                BackendRow,
                backendSource.StreamIndex,
                out byte stride))
        {
            return false;
        }

        source = new VertexSource(
            backendSource.StreamIndex,
            stride,
            backendSource.ByteOffset,
            backendSource.ComponentCount,
            backendSource.RsxType);
        return true;
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
            source.FormatByte1 != PackedDirectionRsxType ||
            !TryGetVertexOffset(
                vertexIndex,
                source.Stride,
                source.Offset,
                out int offset) ||
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

        return XSurfaceVertexCodec.TryDecodeDirection(
            BinaryPrimitives.ReadUInt32BigEndian(packedBytes),
            out value);
    }

    private static bool TryDecodeRsxVertexInput(
        IReadOnlyList<byte> stream,
        int offset,
        byte componentCount,
        RsxVertexElementType rsxType,
        out Vector4 value,
        out string blocker)
    {
        value = new Vector4(0f, 0f, 0f, 1f);
        blocker = string.Empty;
        int byteCount = rsxType switch
        {
            RsxVertexElementType.Signed16Normalized or
            RsxVertexElementType.Float16 or
            RsxVertexElementType.Signed16Unnormalized => componentCount * 2,
            RsxVertexElementType.Float32 => componentCount * 4,
            RsxVertexElementType.Unsigned8Normalized or
            RsxVertexElementType.Unsigned8Unnormalized => componentCount,
            RsxVertexElementType.Signed11_11_10Normalized => 4,
            _ => 0
        };
        if (byteCount <= 0 || offset < 0)
        {
            blocker = $"TYPE0x{(byte)rsxType:X2}_OR_OFFSET_INVALID";
            return false;
        }
        if (offset > stream.Count - byteCount)
        {
            blocker =
                $"STREAM_RANGE_END0x{offset + byteCount:X}_SIZE0x{stream.Count:X}";
            return false;
        }

        Span<byte> bytes = byteCount <= 16
            ? stackalloc byte[byteCount]
            : new byte[byteCount];
        if (stream is byte[] array)
        {
            array.AsSpan(offset, byteCount).CopyTo(bytes);
        }
        else
        {
            for (int index = 0; index < byteCount; index++)
                bytes[index] = stream[offset + index];
        }

        Span<float> decoded = stackalloc float[4];
        decoded[3] = 1f;
        if (rsxType == RsxVertexElementType.Signed11_11_10Normalized)
        {
            Vector3 decodedPacked = new PackedSigned11_11_10(
                BinaryPrimitives.ReadUInt32BigEndian(bytes))
                .DecodeRsxNormalized();
            decoded[0] = decodedPacked.X;
            decoded[1] = decodedPacked.Y;
            decoded[2] = decodedPacked.Z;
        }
        else
        {
            for (int component = 0;
                 component < componentCount && component < 4;
                 component++)
            {
                decoded[component] = rsxType switch
                {
                    RsxVertexElementType.Signed16Normalized =>
                        (BinaryPrimitives.ReadInt16BigEndian(
                            bytes[(component * 2)..]) + 0.5f) / 32767.5f,
                    RsxVertexElementType.Float32 =>
                        BinaryPrimitives.ReadSingleBigEndian(
                        bytes[(component * 4)..]),
                    RsxVertexElementType.Float16 =>
                        (float)BitConverter.UInt16BitsToHalf(
                        BinaryPrimitives.ReadUInt16BigEndian(
                            bytes[(component * 2)..])),
                    RsxVertexElementType.Unsigned8Normalized =>
                        bytes[component] / 255f,
                    RsxVertexElementType.Signed16Unnormalized =>
                        BinaryPrimitives.ReadInt16BigEndian(
                        bytes[(component * 2)..]),
                    RsxVertexElementType.Unsigned8Unnormalized =>
                        bytes[component],
                    _ => 0f
                };
            }
        }

        value = new Vector4(
            decoded[0], decoded[1], decoded[2], decoded[3]);
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W))
        {
            blocker = "NONFINITE_DECODE";
            return false;
        }

        return true;
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

    private static bool IsReasonablePosition(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        MathF.Abs(value.X) < MaximumReasonableCoordinate &&
        MathF.Abs(value.Y) < MaximumReasonableCoordinate &&
        MathF.Abs(value.Z) < MaximumReasonableCoordinate;

}
