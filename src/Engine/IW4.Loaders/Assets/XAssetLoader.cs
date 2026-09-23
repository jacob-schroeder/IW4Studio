using IW4.Game.Assets;
using IW4.Game.IO;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using IW4.Runtime.Database;

namespace IW4.Loaders.Assets;

/// <summary>
/// Loads XAsset references while keeping body decoding and specialized canonical
/// registration in the owning asset loader. Nested data pointers have separate
/// loading rules and do not use this provider-registration path.
/// </summary>
public abstract class XAssetLoader<TAsset>
    where TAsset : BaseAsset
{
    protected XAssetLoader(XAssetType assetType, int serializedSize, string assetName)
    {
        AssetType = assetType;
        SerializedSize = serializedSize;
        AssetName = assetName;
    }

    protected XAssetType AssetType { get; }
    protected int SerializedSize { get; }
    protected string AssetName { get; }
    protected virtual bool ValidatePackedPointerRange => true;

    public TAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadPointer(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException($"Top-level {AssetName} pointer resolved to null.");
    }

    public TAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadPointer(cursor, pointer, context, requireAsset: false);
    }

    private TAsset? LoadPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        switch (pointer.Type)
        {
            case PointerType.Null:
                if (requireAsset)
                    throw new InvalidDataException($"Top-level {AssetName} pointer is null.");
                return null;
            case PointerType.Offset:
                return ResolvePackedPointer(pointer, context, requireAsset);
            case PointerType.Inline:
            case PointerType.Insert:
                return LoadInline(cursor, pointer, context);
            default:
                throw new InvalidDataException(
                    $"{AssetName} pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }
    }

    protected virtual TAsset? ResolvePackedPointer(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (ValidatePackedPointerRange)
            context.PointerReader.ValidateOffsetPointerRange<TAsset>(pointer, SerializedSize, AssetName);
        TAsset? canonical = context.ResolveCanonicalAsset<TAsset>(pointer, AssetType);
        if (canonical is null)
            return HandleUnresolvedReference(pointer, context, requireAsset);

        context.PatchCanonicalAssetPointerCell(
            pointer,
            canonical,
            $"Packed {AssetName} pointer has no destination cell.",
            $"Canonical {AssetName} has no runtime address.");
        return canonical;
    }

    // A nullable source pointer does not imply that an unresolved non-null
    // reference is valid. Only families that permit it override this policy.
    protected virtual TAsset? HandleUnresolvedReference(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        throw new InvalidDataException(
            $"{AssetName} pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical {AssetName} asset.");
    }

    protected int? ResolveAndPatchAliasCell(
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type != PointerType.Offset || pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            return null;

        if (pointer.CellAddress is not { } destinationCell)
            throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} has no destination cell to patch.");

        int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
        if (aliasedRaw != 0)
        {
            if (XPointerCodec.GetType(aliasedRaw) != PointerType.Offset)
                throw new InvalidDataException(
                    $"Alias-cell pointer 0x{pointer.Raw:X8} resolved to unresolved sentinel 0x{aliasedRaw:X8} for {AssetName}.");

            context.PointerReader.ValidateOffsetPointerRange<TAsset>(
                XPointerReference.FromRaw(aliasedRaw, XPointerResolutionMode.Direct, pointer.PackedAddress),
                SerializedSize,
                AssetName);
        }

        context.Blocks.WriteInt32(destinationCell, aliasedRaw);
        return aliasedRaw;
    }

    protected virtual TAsset LoadInline(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        // Capture the source occurrence and reserve any durable insert cell
        // before nested TEMP scopes can reuse its physical address.
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            TAsset asset = ReadInlineBody(cursor, pointer, context);
            return RegisterAsset(asset, providerRegistration, context);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    protected TAsset ReadInlineBody(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        return ReadBody(cursor, rootAddress, context);
    }

    protected abstract TAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context);

    protected virtual TAsset RegisterAsset(
        TAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(AssetType, asset.SerializedAssetName, asset, providerRegistration);
    }
}
