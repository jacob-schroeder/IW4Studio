using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Localize;

public sealed class LocalizeLoader
{
    public LocalizeAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level Localize pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<LocalizeAsset>(
                pointer,
                LocalizeAsset.SerializedSize,
                "Localize");
            LocalizeAsset canonical = context.ResolveLocalize(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level Localize pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Localize asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed Localize pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical Localize has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException(
                $"Top-level Localize pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            LocalizeAsset localize = ReadLocalize(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline Localize pointer has no destination cell.");
            LocalizeAsset canonical = context.DB_AddXAsset(localize, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical Localize has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The root is staged in TEMP; both XStrings are materialized in LARGE
    // before registration copies the header.
    private static LocalizeAsset ReadLocalize(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, LocalizeAsset.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"Localize pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> valuePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != LocalizeAsset.SerializedSize)
            throw new InvalidDataException($"Localize consumed 0x{rootCursor.Offset:X} bytes instead of 0x{LocalizeAsset.SerializedSize:X}.");


        string? value;
        string? name;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            value = context.PointerReader.LoadXString(cursor, valuePointer);
            name = context.PointerReader.LoadXString(cursor, namePointer);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new LocalizeAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            ValuePointer = valuePointer,
            Value = value,
            NamePointer = namePointer,
            Name = name
        };
    }

}
