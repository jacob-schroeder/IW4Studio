using IW4.Loaders.Database;
using IW4.Linker.Capture;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;
using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

public static partial class LoadedZoneLinkExtensions
{
    /// <summary>Freezes retained loader observations into a symbolic source-layout object.</summary>
    public static ZoneObjectFile FreezeZoneObjectFile(this LoadedXZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return ZoneObjectObservationTranslator.Translate(zone.Observation ??
            throw new InvalidOperationException("This zone has no linkable capture.")).ObjectFile;
    }

    /// <summary>
    /// Rebuilds the ordered linker root occurrences directly from the loaded
    /// XAsset rows. Stock PS3 roots are inline/null/opaque; a packed alias row
    /// is rejected rather than reinterpreted as a canonical root policy.
    /// </summary>
    public static IReadOnlyList<LinkRoot> FreezeLinkRoots(this LoadedXZone zone)
    {
        if (zone.XAssetList.Assets.Count != zone.LoadedAssets.Count)
        {
            throw new InvalidDataException(
                "Loaded XAsset rows and materialization results have different lengths.");
        }

        var roots = new LinkRoot[zone.XAssetList.Assets.Count];
        for (int index = 0; index < roots.Length; index++)
        {
            XAssetListEntrySnapshot row = zone.XAssetList.Assets[index];
            XAssetLoadResult loaded = zone.LoadedAssets[index];
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
        string normalizedSerializedName =
            DbLoadExecutionContext.NormalizeLoadedAssetName(provider.OriginalName);
        CanonicalAssetFamily family =
            CanonicalAssetFamily.FromSerializedType(row.Type);
        return new LinkRoot(
            entryId,
            row.Type,
            intent,
            AssetKey.FromWireName(family, normalizedSerializedName),
            normalizedSerializedName,
            opaqueHeader: null);
    }
}
