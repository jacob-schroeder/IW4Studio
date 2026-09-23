using IW4.Loaders.Database;
using IW4.Game.Assets.Localize;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Localize;

public sealed class LocalizeLoader : XAssetLoader<LocalizeAsset>
{
    public LocalizeLoader() : base(XAssetType.Localize, LocalizeAsset.SerializedSize, "Localize")
    {
    }

    protected override LocalizeAsset RegisterAsset(
        LocalizeAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    // The root is staged in TEMP; both XStrings are materialized in LARGE
    // before registration copies the header.
    protected override LocalizeAsset ReadBody(
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
