using System.Buffers.Binary;
using System.Text;

namespace IW4.Render.Shaders;

/// <summary>
/// Decodes compiler-owned vertex constants from a PS3 Cg binary program. The
/// parameter resource index and the microcode's nine-bit constant source both
/// identify c0..c467.
/// </summary>
internal static class RsxVertexProgramEmbeddedConstantDecoder
{
    private const uint VertexProfile = 0x1b5b;
    private const uint ConstantRegisterResource = 0x0882;
    private const uint ConstantVariability = 0x1007;
    private const int ProgramHeaderSize = 0x20;
    private const int ParameterSize = 0x30;

    public static RsxVertexProgramEmbeddedConstantDecodeResult Decode(
        ReadOnlySpan<byte> data)
    {
        var constants = new List<EmbeddedVertexConstant>();
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        if (data.Length < ProgramHeaderSize ||
            BinaryPrimitives.ReadUInt32BigEndian(data) != VertexProfile)
        {
            return new([], []);
        }

        uint totalSize = ReadUInt32(data, 0x08);
        uint parameterCount = ReadUInt32(data, 0x0c);
        uint parameterArray = ReadUInt32(data, 0x10);
        if (totalSize < ProgramHeaderSize || totalSize > data.Length)
        {
            blockers.Add("vertexEmbeddedConstantTable=invalidProgramSize");
            return new([], blockers.ToArray());
        }
        if (parameterCount > ushort.MaxValue ||
            (ulong)parameterArray + parameterCount * ParameterSize > totalSize)
        {
            blockers.Add("vertexEmbeddedConstantTable=invalidParameterRange");
            return new([], blockers.ToArray());
        }

        var destinations = new HashSet<ushort>();
        for (int ordinal = 0; ordinal < (int)parameterCount; ordinal++)
        {
            int parameterOffset = checked((int)parameterArray + ordinal * ParameterSize);
            uint resource = ReadUInt32(data, parameterOffset + 0x04);
            uint variability = ReadUInt32(data, parameterOffset + 0x08);
            if (variability != ConstantVariability)
                continue;

            uint rawResourceIndex = ReadUInt32(data, parameterOffset + 0x0c);
            uint nameOffset = ReadUInt32(data, parameterOffset + 0x10);
            uint defaultValueOffset = ReadUInt32(data, parameterOffset + 0x14);
            uint embeddedConstantOffset = ReadUInt32(data, parameterOffset + 0x18);
            uint isReferenced = ReadUInt32(data, parameterOffset + 0x28);
            if (isReferenced == 0)
                continue;

            string prefix = $"vertexEmbeddedConstantParam{ordinal}";
            if (isReferenced != 1)
            {
                blockers.Add($"{prefix}=invalidReferencedFlag0x{isReferenced:X8}");
                continue;
            }
            if (resource != ConstantRegisterResource)
            {
                blockers.Add($"{prefix}=unsupportedResource0x{resource:X4}");
                continue;
            }
            if (rawResourceIndex >= RsxVertexConstantLayout.Count)
            {
                blockers.Add($"{prefix}=unsupportedResourceIndex0x{rawResourceIndex:X8}");
                continue;
            }
            if (defaultValueOffset == 0 ||
                (ulong)defaultValueOffset + 16 > totalSize ||
                (defaultValueOffset & 3) != 0)
            {
                blockers.Add($"{prefix}=invalidDefaultValueRange");
                continue;
            }
            if (embeddedConstantOffset != 0)
            {
                blockers.Add($"{prefix}=unsupportedVertexPatchList");
                continue;
            }
            if (!TryReadCString(data[..(int)totalSize], nameOffset, out string name))
            {
                blockers.Add($"{prefix}=invalidName");
                continue;
            }

            ushort destination = checked((ushort)rawResourceIndex);
            int valueOffset = checked((int)defaultValueOffset);
            var value = new ShaderConstantValue(
                ReadSingle(data, valueOffset),
                ReadSingle(data, valueOffset + 4),
                ReadSingle(data, valueOffset + 8),
                ReadSingle(data, valueOffset + 12));
            if (!IsFinite(value))
            {
                blockers.Add($"{prefix}=invalidNonFiniteDefault");
                continue;
            }
            if (!destinations.Add(destination))
            {
                blockers.Add($"vertexEmbeddedConstantDest{destination}=ambiguous");
                continue;
            }

            constants.Add(new EmbeddedVertexConstant(
                ordinal,
                destination,
                rawResourceIndex,
                name,
                defaultValueOffset,
                value,
                IsOperationallyResolved: true));
        }

        return new(
            constants.OrderBy(constant => constant.Destination).ToArray(),
            blockers.ToArray());
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(data, offset)));

    private static bool TryReadCString(
        ReadOnlySpan<byte> data,
        uint rawOffset,
        out string value)
    {
        value = string.Empty;
        if (rawOffset == 0 || rawOffset >= data.Length)
            return false;

        ReadOnlySpan<byte> tail = data[(int)rawOffset..];
        int terminator = tail.IndexOf((byte)0);
        if (terminator <= 0)
            return false;
        ReadOnlySpan<byte> encoded = tail[..terminator];
        foreach (byte character in encoded)
        {
            if (character is < 0x20 or > 0x7e)
                return false;
        }

        value = Encoding.ASCII.GetString(encoded);
        return true;
    }

    private static bool IsFinite(ShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

internal sealed record RsxVertexProgramEmbeddedConstantDecodeResult(
    IReadOnlyList<EmbeddedVertexConstant> Constants,
    IReadOnlyList<string> Blockers);
