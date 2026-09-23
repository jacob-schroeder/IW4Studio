using IW4.Loaders.Database;
using IW4.Game.Assets.RawFile;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.RawFile;

public sealed class RawFileLoader : XAssetLoader<RawFileAsset>
{
    public RawFileLoader() : base(XAssetType.RawFile, RawFileAsset.SerializedSize, "RawFile")
    {
    }

    protected override RawFileAsset RegisterAsset(
        RawFileAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    // The root is staged in TEMP; its name and buffer payload are materialized
    // in LARGE before registration.
    protected override RawFileAsset ReadBody(
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
        XPointer<byte[]> bufferPointer = context.PointerReader.ReadPointer<byte[]>(
            rootCursor,
            XPointerResolutionMode.Direct);

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
