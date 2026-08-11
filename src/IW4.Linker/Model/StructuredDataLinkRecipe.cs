using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen StructuredDataDefSet graph. Every counted table is prewritten before
/// its children, matching the native array walkers without retaining pointers.
/// </summary>
internal sealed class StructuredDataLinkRecipe : AssetLinkRecipe
{
    private StructuredDataLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        StructuredDataDefSetAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? definitions = CreateDefinitions(definition.Defs, freeze);
        var writer = new LinkTemplateWriter(StructuredDataDefSetAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.DefCount);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => definitions is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    Direct(
                        root,
                        0x08,
                        definitions,
                        "StructuredDataDefSet.Defs")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        StructuredDataDefSetAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<StructuredDataDef> definitions = definition.Defs ??
            throw new InvalidDataException("StructuredDataDefSet.Defs cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.DefCount != 0 || definitions.Count != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed StructuredDataDef provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.StructuredDataDef,
                originalSerializedName,
                freeze);
        }

        RequireCount(definition.DefCount, definitions.Count, "StructuredDataDefSet.DefCount");
        for (int index = 0; index < definitions.Count; index++)
            ValidateDefinition(definitions[index], index);
        return new StructuredDataLinkRecipe(key, originalSerializedName, definition, freeze);
    }

    private static LinkStorageSymbol? CreateDefinitions(
        IReadOnlyList<StructuredDataDef> definitions,
        LinkAssetFreezeScope freeze)
    {
        if (definitions.Count == 0)
            return null;

        var enums = new LinkStorageSymbol?[definitions.Count];
        var structs = new LinkStorageSymbol?[definitions.Count];
        var indexedArrays = new LinkStorageSymbol?[definitions.Count];
        var enumedArrays = new LinkStorageSymbol?[definitions.Count];
        var writer = new LinkTemplateWriter(
            checked(definitions.Count * StructuredDataDef.SerializedSize));
        for (int index = 0; index < definitions.Count; index++)
        {
            StructuredDataDef definition = definitions[index];
            enums[index] = CreateEnums(definition.Enums, index, freeze);
            structs[index] = CreateStructs(definition.Structs, index, freeze);
            indexedArrays[index] = CreateIndexedArrays(definition.IndexedArrays);
            enumedArrays[index] = CreateEnumedArrays(definition.EnumedArrays);

            writer.WriteInt32(definition.Version);
            writer.WriteUInt32(definition.FormatChecksum);
            writer.WriteInt32(definition.EnumCount);
            writer.Skip(sizeof(int));
            writer.WriteInt32(definition.StructCount);
            writer.Skip(sizeof(int));
            writer.WriteInt32(definition.IndexedArrayCount);
            writer.Skip(sizeof(int));
            writer.WriteInt32(definition.EnumedArrayCount);
            writer.Skip(sizeof(int));
            WriteType(writer, definition.RootType);
            writer.WriteUInt32(definition.Size);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => CreateDefinitionOperations(
                table,
                enums,
                structs,
                indexedArrays,
                enumedArrays));
    }

    private static IEnumerable<LinkOperation> CreateDefinitionOperations(
        LinkStorageSymbol table,
        IReadOnlyList<LinkStorageSymbol?> enums,
        IReadOnlyList<LinkStorageSymbol?> structs,
        IReadOnlyList<LinkStorageSymbol?> indexedArrays,
        IReadOnlyList<LinkStorageSymbol?> enumedArrays)
    {
        for (int index = 0; index < enums.Count; index++)
        {
            int row = checked(index * StructuredDataDef.SerializedSize);
            if (enums[index] is { } enumTable)
            {
                yield return Direct(
                    table,
                    checked(row + 0x0c),
                    enumTable,
                    $"StructuredDataDefSet.Defs[{index}].Enums");
            }
            if (structs[index] is { } structTable)
            {
                yield return Direct(
                    table,
                    checked(row + 0x14),
                    structTable,
                    $"StructuredDataDefSet.Defs[{index}].Structs");
            }
            if (indexedArrays[index] is { } indexedTable)
            {
                yield return Direct(
                    table,
                    checked(row + 0x1c),
                    indexedTable,
                    $"StructuredDataDefSet.Defs[{index}].IndexedArrays");
            }
            if (enumedArrays[index] is { } enumedTable)
            {
                yield return Direct(
                    table,
                    checked(row + 0x24),
                    enumedTable,
                    $"StructuredDataDefSet.Defs[{index}].EnumedArrays");
            }
        }
    }

    private static LinkStorageSymbol? CreateEnums(
        IReadOnlyList<StructuredDataEnum> values,
        int definitionIndex,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;

        var entries = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataEnum.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataEnum value = values[index];
            entries[index] = CreateEnumEntries(
                value.Entries,
                definitionIndex,
                index,
                freeze);
            writer.WriteInt32(value.EntryCount);
            writer.WriteInt32(value.ReservedEntryCount);
            writer.Skip(sizeof(int));
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => entries
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => Direct(
                    table,
                    checked(item.index * StructuredDataEnum.SerializedSize + 0x08),
                    item.storage!,
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{item.index}].Entries")));
    }

    private static LinkStorageSymbol? CreateEnumEntries(
        IReadOnlyList<StructuredDataEnumEntry> values,
        int definitionIndex,
        int enumIndex,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;

        var strings = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataEnumEntry.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataEnumEntry value = values[index];
            strings[index] = freeze.FreezeOptionalXString(
                value.String,
                value.StringPointer.Untyped,
                $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{enumIndex}].Entries[{index}].String");
            writer.Skip(sizeof(int));
            writer.WriteUInt16(value.Index);
            writer.WriteUInt16(value.Padding);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => strings
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => XStringOperation(
                    table,
                    checked(item.index * StructuredDataEnumEntry.SerializedSize),
                    item.storage!,
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{enumIndex}].Entries[{item.index}].String")));
    }

    private static LinkStorageSymbol? CreateStructs(
        IReadOnlyList<StructuredDataStruct> values,
        int definitionIndex,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;

        var properties = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataStruct.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataStruct value = values[index];
            properties[index] = CreateProperties(
                value.Properties,
                definitionIndex,
                index,
                freeze);
            writer.WriteInt32(value.PropertyCount);
            writer.Skip(sizeof(int));
            writer.WriteInt32(value.Size);
            writer.WriteUInt32(value.BitOffset);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => properties
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => Direct(
                    table,
                    checked(item.index * StructuredDataStruct.SerializedSize + 0x04),
                    item.storage!,
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{item.index}].Properties")));
    }

    private static LinkStorageSymbol? CreateProperties(
        IReadOnlyList<StructuredDataStructProperty> values,
        int definitionIndex,
        int structIndex,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;

        var names = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataStructProperty.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataStructProperty value = values[index];
            names[index] = freeze.FreezeOptionalXString(
                value.Name,
                value.NamePointer.Untyped,
                $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{structIndex}].Properties[{index}].Name");
            writer.Skip(sizeof(int));
            WriteType(writer, value.Type);
            writer.WriteUInt32(value.Offset);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => names
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => XStringOperation(
                    table,
                    checked(item.index * StructuredDataStructProperty.SerializedSize),
                    item.storage!,
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{structIndex}].Properties[{item.index}].Name")));
    }

    private static LinkStorageSymbol? CreateIndexedArrays(
        IReadOnlyList<StructuredDataIndexedArray> values)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataIndexedArray.SerializedSize));
        foreach (StructuredDataIndexedArray value in values)
        {
            writer.WriteInt32(value.ArraySize);
            WriteType(writer, value.ElementType);
            writer.WriteUInt32(value.ElementSize);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static LinkStorageSymbol? CreateEnumedArrays(
        IReadOnlyList<StructuredDataEnumedArray> values)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(
            checked(values.Count * StructuredDataEnumedArray.SerializedSize));
        foreach (StructuredDataEnumedArray value in values)
        {
            writer.WriteInt32(value.EnumIndex);
            WriteType(writer, value.ElementType);
            writer.WriteUInt32(value.ElementSize);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static void ValidateDefinition(
        StructuredDataDef? definition,
        int definitionIndex)
    {
        if (definition is null)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}] cannot be null.");
        }
        ValidateEnums(definition.Enums, definition.EnumCount, definitionIndex);
        ValidateStructs(
            definition,
            definition.Structs,
            definition.StructCount,
            definitionIndex);
        ValidateIndexedArrays(
            definition,
            definition.IndexedArrays,
            definition.IndexedArrayCount,
            definitionIndex);
        ValidateEnumedArrays(
            definition,
            definition.EnumedArrays,
            definition.EnumedArrayCount,
            definitionIndex);
        ValidateType(
            definition.RootType,
            definition,
            $"StructuredDataDefSet.Defs[{definitionIndex}].RootType");
    }

    private static void ValidateEnums(
        IReadOnlyList<StructuredDataEnum>? values,
        int declaredCount,
        int definitionIndex)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].Enums cannot be null.");
        }
        RequireCount(
            declaredCount,
            values.Count,
            $"StructuredDataDefSet.Defs[{definitionIndex}].EnumCount");
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataEnum value = values[index] ?? throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}] cannot be null.");
            IReadOnlyList<StructuredDataEnumEntry> entries = value.Entries ??
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}].Entries cannot be null.");
            RequireCount(
                value.EntryCount,
                entries.Count,
                $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}].EntryCount");
            if (value.ReservedEntryCount < value.EntryCount)
            {
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}].ReservedEntryCount cannot be below EntryCount.");
            }
            if (entries.Select(entry => entry.Index).Distinct().Count() != entries.Count)
            {
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}] entry indices must be unique.");
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                if (entries[entryIndex] is null)
                {
                    throw new InvalidDataException(
                        $"StructuredDataDefSet.Defs[{definitionIndex}].Enums[{index}].Entries[{entryIndex}] cannot be null.");
                }
            }
        }
    }

    private static void ValidateStructs(
        StructuredDataDef definition,
        IReadOnlyList<StructuredDataStruct>? values,
        int declaredCount,
        int definitionIndex)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].Structs cannot be null.");
        }
        RequireCount(
            declaredCount,
            values.Count,
            $"StructuredDataDefSet.Defs[{definitionIndex}].StructCount");
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataStruct value = values[index] ?? throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}] cannot be null.");
            if (value.Size < 0)
            {
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}].Size cannot be negative.");
            }
            IReadOnlyList<StructuredDataStructProperty> properties = value.Properties ??
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}].Properties cannot be null.");
            RequireCount(
                value.PropertyCount,
                properties.Count,
                $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}].PropertyCount");
            for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
            {
                StructuredDataStructProperty property = properties[propertyIndex] ??
                    throw new InvalidDataException(
                        $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}].Properties[{propertyIndex}] cannot be null.");
                ValidateType(
                    property.Type,
                    definition,
                    $"StructuredDataDefSet.Defs[{definitionIndex}].Structs[{index}].Properties[{propertyIndex}].Type");
            }
        }
    }

    private static void ValidateIndexedArrays(
        StructuredDataDef definition,
        IReadOnlyList<StructuredDataIndexedArray>? values,
        int declaredCount,
        int definitionIndex)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].IndexedArrays cannot be null.");
        }
        RequireCount(
            declaredCount,
            values.Count,
            $"StructuredDataDefSet.Defs[{definitionIndex}].IndexedArrayCount");
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataIndexedArray value = values[index] ?? throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].IndexedArrays[{index}] cannot be null.");
            if (value.ArraySize < 0)
            {
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].IndexedArrays[{index}].ArraySize cannot be negative.");
            }
            ValidateType(
                value.ElementType,
                definition,
                $"StructuredDataDefSet.Defs[{definitionIndex}].IndexedArrays[{index}].ElementType");
        }
    }

    private static void ValidateEnumedArrays(
        StructuredDataDef definition,
        IReadOnlyList<StructuredDataEnumedArray>? values,
        int declaredCount,
        int definitionIndex)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].EnumedArrays cannot be null.");
        }
        RequireCount(
            declaredCount,
            values.Count,
            $"StructuredDataDefSet.Defs[{definitionIndex}].EnumedArrayCount");
        for (int index = 0; index < values.Count; index++)
        {
            StructuredDataEnumedArray value = values[index] ?? throw new InvalidDataException(
                $"StructuredDataDefSet.Defs[{definitionIndex}].EnumedArrays[{index}] cannot be null.");
            if (value.EnumIndex < 0 || value.EnumIndex >= definition.Enums.Count)
            {
                throw new InvalidDataException(
                    $"StructuredDataDefSet.Defs[{definitionIndex}].EnumedArrays[{index}].EnumIndex is outside the enum table.");
            }
            ValidateType(
                value.ElementType,
                definition,
                $"StructuredDataDefSet.Defs[{definitionIndex}].EnumedArrays[{index}].ElementType");
        }
    }

    private static void ValidateType(
        StructuredDataType? value,
        StructuredDataDef? definition,
        string fieldPath)
    {
        if (value is null)
            throw new InvalidDataException($"{fieldPath} cannot be null.");
        if (!Enum.IsDefined(value.Type))
            throw new InvalidDataException($"{fieldPath} has undefined category {value.Type}.");
        if (definition is null)
            return;

        int limit = value.Type switch
        {
            StructuredDataTypeCategory.DataEnum => definition.Enums.Count,
            StructuredDataTypeCategory.DataStruct => definition.Structs.Count,
            StructuredDataTypeCategory.DataIndexedArray => definition.IndexedArrays.Count,
            StructuredDataTypeCategory.DataEnumArray => definition.EnumedArrays.Count,
            _ => -1
        };
        if (limit >= 0 && (value.UnionValue < 0 || value.UnionValue >= limit))
        {
            throw new InvalidDataException(
                $"{fieldPath}.UnionValue is outside the referenced definition table.");
        }
    }

    private static void WriteType(
        LinkTemplateWriter writer,
        StructuredDataType value)
    {
        writer.WriteInt32((int)value.Type);
        writer.WriteInt32(value.UnionValue);
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(target),
            CanMaterializeRoot: true,
            fieldPath);

    private static void RequireCount(int declared, int actual, string fieldPath)
    {
        if (declared < 0 || declared != actual)
        {
            throw new InvalidDataException(
                $"{fieldPath} ({declared}) must equal its semantic element count ({actual}).");
        }
    }
}
