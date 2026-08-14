using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;

namespace IW4.Linker.Plans;

internal static class MaterialShaderVertexReservation
{
    private const int HeaderSize = 0x20;
    private const int ParameterSize = 0x30;
    private const int DescriptorSize = 0x18;

    public static LinkStorageSymbol Create(
        MaterialShaderKind kind,
        ReadOnlySpan<byte> bytecode,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        try
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

            return kind switch
            {
                MaterialShaderKind.Vertex => CreateVertex(
                    bytecode,
                    parameterCount,
                    parameterTableOffset,
                    descriptorOffset,
                    uploadSize,
                    fieldPath),
                MaterialShaderKind.Pixel => CreatePixel(
                    bytecode,
                    parameterCount,
                    parameterTableOffset,
                    descriptorOffset,
                    uploadSize,
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
        int parameterCount,
        int parameterTableOffset,
        int descriptorOffset,
        int uploadSize,
        string fieldPath)
    {
        int instructionCount = checked((int)ReadUInt32(
            bytecode,
            descriptorOffset));
        int instructionBytes = checked(instructionCount * 0x10);
        if (instructionBytes > uploadSize)
        {
            throw new InvalidDataException(
                $"{fieldPath} descriptor declares 0x{instructionBytes:X} instruction bytes, " +
                $"but its upload payload contains 0x{uploadSize:X} bytes.");
        }

        int defaultWords = 0;
        for (int index = 0; index < parameterCount; index++)
        {
            int parameterOffset = checked(
                parameterTableOffset + checked(index * ParameterSize));
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
            defaultWords = checked(
                defaultWords + GetDefaultWordCount(
                    bytecode,
                    parameterCount,
                    parameterTableOffset,
                    index,
                    parameterOffset,
                    fieldPath));
        }

        int quotient = instructionCount >> 3;
        int remainder = instructionCount & 7;
        int commandWordCount = checked(
            7 +
            checked(33 * quotient) +
            (remainder == 0 ? 0 : checked(1 + 4 * remainder)) +
            defaultWords);
        int allocationSize = checked(
            sizeof(uint) * checked(commandWordCount + 1));
        return VertexReservation(allocationSize);
    }

    private static LinkStorageSymbol CreatePixel(
        ReadOnlySpan<byte> bytecode,
        int parameterCount,
        int parameterTableOffset,
        int descriptorOffset,
        int uploadSize,
        string fieldPath)
    {
        ushort mask = ReadUInt16(bytecode, descriptorOffset + 0x0c);
        int programSize = checked(
            20 + 8 * BitOperations.PopCount((uint)mask));

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
            programSize,
            alignment: sizeof(uint),
            LinkMaterializationKind.VertexReservation,
            _ =>
            [
                new MaterializeStorageLinkOperation(
                    patches,
                    $"{fieldPath}.PatchTables")
            ]);
    }

    private static int GetDefaultWordCount(
        ReadOnlySpan<byte> bytecode,
        int parameterCount,
        int parameterTableOffset,
        int parameterIndex,
        int parameterOffset,
        string fieldPath)
    {
        if (ReadUInt32(bytecode, parameterOffset + 0x04) == 0xCB8u)
            return 0;

        uint type = ReadUInt32(bytecode, parameterOffset);
        if (type is 0x415u or 0x416u or 0x417u or 0x418u or 0x443u)
        {
            return ReadUInt32(bytecode, parameterOffset + 0x0c) == uint.MaxValue
                ? 0
                : 6;
        }

        int childCount = type switch
        {
            0x423u or 0x424u => 3,
            0x427u or 0x428u => 4,
            _ => 0
        };
        if (childCount == 0)
            return 0;

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

        if (type == 0x428u && validChildCount == 4)
            return 18;
        return checked(6 * validChildCount);
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

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, sizeof(ushort)));
}
