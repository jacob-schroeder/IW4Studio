using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.RawFile;

public sealed class RawFileLoader
{
    public RawFileAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level RawFile pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<RawFileAsset>(
                pointer,
                RawFileAsset.SerializedSize,
                "RawFile");
            RawFileAsset canonical = context.ResolveRawFile(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level RawFile pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical RawFile asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed RawFile pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical RawFile has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException(
                $"Top-level RawFile pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            RawFileAsset rawFile = ReadRawFile(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline RawFile pointer has no destination cell.");
            RawFileAsset canonical = context.DB_AddXAsset(rawFile, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical RawFile has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The root is staged in TEMP; its name and buffer payload are materialized
    // in LARGE before registration.
    private static RawFileAsset ReadRawFile(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, RawFileAsset.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"RawFile pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        int compressedLen = rootCursor.ReadInt32();
        int len = rootCursor.ReadInt32();
        int bufferCellOffset = rootCursor.Offset;
        var bufferPointer = new XPointer<byte[]>(
            rootCursor.ReadInt32(),
            XPointerResolutionMode.Direct,
            rootCursor.AddressAt(bufferCellOffset));

        if (rootCursor.Offset != RawFileAsset.SerializedSize)
            throw new InvalidDataException($"RawFile consumed 0x{rootCursor.Offset:X} bytes instead of 0x{RawFileAsset.SerializedSize:X}.");

        int bufferLength = compressedLen != 0 ? compressedLen : checked(len + 1);

        string? name;
        byte[]? buffer;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            buffer = LoadRawFileBuffer(cursor, bufferPointer, bufferLength, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new RawFileAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            CompressedLen = compressedLen,
            Len = len,
            BufferPointer = bufferPointer,
            Buffer = buffer
        };
    }

    // Every nonzero serialized value owns the computed byte count and is
    // replaced with the current LARGE address; it is not a packed offset.
    private static byte[]? LoadRawFileBuffer(
        FastFileCursor cursor,
        XPointer<byte[]> pointer,
        int byteCount,
        DbLoadExecutionContext context)
    {
        if (pointer.Raw == 0)
            return null;

        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("RawFile buffer pointer has no destination cell.");
        XBlockAddress bufferAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(pointerCellAddress, XPointerCodec.Encode(bufferAddress));
        return context.Blocks.Load(cursor, byteCount);
    }
}
