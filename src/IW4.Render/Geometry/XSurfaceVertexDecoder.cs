using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.Geometry;

/// <summary>
/// Decodes the recovered PS3 static-XSurface vertex streams described by
/// backend source-table row 2 (MTL_WORLDVERT_TEX_2_NRM_2).
/// </summary>
internal sealed class XSurfaceVertexDecoder
{
    internal const int RsxVertexInputCount = 16;
    internal const int RsxVertexInputComponentCount = 4;
    internal const int BackendRow =
        (int)MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2;
    internal const byte DefaultTexCoordSourceIndex = 2;

    private const int PositionStride = 0x10;
    private const byte ColorSourceIndex = 1;
    private const byte NormalSourceIndex = 3;
    private const byte TangentSourceIndex = 4;
    private const byte PackedDirectionRsxType = 0x06;
    private const float MaximumReasonableCoordinate = 1_000_000f;

    private readonly VertexSource _texCoord;

    private XSurfaceVertexDecoder(VertexSource texCoord)
    {
        _texCoord = texCoord;
    }

    internal static bool TryCreate(
        byte texCoordSource,
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

    internal static UvRoute CreateUvRoute(byte texCoordSource)
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
            MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2
                .ToString(),
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
            if (binding.Destination >= values.Length)
            {
                blocker = $"dest0x{binding.Destination:X2}:OUT_OF_RANGE";
                return false;
            }
            if (binding.IsDisabledDefaultAttribute)
                continue;
            if (binding.StreamIndex > 1)
            {
                blocker =
                    $"dest0x{binding.Destination:X2}:STREAM{binding.StreamIndex}_UNAVAILABLE";
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
                    $"dest0x{binding.Destination:X2}:VERTEX_OFFSET_OVERFLOW";
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
                    $"dest0x{binding.Destination:X2}:{decodeBlocker}:offset0x{offset:X}";
                return false;
            }
            values[binding.Destination] = value;
        }

        return bindings.Count > 0;
    }

    internal static bool TryReadPosition(
        XSurface surface,
        int vertexIndex,
        out Vector3 value)
    {
        value = default;
        if (!TryGetVertexOffset(
                vertexIndex,
                PositionStride,
                attributeOffset: 0,
                out int offset) ||
            surface.Verts0.Count < 3 * sizeof(float) ||
            offset > surface.Verts0.Count - 3 * sizeof(float))
        {
            return false;
        }

        value = new Vector3(
            VertexElementDecoder.ReadSingleBigEndian(surface.Verts0, offset),
            VertexElementDecoder.ReadSingleBigEndian(
                surface.Verts0,
                offset + sizeof(float)),
            VertexElementDecoder.ReadSingleBigEndian(
                surface.Verts0,
                offset + 2 * sizeof(float)));
        return IsReasonablePosition(value);
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
        if (!TryGetSource(ColorSourceIndex, out VertexSource source) ||
            source.FormatByte0 != 4 ||
            source.FormatByte1 != 0x04 ||
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
        return TryGetSource(NormalSourceIndex, out VertexSource source) &&
            TryReadPackedDirection(surface, vertexIndex, source, out value);
    }

    internal bool TryReadTangent(
        XSurface surface,
        int vertexIndex,
        out Vector3 value)
    {
        value = default;
        return TryGetSource(TangentSourceIndex, out VertexSource source) &&
            TryReadPackedDirection(surface, vertexIndex, source, out value);
    }

    private static bool TryGetSource(
        byte sourceIndex,
        out VertexSource source)
    {
        source = default;
        if (!WorldVertexLayout.TryGetSource(
                BackendRow,
                sourceIndex,
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

        uint packed = BinaryPrimitives.ReadUInt32BigEndian(packedBytes);
        var decoded = new Vector3(
            (SignExtend((int)(packed & 0x7ff), 11) << 5) / 32767f,
            (SignExtend((int)((packed >> 11) & 0x7ff), 11) << 5) / 32767f,
            (SignExtend((int)((packed >> 22) & 0x3ff), 10) << 6) / 32767f);
        return StaticVertexBasisTransformer.TryNormalizeDirection(
            decoded,
            out value);
    }

    private static bool TryDecodeRsxVertexInput(
        IReadOnlyList<byte> stream,
        int offset,
        byte componentCount,
        byte rsxType,
        out Vector4 value,
        out string blocker)
    {
        value = new Vector4(0f, 0f, 0f, 1f);
        blocker = string.Empty;
        int byteCount = rsxType switch
        {
            0x01 or 0x03 or 0x05 => componentCount * 2,
            0x02 => componentCount * 4,
            0x04 or 0x07 => componentCount,
            0x06 => 4,
            _ => 0
        };
        if (byteCount <= 0 || offset < 0)
        {
            blocker = $"TYPE0x{rsxType:X2}_OR_OFFSET_INVALID";
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
        if (rsxType == 0x06)
        {
            uint packed = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            decoded[0] =
                (SignExtend((int)(packed & 0x7ff), 11) << 5) / 32767f;
            decoded[1] =
                (SignExtend((int)((packed >> 11) & 0x7ff), 11) << 5) /
                32767f;
            decoded[2] =
                (SignExtend((int)((packed >> 22) & 0x3ff), 10) << 6) /
                32767f;
        }
        else
        {
            for (int component = 0;
                 component < componentCount && component < 4;
                 component++)
            {
                decoded[component] = rsxType switch
                {
                    0x01 =>
                        (BinaryPrimitives.ReadInt16BigEndian(
                            bytes[(component * 2)..]) + 0.5f) / 32767.5f,
                    0x02 => BinaryPrimitives.ReadSingleBigEndian(
                        bytes[(component * 4)..]),
                    0x03 => (float)BitConverter.UInt16BitsToHalf(
                        BinaryPrimitives.ReadUInt16BigEndian(
                            bytes[(component * 2)..])),
                    0x04 => bytes[component] / 255f,
                    0x05 => BinaryPrimitives.ReadInt16BigEndian(
                        bytes[(component * 2)..]),
                    0x07 => bytes[component],
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

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }
}
