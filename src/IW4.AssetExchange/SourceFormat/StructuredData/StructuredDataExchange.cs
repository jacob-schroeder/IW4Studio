using System.Buffers.Binary;
using System.Text;
using IW4.Assets.Assets.StructuredData;

namespace IW4.AssetExchange.SourceFormat.StructuredData;

/// <summary>
/// Writes IW4 structured-data definition sets in the OpenAssetTools source
/// grammar, including its source checksum compatibility behavior.
/// </summary>
public sealed class StructuredDataExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        StructuredDataDefSetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "StructuredDataDefSet");
        StructuredDataSourceWriter.Validate(asset, assetName);

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            (
                assetName,
                writer => StructuredDataSourceWriter.Write(
                    writer,
                    asset))
        ]);
    }
}

internal static class StructuredDataSourceWriter
{
    private const int IndentWidth = 2;

    public static void Validate(
        StructuredDataDefSetAsset asset,
        string assetName)
    {
        if (asset.DefCount < 0 || asset.DefCount != asset.Defs.Count)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet '{assetName}' declares {asset.DefCount} definitions but contains {asset.Defs.Count}.");
        }

        for (int defIndex = 0; defIndex < asset.Defs.Count; defIndex++)
        {
            StructuredDataDef def = asset.Defs[defIndex] ??
                throw new InvalidDataException(
                    $"StructuredDataDefSet '{assetName}' definition {defIndex} is null.");
            string context = $"StructuredDataDefSet '{assetName}' definition {defIndex}";
            ValidateCount(def.EnumCount, def.Enums.Count, context, "enums");
            ValidateCount(def.StructCount, def.Structs.Count, context, "structs");
            ValidateCount(
                def.IndexedArrayCount,
                def.IndexedArrays.Count,
                context,
                "indexed arrays");
            ValidateCount(
                def.EnumedArrayCount,
                def.EnumedArrays.Count,
                context,
                "enum arrays");

            for (int enumIndex = 0; enumIndex < def.Enums.Count; enumIndex++)
            {
                StructuredDataEnum value = def.Enums[enumIndex] ??
                    throw new InvalidDataException(
                        $"{context} enum {enumIndex} is null.");
                ValidateCount(
                    value.EntryCount,
                    value.Entries.Count,
                    $"{context} enum {enumIndex}",
                    "entries");
                for (int entryIndex = 0;
                     entryIndex < value.Entries.Count;
                     entryIndex++)
                {
                    StructuredDataEnumEntry entry = value.Entries[entryIndex] ??
                        throw new InvalidDataException(
                            $"{context} enum {enumIndex} entry {entryIndex} is null.");
                    ValidateQuotedText(
                        entry.String,
                        $"{context} enum {enumIndex} entry {entryIndex}");
                }
            }

            for (int structIndex = 0;
                 structIndex < def.Structs.Count;
                 structIndex++)
            {
                StructuredDataStruct value = def.Structs[structIndex] ??
                    throw new InvalidDataException(
                        $"{context} struct {structIndex} is null.");
                if (value.Size < 0)
                {
                    throw new InvalidDataException(
                        $"{context} struct {structIndex} has negative size {value.Size}.");
                }
                ValidateCount(
                    value.PropertyCount,
                    value.Properties.Count,
                    $"{context} struct {structIndex}",
                    "properties");
                for (int propertyIndex = 0;
                     propertyIndex < value.Properties.Count;
                     propertyIndex++)
                {
                    StructuredDataStructProperty property =
                        value.Properties[propertyIndex] ??
                        throw new InvalidDataException(
                            $"{context} struct {structIndex} property {propertyIndex} is null.");
                    ValidateIdentifier(
                        property.Name,
                        $"{context} struct {structIndex} property {propertyIndex}");
                    ValidateType(
                        def,
                        property.Type,
                        $"{context} struct {structIndex} property '{property.Name}'",
                        []);
                }
            }

            for (int arrayIndex = 0;
                 arrayIndex < def.IndexedArrays.Count;
                 arrayIndex++)
            {
                StructuredDataIndexedArray array =
                    def.IndexedArrays[arrayIndex] ??
                    throw new InvalidDataException(
                        $"{context} indexed array {arrayIndex} is null.");
                if (array.ArraySize < 0)
                {
                    throw new InvalidDataException(
                        $"{context} indexed array {arrayIndex} has negative element count {array.ArraySize}.");
                }
                ValidateType(
                    def,
                    array.ElementType,
                    $"{context} indexed array {arrayIndex} element",
                    [(StructuredDataTypeCategory.DataIndexedArray, arrayIndex)]);
            }

            for (int arrayIndex = 0;
                 arrayIndex < def.EnumedArrays.Count;
                 arrayIndex++)
            {
                StructuredDataEnumedArray array =
                    def.EnumedArrays[arrayIndex] ??
                    throw new InvalidDataException(
                        $"{context} enum array {arrayIndex} is null.");
                if (array.EnumIndex < 0 || array.EnumIndex >= def.Enums.Count)
                {
                    throw new InvalidDataException(
                        $"{context} enum array {arrayIndex} references enum {array.EnumIndex}, outside 0..{def.Enums.Count - 1}.");
                }
                ValidateType(
                    def,
                    array.ElementType,
                    $"{context} enum array {arrayIndex} element",
                    [(StructuredDataTypeCategory.DataEnumArray, arrayIndex)]);
            }

            ValidateType(def, def.RootType, $"{context} root", []);
            ValidateLayouts(def, context);
        }
    }

    public static void Write(
        TextWriter writer,
        StructuredDataDefSetAsset asset)
    {
        for (int index = 0; index < asset.Defs.Count; index++)
        {
            if (index != 0)
                writer.Write("\n\n");
            WriteDef(writer, asset.Defs[index]);
        }
    }

    private static void WriteDef(
        TextWriter writer,
        StructuredDataDef def)
    {
        uint calculatedChecksum = CalculateChecksum(def);
        bool checksumMismatch = calculatedChecksum != def.FormatChecksum;

        writer.WriteLine("// ====================");
        writer.WriteLine($"// Version {def.Version}");
        if (checksumMismatch)
        {
            writer.WriteLine("// Calculated checksum did not match checksum in file");
            writer.WriteLine("// Overriding checksum to match original value");
        }
        writer.WriteLine("// ====================");
        writer.WriteLine($"version {def.Version}");
        writer.WriteLine("{");

        bool insertEmptyLine = false;
        if (checksumMismatch)
        {
            WriteIndent(writer, 1);
            writer.WriteLine($"checksumoverride {def.FormatChecksum};");
            insertEmptyLine = true;
        }

        for (int index = 0; index < def.Enums.Count; index++)
        {
            if (insertEmptyLine)
                writer.WriteLine();
            else
                insertEmptyLine = true;
            WriteEnum(writer, def.Enums[index], index);
        }

        for (int index = 0; index < def.Structs.Count; index++)
        {
            if (insertEmptyLine)
                writer.WriteLine();
            else
                insertEmptyLine = true;
            WriteStruct(writer, def, def.Structs[index], index);
        }

        writer.WriteLine("}");
    }

    private static void WriteEnum(
        TextWriter writer,
        StructuredDataEnum value,
        int enumIndex)
    {
        WriteIndent(writer, 1);
        if (value.ReservedEntryCount > value.Entries.Count)
            writer.Write($"enum({value.ReservedEntryCount}) ");
        else
            writer.Write("enum ");
        writer.WriteLine(GetEnumName(enumIndex));
        WriteIndent(writer, 1);
        writer.WriteLine("{");

        StructuredDataEnumEntry[] entries = value.Entries
            .OrderBy(entry => entry.Index)
            .ToArray();
        for (int index = 0; index < entries.Length; index++)
        {
            WriteIndent(writer, 2);
            writer.Write('"');
            SourceText.WriteQuotedContent(writer, entries[index].String!);
            writer.Write('"');
            if (index + 1 < entries.Length)
                writer.Write(',');
            writer.WriteLine();
        }

        WriteIndent(writer, 1);
        writer.WriteLine("};");
    }

    private static void WriteStruct(
        TextWriter writer,
        StructuredDataDef def,
        StructuredDataStruct value,
        int structIndex)
    {
        WriteIndent(writer, 1);
        writer.Write("struct ");
        writer.WriteLine(GetStructName(def, structIndex));
        WriteIndent(writer, 1);
        writer.WriteLine("{");

        ulong currentOffset = IsRootStruct(def, structIndex) ? 64UL : 0UL;
        foreach (StructuredDataStructProperty property in
                 value.Properties.OrderBy(property => GetPropertyOffset(property)))
        {
            ulong propertyOffset = GetPropertyOffset(property);
            currentOffset = property.Type.Type == StructuredDataTypeCategory.DataBool
                ? currentOffset
                : Align(currentOffset, 8);
            if (currentOffset < propertyOffset)
            {
                WriteIndent(writer, 2);
                writer.WriteLine($"pad({(propertyOffset - currentOffset) / 8});");
                currentOffset = propertyOffset;
            }

            WriteIndent(writer, 2);
            writer.Write(GetTypeSource(def, property.Type));
            writer.Write(' ');
            writer.Write(property.Name);
            writer.WriteLine(';');
            currentOffset = checked(currentOffset + GetTypeSizeInBits(def, property.Type));
        }

        currentOffset = Align(currentOffset, 8);
        ulong sizeInBytes = GetStructSizeInBytes(def, value, structIndex);
        if (currentOffset / 8 < sizeInBytes)
        {
            WriteIndent(writer, 2);
            writer.WriteLine($"pad({sizeInBytes - currentOffset / 8});");
        }

        WriteIndent(writer, 1);
        writer.WriteLine("};");
    }

    private static string GetTypeSource(
        StructuredDataDef def,
        StructuredDataType initialType)
    {
        var arrays = new List<string>();
        StructuredDataType type = initialType;
        int remaining = checked(def.IndexedArrays.Count + def.EnumedArrays.Count + 1);
        while (remaining-- > 0)
        {
            string? typeName = type.Type switch
            {
                StructuredDataTypeCategory.DataInt => "int",
                StructuredDataTypeCategory.DataByte => "byte",
                StructuredDataTypeCategory.DataBool => "bool",
                StructuredDataTypeCategory.DataFloat => "float",
                StructuredDataTypeCategory.DataShort => "short",
                StructuredDataTypeCategory.DataString =>
                    $"string({type.UnionValue})",
                StructuredDataTypeCategory.DataEnum =>
                    GetEnumName(type.UnionValue),
                StructuredDataTypeCategory.DataStruct =>
                    GetStructName(def, type.UnionValue),
                _ => null
            };
            if (typeName is not null)
                return typeName + string.Concat(arrays);

            if (type.Type == StructuredDataTypeCategory.DataIndexedArray)
            {
                StructuredDataIndexedArray array =
                    def.IndexedArrays[type.UnionValue];
                arrays.Add($"[{array.ArraySize}]");
                type = array.ElementType;
                continue;
            }
            if (type.Type == StructuredDataTypeCategory.DataEnumArray)
            {
                StructuredDataEnumedArray array =
                    def.EnumedArrays[type.UnionValue];
                arrays.Add($"[{GetEnumName(array.EnumIndex)}]");
                type = array.ElementType;
                continue;
            }

            break;
        }

        throw new InvalidDataException(
            "Structured-data type contains a cyclic array definition.");
    }

    private static void ValidateLayouts(
        StructuredDataDef def,
        string context)
    {
        for (int structIndex = 0;
             structIndex < def.Structs.Count;
             structIndex++)
        {
            StructuredDataStruct value = def.Structs[structIndex];
            ulong currentOffset = IsRootStruct(def, structIndex) ? 64UL : 0UL;
            foreach (StructuredDataStructProperty property in
                     value.Properties.OrderBy(property => GetPropertyOffset(property)))
            {
                ulong propertyOffset = GetPropertyOffset(property);
                currentOffset = property.Type.Type == StructuredDataTypeCategory.DataBool
                    ? currentOffset
                    : Align(currentOffset, 8);
                if (currentOffset > propertyOffset)
                {
                    throw new InvalidDataException(
                        $"{context} struct {structIndex} property '{property.Name}' overlaps its preceding layout.");
                }
                if ((propertyOffset - currentOffset) % 8 != 0)
                {
                    throw new InvalidDataException(
                        $"{context} struct {structIndex} property '{property.Name}' requires a non-byte padding gap.");
                }
                currentOffset = checked(
                    propertyOffset + GetTypeSizeInBits(def, property.Type));
            }

            currentOffset = Align(currentOffset, 8);
            ulong declaredBits = checked(
                GetStructSizeInBytes(def, value, structIndex) * 8);
            if (currentOffset > declaredBits)
            {
                throw new InvalidDataException(
                    $"{context} struct {structIndex} layout consumes {currentOffset} bits but declares {declaredBits}.");
            }
        }
    }

    private static void ValidateType(
        StructuredDataDef def,
        StructuredDataType? type,
        string context,
        HashSet<(StructuredDataTypeCategory Type, int Index)> activeArrays)
    {
        if (type is null)
            throw new InvalidDataException($"{context} type is null.");

        int count = type.Type switch
        {
            StructuredDataTypeCategory.DataInt => int.MaxValue,
            StructuredDataTypeCategory.DataByte => int.MaxValue,
            StructuredDataTypeCategory.DataBool => int.MaxValue,
            StructuredDataTypeCategory.DataFloat => int.MaxValue,
            StructuredDataTypeCategory.DataShort => int.MaxValue,
            StructuredDataTypeCategory.DataString when type.UnionValue < 0 => -1,
            StructuredDataTypeCategory.DataString => int.MaxValue,
            StructuredDataTypeCategory.DataEnum => def.Enums.Count,
            StructuredDataTypeCategory.DataStruct => def.Structs.Count,
            StructuredDataTypeCategory.DataIndexedArray => def.IndexedArrays.Count,
            StructuredDataTypeCategory.DataEnumArray => def.EnumedArrays.Count,
            _ => -1
        };
        if (count == -1 ||
            count != int.MaxValue &&
            (type.UnionValue < 0 || type.UnionValue >= count))
        {
            throw new InvalidDataException(
                $"{context} has invalid {type.Type} value {type.UnionValue}.");
        }

        if (type.Type is not (
                StructuredDataTypeCategory.DataIndexedArray or
                StructuredDataTypeCategory.DataEnumArray))
        {
            return;
        }

        var key = (type.Type, type.UnionValue);
        if (!activeArrays.Add(key))
        {
            throw new InvalidDataException(
                $"{context} contains a cyclic array type.");
        }
        StructuredDataType elementType;
        if (type.Type == StructuredDataTypeCategory.DataIndexedArray)
        {
            StructuredDataIndexedArray array =
                def.IndexedArrays[type.UnionValue] ??
                throw new InvalidDataException(
                    $"{context} references a null indexed array.");
            elementType = array.ElementType;
        }
        else
        {
            StructuredDataEnumedArray array =
                def.EnumedArrays[type.UnionValue] ??
                throw new InvalidDataException(
                    $"{context} references a null enum array.");
            elementType = array.ElementType;
        }
        ValidateType(def, elementType, context, activeArrays);
        activeArrays.Remove(key);
    }

    private static ulong GetPropertyOffset(
        StructuredDataStructProperty property) =>
        property.Type.Type == StructuredDataTypeCategory.DataBool
            ? property.Offset
            : checked((ulong)property.Offset * 8);

    private static ulong GetTypeSizeInBits(
        StructuredDataDef def,
        StructuredDataType type) => type.Type switch
    {
        StructuredDataTypeCategory.DataInt => 32,
        StructuredDataTypeCategory.DataByte => 8,
        StructuredDataTypeCategory.DataBool => 1,
        StructuredDataTypeCategory.DataFloat => 32,
        StructuredDataTypeCategory.DataShort => 16,
        StructuredDataTypeCategory.DataString => checked((ulong)type.UnionValue * 8),
        StructuredDataTypeCategory.DataEnum => 16,
        StructuredDataTypeCategory.DataStruct => checked(
            GetStructSizeInBytes(
                def,
                def.Structs[type.UnionValue],
                type.UnionValue) * 8),
        StructuredDataTypeCategory.DataIndexedArray =>
            GetIndexedArraySizeInBits(def.IndexedArrays[type.UnionValue]),
        StructuredDataTypeCategory.DataEnumArray =>
            GetEnumArraySizeInBits(def, def.EnumedArrays[type.UnionValue]),
        _ => throw new InvalidDataException(
            $"Unsupported structured-data type {type.Type}.")
    };

    private static ulong GetIndexedArraySizeInBits(
        StructuredDataIndexedArray array)
    {
        ulong elementSize = array.ElementType.Type ==
            StructuredDataTypeCategory.DataBool
                ? 1UL
                : checked((ulong)array.ElementSize * 8);
        return Align(checked(elementSize * (ulong)array.ArraySize), 8);
    }

    private static ulong GetEnumArraySizeInBits(
        StructuredDataDef def,
        StructuredDataEnumedArray array)
    {
        ulong elementSize = array.ElementType.Type ==
            StructuredDataTypeCategory.DataBool
                ? 1UL
                : checked((ulong)array.ElementSize * 8);
        return Align(
            checked(elementSize * GetEnumElementCount(def.Enums[array.EnumIndex])),
            8);
    }

    private static ulong GetStructSizeInBytes(
        StructuredDataDef def,
        StructuredDataStruct value,
        int structIndex) =>
        IsRootStruct(def, structIndex) ? def.Size : (ulong)value.Size;

    private static bool IsRootStruct(
        StructuredDataDef def,
        int structIndex) =>
        def.RootType.Type == StructuredDataTypeCategory.DataStruct &&
        def.RootType.UnionValue == structIndex;

    private static string GetStructName(
        StructuredDataDef def,
        int structIndex) =>
        IsRootStruct(def, structIndex)
            ? "root"
            : $"STRUCT_{structIndex}";

    private static string GetEnumName(int enumIndex) => $"ENUM_{enumIndex}";

    private static ulong GetEnumElementCount(StructuredDataEnum value) =>
        value.ReservedEntryCount > 0
            ? (ulong)value.ReservedEntryCount
            : (ulong)value.Entries.Count;

    private static uint CalculateChecksum(StructuredDataDef def)
    {
        uint checksum = 0;
        for (int enumIndex = 0; enumIndex < def.Enums.Count; enumIndex++)
        {
            StructuredDataEnum value = def.Enums[enumIndex];
            checksum = UpdateString(checksum, GetEnumName(enumIndex));
            checksum = UpdateSize(checksum, GetEnumElementCount(value));
            foreach (StructuredDataEnumEntry entry in
                     value.Entries.OrderBy(entry => entry.Index))
            {
                checksum = UpdateString(checksum, entry.String!);
                checksum = UpdateSize(checksum, entry.Index);
            }
        }

        for (int structIndex = 0;
             structIndex < def.Structs.Count;
             structIndex++)
        {
            checksum = UpdateString(
                checksum,
                GetStructName(def, structIndex));
            foreach (StructuredDataStructProperty property in
                     def.Structs[structIndex].Properties
                         .OrderBy(property => GetPropertyOffset(property)))
            {
                checksum = UpdateString(checksum, property.Name!);
                checksum = UpdateSize(
                    checksum,
                    GetPropertyOffset(property));
                checksum = UpdateTypeChecksum(
                    checksum,
                    def,
                    property.Type);
            }
        }

        return checksum;
    }

    private static uint UpdateTypeChecksum(
        uint checksum,
        StructuredDataDef def,
        StructuredDataType initialType)
    {
        StructuredDataType type = initialType;
        int remaining = checked(def.IndexedArrays.Count + def.EnumedArrays.Count + 1);
        while (remaining-- > 0)
        {
            checksum = UpdateByte(checksum, GetCommonCategory(type.Type));
            switch (type.Type)
            {
                case StructuredDataTypeCategory.DataString:
                    return UpdateSize(checksum, (ulong)type.UnionValue);
                case StructuredDataTypeCategory.DataEnum:
                    return UpdateString(checksum, GetEnumName(type.UnionValue));
                case StructuredDataTypeCategory.DataStruct:
                    return UpdateString(
                        checksum,
                        GetStructName(def, type.UnionValue));
                case StructuredDataTypeCategory.DataIndexedArray:
                {
                    StructuredDataIndexedArray array =
                        def.IndexedArrays[type.UnionValue];
                    checksum = UpdateSize(checksum, (ulong)array.ArraySize);
                    type = array.ElementType;
                    continue;
                }
                case StructuredDataTypeCategory.DataEnumArray:
                {
                    StructuredDataEnumedArray array =
                        def.EnumedArrays[type.UnionValue];
                    checksum = UpdateString(
                        checksum,
                        GetEnumName(array.EnumIndex));
                    type = array.ElementType;
                    continue;
                }
                default:
                    return checksum;
            }
        }

        throw new InvalidDataException(
            "Structured-data type contains a cyclic array definition.");
    }

    private static byte GetCommonCategory(
        StructuredDataTypeCategory category) => category switch
    {
        StructuredDataTypeCategory.DataInt => 1,
        StructuredDataTypeCategory.DataByte => 2,
        StructuredDataTypeCategory.DataBool => 3,
        StructuredDataTypeCategory.DataFloat => 4,
        StructuredDataTypeCategory.DataShort => 5,
        StructuredDataTypeCategory.DataString => 6,
        StructuredDataTypeCategory.DataEnum => 7,
        StructuredDataTypeCategory.DataStruct => 8,
        StructuredDataTypeCategory.DataIndexedArray => 9,
        StructuredDataTypeCategory.DataEnumArray => 10,
        _ => throw new InvalidDataException(
            $"Unsupported structured-data type {category}.")
    };

    private static uint UpdateString(uint checksum, string value)
    {
        checksum = UpdateBytes(checksum, Encoding.UTF8.GetBytes(value));
        return UpdateByte(checksum, 0);
    }

    private static uint UpdateSize(uint checksum, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return UpdateBytes(checksum, bytes);
    }

    private static uint UpdateByte(uint checksum, byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        return UpdateBytes(checksum, bytes);
    }

    private static uint UpdateBytes(uint checksum, ReadOnlySpan<byte> bytes)
    {
        uint value = ~checksum;
        foreach (byte item in bytes)
        {
            value ^= item;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^
                    ((value & 1) != 0 ? 0xEDB88320u : 0u);
            }
        }
        return ~value;
    }

    private static ulong Align(ulong value, ulong alignment)
    {
        if (alignment == 0)
            return value;
        return checked((value + alignment - 1) / alignment * alignment);
    }

    private static void ValidateCount(
        int declared,
        int actual,
        string context,
        string itemName)
    {
        if (declared < 0 || declared != actual)
        {
            throw new InvalidDataException(
                $"{context} declares {declared} {itemName} but contains {actual}.");
        }
    }

    private static void ValidateIdentifier(string? value, string context)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Contains('\0') ||
            value.Contains('\r') ||
            value.Contains('\n'))
        {
            throw new InvalidDataException(
                $"{context} has no valid source identifier.");
        }
    }

    private static void ValidateQuotedText(string? value, string context)
    {
        if (value is null || value.Contains('\0'))
        {
            throw new InvalidDataException(
                $"{context} has no valid string value.");
        }
    }

    private static void WriteIndent(TextWriter writer, int level) =>
        writer.Write(new string(' ', checked(level * IndentWidth)));
}
