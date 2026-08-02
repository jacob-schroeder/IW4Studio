using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.StructuredData;

public sealed class StructuredDataDefSetLoader
{
    public StructuredDataDefSetAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level StructuredDataDefSet pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataDefSetAsset>(
                pointer,
                StructuredDataDefSetAsset.SerializedSize,
                "StructuredDataDefSet");
            StructuredDataDefSetAsset canonical = context.ResolveStructuredDataDefSet(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level StructuredDataDefSet pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical StructuredDataDef asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed StructuredDataDefSet pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical StructuredDataDefSet has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Top-level StructuredDataDefSet pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                $"has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            StructuredDataDefSetAsset defSet = ReadDefSet(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline StructuredDataDefSet pointer has no destination cell.");
            StructuredDataDefSetAsset canonical = context.DB_AddXAsset(defSet, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical StructuredDataDefSet has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static StructuredDataDefSetAsset ReadDefSet(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, StructuredDataDefSetAsset.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
        {
            throw new InvalidDataException(
                $"StructuredDataDefSet pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");
        }
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        int defCount = rootCursor.ReadInt32();
        XPointer<StructuredDataDef[]> defsPointer = ReadPointer<StructuredDataDef[]>(rootCursor, context);

        if (rootCursor.Offset != StructuredDataDefSetAsset.SerializedSize)
            throw new InvalidDataException($"StructuredDataDefSet consumed 0x{rootCursor.Offset:X} bytes instead of 0x{StructuredDataDefSetAsset.SerializedSize:X}.");


        string? name;
        IReadOnlyList<StructuredDataDef> defs;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            defs = ReadDefArray(cursor, defsPointer.Untyped, defCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new StructuredDataDefSetAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            DefCount = defCount,
            DefsPointer = defsPointer,
            Defs = defs
        };
    }

    private static IReadOnlyList<StructuredDataDef> ReadDefArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataDef count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataDef[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataDef[]>(pointer, checked(count * StructuredDataDef.SerializedSize), "StructuredDataDef[]");
            return context.ResolveMaterializedDirect<StructuredDataDef[]>(pointer, "StructuredDataDef[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataDef.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var defs = new StructuredDataDef[count];
        context.RegisterMaterialized(tableAddress, defs, "StructuredDataDef[]");

        for (int i = 0; i < defs.Length; i++)
        {
            int rowStart = rowCursor.Offset;
            defs[i] = new StructuredDataDef
            {
                Version = rowCursor.ReadInt32(),
                FormatChecksum = rowCursor.ReadUInt32(),
                EnumCount = rowCursor.ReadInt32(),
                EnumsPointer = ReadPointer<StructuredDataEnum[]>(rowCursor, context),
                StructCount = rowCursor.ReadInt32(),
                StructsPointer = ReadPointer<StructuredDataStruct[]>(rowCursor, context),
                IndexedArrayCount = rowCursor.ReadInt32(),
                IndexedArraysPointer = ReadPointer<StructuredDataIndexedArray[]>(rowCursor, context),
                EnumedArrayCount = rowCursor.ReadInt32(),
                EnumedArraysPointer = ReadPointer<StructuredDataEnumedArray[]>(rowCursor, context),
                RootType = ReadStructuredDataType(rowCursor),
                Size = rowCursor.ReadUInt32()
            };

            if (rowCursor.Offset - rowStart != StructuredDataDef.SerializedSize)
                throw new InvalidDataException($"StructuredDataDef consumed 0x{rowCursor.Offset - rowStart:X} bytes instead of 0x{StructuredDataDef.SerializedSize:X}.");
        }

        foreach (StructuredDataDef def in defs)
        {
            def.Enums = ReadEnumArray(cursor, def.EnumsPointer.Untyped, def.EnumCount, context);
            def.Structs = ReadStructArray(cursor, def.StructsPointer.Untyped, def.StructCount, context);
            def.IndexedArrays = ReadIndexedArray(cursor, def.IndexedArraysPointer.Untyped, def.IndexedArrayCount, context);
            def.EnumedArrays = ReadEnumedArray(cursor, def.EnumedArraysPointer.Untyped, def.EnumedArrayCount, context);
        }

        return defs;
    }

    private static IReadOnlyList<StructuredDataEnum> ReadEnumArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataEnum count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataEnum[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataEnum[]>(pointer, checked(count * StructuredDataEnum.SerializedSize), "StructuredDataEnum[]");
            return context.ResolveMaterializedDirect<StructuredDataEnum[]>(pointer, "StructuredDataEnum[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataEnum.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var enums = new StructuredDataEnum[count];
        context.RegisterMaterialized(tableAddress, enums, "StructuredDataEnum[]");

        for (int i = 0; i < enums.Length; i++)
        {
            int rowStart = rowCursor.Offset;
            enums[i] = new StructuredDataEnum
            {
                EntryCount = rowCursor.ReadInt32(),
                ReservedEntryCount = rowCursor.ReadInt32(),
                EntriesPointer = ReadPointer<StructuredDataEnumEntry[]>(rowCursor, context)
            };

            if (rowCursor.Offset - rowStart != StructuredDataEnum.SerializedSize)
                throw new InvalidDataException($"StructuredDataEnum consumed 0x{rowCursor.Offset - rowStart:X} bytes instead of 0x{StructuredDataEnum.SerializedSize:X}.");
        }

        foreach (StructuredDataEnum value in enums)
            value.Entries = ReadEnumEntryArray(cursor, value.EntriesPointer.Untyped, value.EntryCount, context);

        return enums;
    }

    private static IReadOnlyList<StructuredDataEnumEntry> ReadEnumEntryArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataEnumEntry count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataEnumEntry[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataEnumEntry[]>(pointer, checked(count * StructuredDataEnumEntry.SerializedSize), "StructuredDataEnumEntry[]");
            return context.ResolveMaterializedDirect<StructuredDataEnumEntry[]>(pointer, "StructuredDataEnumEntry[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataEnumEntry.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var entries = new StructuredDataEnumEntry[count];
        context.RegisterMaterialized(tableAddress, entries, "StructuredDataEnumEntry[]");

        for (int i = 0; i < entries.Length; i++)
        {
            int rowStart = rowCursor.Offset;
            entries[i] = new StructuredDataEnumEntry
            {
                StringPointer = ReadXStringPointer(rowCursor, context),
                Index = rowCursor.ReadUInt16(),
                Padding = rowCursor.ReadUInt16()
            };

            if (rowCursor.Offset - rowStart != StructuredDataEnumEntry.SerializedSize)
                throw new InvalidDataException($"StructuredDataEnumEntry consumed 0x{rowCursor.Offset - rowStart:X} bytes instead of 0x{StructuredDataEnumEntry.SerializedSize:X}.");
        }

        for (int i = 0; i < entries.Length; i++)
        {
            string? value = context.PointerReader.LoadXString(cursor, entries[i].StringPointer);
            entries[i] = new StructuredDataEnumEntry
            {
                StringPointer = entries[i].StringPointer,
                String = value,
                Index = entries[i].Index,
                Padding = entries[i].Padding
            };
        }

        return entries;
    }

    private static IReadOnlyList<StructuredDataStruct> ReadStructArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataStruct count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataStruct[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataStruct[]>(pointer, checked(count * StructuredDataStruct.SerializedSize), "StructuredDataStruct[]");
            return context.ResolveMaterializedDirect<StructuredDataStruct[]>(pointer, "StructuredDataStruct[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataStruct.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var structs = new StructuredDataStruct[count];
        context.RegisterMaterialized(tableAddress, structs, "StructuredDataStruct[]");

        for (int i = 0; i < structs.Length; i++)
        {
            int rowStart = rowCursor.Offset;
            structs[i] = new StructuredDataStruct
            {
                PropertyCount = rowCursor.ReadInt32(),
                PropertiesPointer = ReadPointer<StructuredDataStructProperty[]>(rowCursor, context),
                Size = rowCursor.ReadInt32(),
                BitOffset = rowCursor.ReadUInt32()
            };

            if (rowCursor.Offset - rowStart != StructuredDataStruct.SerializedSize)
                throw new InvalidDataException($"StructuredDataStruct consumed 0x{rowCursor.Offset - rowStart:X} bytes instead of 0x{StructuredDataStruct.SerializedSize:X}.");
        }

        foreach (StructuredDataStruct value in structs)
            value.Properties = ReadStructPropertyArray(cursor, value.PropertiesPointer.Untyped, value.PropertyCount, context);

        return structs;
    }

    private static IReadOnlyList<StructuredDataStructProperty> ReadStructPropertyArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataStructProperty count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataStructProperty[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataStructProperty[]>(pointer, checked(count * StructuredDataStructProperty.SerializedSize), "StructuredDataStructProperty[]");
            return context.ResolveMaterializedDirect<StructuredDataStructProperty[]>(pointer, "StructuredDataStructProperty[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataStructProperty.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var properties = new StructuredDataStructProperty[count];
        context.RegisterMaterialized(tableAddress, properties, "StructuredDataStructProperty[]");

        for (int i = 0; i < properties.Length; i++)
        {
            int rowStart = rowCursor.Offset;
            properties[i] = new StructuredDataStructProperty
            {
                NamePointer = ReadXStringPointer(rowCursor, context),
                Type = ReadStructuredDataType(rowCursor),
                Offset = rowCursor.ReadUInt32()
            };

            if (rowCursor.Offset - rowStart != StructuredDataStructProperty.SerializedSize)
                throw new InvalidDataException($"StructuredDataStructProperty consumed 0x{rowCursor.Offset - rowStart:X} bytes instead of 0x{StructuredDataStructProperty.SerializedSize:X}.");
        }

        for (int i = 0; i < properties.Length; i++)
        {
            string? name = context.PointerReader.LoadXString(cursor, properties[i].NamePointer);
            properties[i] = new StructuredDataStructProperty
            {
                NamePointer = properties[i].NamePointer,
                Name = name,
                Type = properties[i].Type,
                Offset = properties[i].Offset
            };
        }

        return properties;
    }

    private static IReadOnlyList<StructuredDataIndexedArray> ReadIndexedArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataIndexedArray count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataIndexedArray[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataIndexedArray[]>(pointer, checked(count * StructuredDataIndexedArray.SerializedSize), "StructuredDataIndexedArray[]");
            return context.ResolveMaterializedDirect<StructuredDataIndexedArray[]>(pointer, "StructuredDataIndexedArray[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataIndexedArray.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var values = new StructuredDataIndexedArray[count];
        context.RegisterMaterialized(tableAddress, values, "StructuredDataIndexedArray[]");

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new StructuredDataIndexedArray
            {
                ArraySize = rowCursor.ReadInt32(),
                ElementType = ReadStructuredDataType(rowCursor),
                ElementSize = rowCursor.ReadUInt32()
            };
        }

        return values;
    }

    private static IReadOnlyList<StructuredDataEnumedArray> ReadEnumedArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StructuredDataEnumedArray count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0) return [];
            throw new InvalidDataException($"StructuredDataEnumedArray[] has count {count}, but its pointer is null.");
        }
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<StructuredDataEnumedArray[]>(pointer, checked(count * StructuredDataEnumedArray.SerializedSize), "StructuredDataEnumedArray[]");
            return context.ResolveMaterializedDirect<StructuredDataEnumedArray[]>(pointer, "StructuredDataEnumedArray[]");
        }

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * StructuredDataEnumedArray.SerializedSize), out XBlockAddress tableAddress);
        var rowCursor = new FastFileCursor(bytes, tableAddress);
        var values = new StructuredDataEnumedArray[count];
        context.RegisterMaterialized(tableAddress, values, "StructuredDataEnumedArray[]");

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new StructuredDataEnumedArray
            {
                EnumIndex = rowCursor.ReadInt32(),
                ElementType = ReadStructuredDataType(rowCursor),
                ElementSize = rowCursor.ReadUInt32()
            };
        }

        return values;
    }

    private static StructuredDataType ReadStructuredDataType(FastFileCursor cursor)
    {
        return new StructuredDataType
        {
            Type = (StructuredDataTypeCategory)cursor.ReadInt32(),
            UnionValue = cursor.ReadInt32()
        };
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<T>(cursor, XPointerResolutionMode.Direct);
    }

    private static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }

    private static void AlignStream(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        int alignment)
    {
        context.Blocks.AlignCurrent(alignment);
    }
}
