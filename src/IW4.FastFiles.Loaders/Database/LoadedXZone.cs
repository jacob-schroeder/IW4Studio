using IW4.FastFiles.Database;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Loaders.Streaming.Images;
using IW4.Runtime.Assets.Images;
using IW4.Runtime.Assets.Sound;
using IW4.Runtime.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Application-facing result for one complete DB_LoadXZone call. XZone remains
/// the engine-shaped runtime object; this loader-owned record retains the
/// managed context, decoded bytes, semantic assets, and diagnostics needed by
/// tools without placing an operation result in IW4.FastFiles.
/// </summary>
public sealed record LoadedXZone(
    string SourceName,
    XZone Zone,
    DbLoadContext Context,
    DbHeader Header,
    XFile XFile,
    XAssetListSnapshot XAssetList,
    IReadOnlyList<XAssetLoadResult> LoadedAssets,
    byte[] ZoneBytes,
    IReadOnlyList<string> Warnings,
    ZoneObjectFile? ZoneObjectFile,
    ILinkAssetImportResolver? LinkAssetImportResolver)
{
    public IGfxImagePayloadResolver ImagePayloadResolver { get; init; } =
        UnavailableGfxImagePayloadResolver.Instance;

    public ISoundPayloadResolver SoundPayloadResolver { get; init; } =
        UnavailableSoundPayloadResolver.Instance;

    internal LinkGfxImageStreamSource? LinkImageStreams { get; init; }

    /// <summary>
    /// Rebuilds the ordered linker root occurrences directly from the loaded
    /// XAsset rows. Stock PS3 roots are inline/null/opaque; a packed alias row
    /// is rejected rather than reinterpreted as a canonical root policy.
    /// </summary>
    public IReadOnlyList<LinkRoot> FreezeLinkRoots()
    {
        if (XAssetList.Assets.Count != LoadedAssets.Count)
        {
            throw new InvalidDataException(
                "Loaded XAsset rows and materialization results have different lengths.");
        }

        var roots = new LinkRoot[XAssetList.Assets.Count];
        for (int index = 0; index < roots.Length; index++)
        {
            XAssetListEntrySnapshot row = XAssetList.Assets[index];
            XAssetLoadResult loaded = LoadedAssets[index];
            if (row.Index != index || loaded.Index != index)
            {
                throw new InvalidDataException(
                    "Loaded XAsset rows do not retain exact serialized order.");
            }

            roots[index] = FreezeRoot(row, loaded.Materialization);
        }

        return Array.AsReadOnly(roots);
    }

    private static LinkRoot FreezeRoot(
        XAssetListEntrySnapshot row,
        XAssetRowMaterialization materialization)
    {
        string entryId = $"xasset:{row.Index}";
        return materialization.Disposition switch
        {
            XAssetMaterializationDisposition.FullDefinition =>
                FreezeProviderRoot(entryId, row, materialization, LinkRootIntent.Owned),
            XAssetMaterializationDisposition.ResolvedReference or
            XAssetMaterializationDisposition.UnresolvedReference =>
                FreezeProviderRoot(entryId, row, materialization, LinkRootIntent.External),
            XAssetMaterializationDisposition.Null => new LinkRoot(
                entryId,
                row.Type,
                LinkRootIntent.Null,
                asset: null,
                originalSerializedName: null,
                opaqueHeader: null),
            XAssetMaterializationDisposition.OpaqueNativeNoOp => new LinkRoot(
                entryId,
                row.Type,
                LinkRootIntent.OpaqueNative,
                asset: null,
                originalSerializedName: null,
                opaqueHeader: row.RawHeader),
            XAssetMaterializationDisposition.OffsetAlias =>
                throw new NotSupportedException(
                    $"Stock-inline root policy does not permit packed XAsset row {row.Index} ({row.Type})."),
            _ => throw new InvalidDataException(
                $"XAsset row {row.Index} has non-linkable disposition {materialization.Disposition}.")
        };
    }

    private static LinkRoot FreezeProviderRoot(
        string entryId,
        XAssetListEntrySnapshot row,
        XAssetRowMaterialization materialization,
        LinkRootIntent intent)
    {
        XAssetProviderMaterialization provider = materialization.RootProvider
            ?? throw new InvalidDataException(
                $"XAsset row {row.Index} has no captured root provider.");
        return new LinkRoot(
            entryId,
            row.Type,
            intent,
            AssetKey.FromDefinition(provider.Asset),
            provider.OriginalName,
            opaqueHeader: null);
    }
}
