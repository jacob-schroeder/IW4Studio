using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Linking;

namespace IW4.Studio.Documents;

/// <summary>
/// One-way adapter from the document-bound import snapshot to the immutable,
/// source-independent linker graph. It intentionally copies no source path,
/// decoded zone, raw header (except native no-ops), runtime object, pool, or
/// UI state.
/// </summary>
public static class ZoneBuildSnapshotLinkAdapter
{
    public static ZoneLinkRequest Create(ZoneBuildSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Validation.IsValid)
            throw new InvalidOperationException("An invalid build snapshot cannot be converted to a linker request.");

        var builder = new ZoneBuildGraphBuilder();
        foreach (ZoneBuildRow row in snapshot.Rows)
        {
            ZoneAssetKey key = new(row.AssetType, LogicalName(row));
            string entryId = $"row:{row.Index}";
            switch (row)
            {
                case OwnedDefinitionBuildRow owned:
                    builder.AddOwned(key, owned.BuildData, importedOrder: row.Index, entryId: entryId);
                    break;
                case ExternalReferenceBuildRow external:
                    builder.AddExternal(key, row.Index, entryId);
                    break;
                case NullBuildRow:
                    builder.AddNull(key, row.Index, entryId);
                    break;
                case OpaqueNativeNoOpBuildRow opaque:
                    builder.AddOpaqueNativeNoOp(key, opaque.RawHeader, row.Index, entryId);
                    break;
                default:
                    throw new InvalidDataException($"Snapshot row {row.Index} has unsupported linker classification '{row.GetType().Name}'.");
            }
        }

        // Existing detached build data still carries source-local script
        // indices. Keep the imported slots during the compatibility bridge;
        // canonical direct requests use value-deduplication by default. Keep
        // the imported block capacities as floors as well: native zones can
        // reserve unused destination space that allocation high-water alone
        // cannot reconstruct.
        var layout = new ZoneLinkLayoutPolicy(
            externalSize: snapshot.DecodedMetadata.ExternalSize,
            blockSizeFloors: snapshot.DecodedMetadata.BlockSizeFloors);
        return builder.Freeze(
            snapshot.ScriptStrings.OrderBy(value => value.Index).Select(value => value.Value),
            ZoneLinkOutputPolicy.LegacyImported,
            layout);
    }

    internal static string LogicalName(ZoneBuildRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string? sourceName = row.OriginalSerializedName ??
            (row as ExternalReferenceBuildRow)?.Reference.OriginalSerializedName;
        return sourceName is { Length: > 0 }
            ? ZoneAssetKey.FromWireName(
                row.AssetType,
                sourceName).LogicalName
            : $"row/{row.Index}";
    }
}
