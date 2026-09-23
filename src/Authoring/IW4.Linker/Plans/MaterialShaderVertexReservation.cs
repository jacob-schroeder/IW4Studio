using System.Buffers.Binary;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;

namespace IW4.Linker.Plans;

public static class MaterialShaderVertexReservation
{
    private const int HeaderSize = 0x20;
    private const int ParameterSize = 0x30;
    private const int DescriptorSize = 0x18;
    private const int PixelCommandReservationSize = 0x48;

    internal static LinkStorageSymbol Create(
        MaterialShaderKind kind,
        ReadOnlySpan<byte> bytecode,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        try
        {
            CgProgramLayout layout = ReadLayout(bytecode, fieldPath);

            return kind switch
            {
                MaterialShaderKind.Vertex => CreateVertex(bytecode, layout, fieldPath),
                MaterialShaderKind.Pixel => CreatePixel(
                    bytecode,
                    layout.ParameterCount,
                    layout.ParameterTableOffset,
                    layout.UploadSize,
                    fieldPath),
                _ => throw new InvalidDataException(
                    $"{fieldPath} has unsupported shader kind {kind}.")
            };
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains a count or offset outside the supported range.",
                exception);
        }
    }

    private static LinkStorageSymbol CreateVertex(
        ReadOnlySpan<byte> bytecode,
        CgProgramLayout layout,
        string fieldPath) =>
        VertexReservation(CalculateCommandMetrics(bytecode, layout, fieldPath).CommandBytes);

    public static MaterialShaderVertexCommandMetrics CalculateCommandMetrics(
        ReadOnlySpan<byte> bytecode,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        try
        {
            return CalculateCommandMetrics(bytecode, ReadLayout(bytecode, fieldPath), fieldPath);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains a count or offset outside the supported range.",
                exception);
        }
    }

    private static MaterialShaderVertexCommandMetrics CalculateCommandMetrics(
        ReadOnlySpan<byte> bytecode,
        CgProgramLayout layout,
        string fieldPath)
    {
        int instructionCount = checked((int)ReadUInt32(bytecode, layout.DescriptorOffset));
        int instructionBytes = checked(instructionCount * 0x10);
        if (instructionBytes > layout.UploadSize)
        {
            throw new InvalidDataException(
                $"{fieldPath} descriptor declares 0x{instructionBytes:X} instruction bytes, " +
                $"but its upload payload contains 0x{layout.UploadSize:X} bytes.");
        }

        MaterialShaderVertexDefaultMetrics defaults = default;
        for (int index = 0; index < layout.ParameterCount; index++)
        {
            int parameterOffset = checked(
                layout.ParameterTableOffset + checked(index * ParameterSize));
            uint defaultOffset = ReadUInt32(bytecode, parameterOffset + 0x14);
            uint variability = ReadUInt32(bytecode, parameterOffset + 0x08);
            if (defaultOffset == 0 || variability is not (0x1006u or 0x1007u))
                continue;

            RequireRange(
                bytecode,
                checked((int)defaultOffset),
                0x10,
                fieldPath,
                $"parameter[{index}] default value");
            MaterialShaderVertexDefaultMetrics parameterDefaults = GetDefaultMetrics(
                bytecode,
                layout.ParameterCount,
                layout.ParameterTableOffset,
                index,
                parameterOffset,
                fieldPath);
            defaults = new MaterialShaderVertexDefaultMetrics(
                checked(defaults.VectorCount + parameterDefaults.VectorCount),
                checked(defaults.CommandWords + parameterDefaults.CommandWords));
        }

        // Three start words, four words per instruction and one header per group
        // of up to eight, four end words, and the builder's final RETURN word.
        int instructionHeaderCount = checked((instructionCount + 7) / 8);
        int commandWordCount = checked(
            8 + instructionBytes / sizeof(uint) + instructionHeaderCount + defaults.CommandWords);
        return new MaterialShaderVertexCommandMetrics(
            instructionCount,
            checked((int)ReadUInt32(bytecode, layout.DescriptorOffset + 8)),
            defaults.VectorCount,
            defaults.CommandWords,
            checked(commandWordCount * sizeof(uint)));
    }

    private static LinkStorageSymbol CreatePixel(
        ReadOnlySpan<byte> bytecode,
        int parameterCount,
        int parameterTableOffset,
        int uploadSize,
        string fieldPath)
    {
        int patchCount = 0;
        for (int index = 0; index < parameterCount; index++)
        {
            int parameterOffset = checked(
                parameterTableOffset + checked(index * ParameterSize));
            uint patchListOffsetValue = ReadUInt32(
                bytecode,
                parameterOffset + 0x18);
            if (patchListOffsetValue == 0)
                continue;

            int patchListOffset = checked((int)patchListOffsetValue);
            RequireRange(
                bytecode,
                patchListOffset,
                sizeof(uint),
                fieldPath,
                $"parameter[{index}] patch-list count");
            int parameterPatchCount = checked((int)ReadUInt32(
                bytecode,
                patchListOffset));
            RequireRange(
                bytecode,
                patchListOffset,
                checked(sizeof(uint) + checked(parameterPatchCount * sizeof(uint))),
                fieldPath,
                $"parameter[{index}] patch list");

            for (int patchIndex = 0; patchIndex < parameterPatchCount; patchIndex++)
            {
                uint entry = ReadUInt32(
                    bytecode,
                    checked(
                        patchListOffset + sizeof(uint) +
                        checked(patchIndex * sizeof(uint))));
                if (entry > ushort.MaxValue)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} parameter[{index}] patch-list entry " +
                        $"[{patchIndex}] exceeds 0x{ushort.MaxValue:X4}.");
                }
                if ((ulong)entry + 0x10UL > (ulong)uploadSize)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} parameter[{index}] patch-list entry " +
                        $"[{patchIndex}] exceeds its 0x{uploadSize:X}-byte upload payload.");
                }
            }

            patchCount = checked(patchCount + parameterPatchCount);
        }

        int patchStorageSize = checked(
            checked(parameterCount * sizeof(uint)) +
            checked(patchCount * sizeof(ushort)));
        LinkStorageSymbol upload = VertexReservation(uploadSize);
        LinkStorageSymbol patches = LinkStorageSymbol.SourceFree(
            XFileBlockType.VERTEX,
            patchStorageSize,
            alignment: sizeof(uint),
            LinkMaterializationKind.VertexReservation,
            _ =>
            [
                new MaterializeStorageLinkOperation(
                    upload,
                    $"{fieldPath}.Upload")
            ]);
        return LinkStorageSymbol.SourceFree(
            XFileBlockType.VERTEX,
            PixelCommandReservationSize,
            alignment: sizeof(uint),
            LinkMaterializationKind.VertexReservation,
            _ =>
            [
                new MaterializeStorageLinkOperation(
                    patches,
                    $"{fieldPath}.PatchTables")
            ]);
    }

    private static MaterialShaderVertexDefaultMetrics GetDefaultMetrics(
        ReadOnlySpan<byte> bytecode,
        int parameterCount,
        int parameterTableOffset,
        int parameterIndex,
        int parameterOffset,
        string fieldPath)
    {
        if (ReadUInt32(bytecode, parameterOffset + 0x04) == 0xCB8u)
            return default;

        uint type = ReadUInt32(bytecode, parameterOffset);
        if (type is 0x415u or 0x416u or 0x417u or 0x418u or 0x443u)
        {
            return ReadUInt32(bytecode, parameterOffset + 0x0c) == uint.MaxValue
                ? default
                : new MaterialShaderVertexDefaultMetrics(1, 6);
        }

        int childCount = type switch
        {
            0x423u or 0x424u => 3,
            0x427u or 0x428u => 4,
            _ => 0
        };
        if (childCount == 0)
            return default;

        int lastChildIndex = checked(parameterIndex + childCount);
        if (lastChildIndex >= parameterCount)
        {
            throw new InvalidDataException(
                $"{fieldPath} parameter[{parameterIndex}] type 0x{type:X} " +
                $"requires {childCount} following parameter record(s).");
        }

        int validChildCount = 0;
        for (int childIndex = parameterIndex + 1;
             childIndex <= lastChildIndex;
             childIndex++)
        {
            int childOffset = checked(
                parameterTableOffset + checked(childIndex * ParameterSize));
            if (ReadUInt32(bytecode, childOffset + 0x04) != 0xCB8u &&
                ReadUInt32(bytecode, childOffset + 0x0c) != uint.MaxValue)
            {
                validChildCount++;
            }
        }

        return new MaterialShaderVertexDefaultMetrics(
            validChildCount,
            type == 0x428u && validChildCount == 4 ? 18 : checked(6 * validChildCount));
    }

    private static CgProgramLayout ReadLayout(ReadOnlySpan<byte> bytecode, string fieldPath)
    {
        if (bytecode.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"{fieldPath} requires at least 0x{HeaderSize:X} bytes.");
        }

        int parameterCount = checked((int)ReadUInt32(bytecode, 0x0c));
        int parameterTableOffset = checked((int)ReadUInt32(bytecode, 0x10));
        int descriptorOffset = checked((int)ReadUInt32(bytecode, 0x14));
        int uploadSize = checked((int)ReadUInt32(bytecode, 0x18));
        int uploadOffset = checked((int)ReadUInt32(bytecode, 0x1c));

        RequireRange(
            bytecode,
            parameterTableOffset,
            checked(parameterCount * ParameterSize),
            fieldPath,
            "parameter table");
        RequireRange(
            bytecode,
            descriptorOffset,
            DescriptorSize,
            fieldPath,
            "descriptor");
        RequireRange(
            bytecode,
            uploadOffset,
            uploadSize,
            fieldPath,
            "upload payload");
        return new CgProgramLayout(parameterCount, parameterTableOffset, descriptorOffset, uploadSize);
    }

    private static LinkStorageSymbol VertexReservation(int byteLength) =>
        LinkStorageSymbol.SourceFree(
            XFileBlockType.VERTEX,
            byteLength,
            alignment: sizeof(uint),
            LinkMaterializationKind.VertexReservation);

    private static void RequireRange(
        ReadOnlySpan<byte> bytecode,
        int offset,
        int length,
        string fieldPath,
        string description)
    {
        int end = checked(offset + length);
        if (offset < 0 || length < 0 || end > bytecode.Length)
        {
            throw new InvalidDataException(
                $"{fieldPath} {description} range 0x{offset:X}..0x{end:X} " +
                $"exceeds its 0x{bytecode.Length:X}-byte payload.");
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset, sizeof(uint)));

    private readonly record struct CgProgramLayout(
        int ParameterCount,
        int ParameterTableOffset,
        int DescriptorOffset,
        int UploadSize);

    private readonly record struct MaterialShaderVertexDefaultMetrics(
        int VectorCount,
        int CommandWords);
}

public readonly record struct MaterialShaderVertexCommandMetrics(
    int InstructionCount,
    int TemporaryRegisterCount,
    int DefaultVectorCount,
    int DefaultCommandWords,
    int CommandBytes);
