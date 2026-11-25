using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    /// <summary>Whether a selected dependency can become a new external target row.</summary>
    public bool CanAddReference(WorkspaceAssetCatalogEntry dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            return TryGetReferenceKeyCore(dependency, out AssetKey key, out _) &&
                !_revision.LinkRequest.Roots.Any(root => root.Asset == key);
        }
    }

    /// <summary>Adds the selected dependency as a comma reference in the target zone.</summary>
    public WorkspaceAssetCatalogEntry AddReference(WorkspaceAssetCatalogEntry dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        WorkspaceAssetCatalogEntry entry;
        bool added;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (!TryGetReferenceKeyCore(
                    dependency, out AssetKey key, out string serializedName))
            {
                throw new InvalidOperationException(
                    "The selected dependency cannot be serialized as an external reference.");
            }

            WorkspaceAssetCatalogEntry? existing = FindReferenceRowCore(dependency.AssetType, key);
            if (existing is not null)
            {
                entry = existing;
                added = false;
            }
            else
            {
                if (_revision.LinkRequest.Roots.Any(root => root.Asset == key))
                {
                    throw new InvalidOperationException(
                        "The target zone already has a root for this asset.");
                }

                LinkRoot[] roots = [.. _revision.LinkRequest.Roots, new LinkRoot(
                    $"reference:{Guid.NewGuid():N}",
                    dependency.AssetType,
                    LinkRootIntent.External,
                    key,
                    serializedName,
                    opaqueHeader: null)];
                Publish(_authoredAssets, roots);
                entry = Document.AppendReference(dependency, serializedName);
                _addedReferenceRows.Add(entry.TargetRowIdentity!.Value, _revision.Revision);
                RebuildChangeSet();
                added = true;
            }
        }

        if (added)
            NotifyTargetRowsChanged();
        return entry;
    }

    internal WorkspaceAssetCatalogEntry? FindReferenceRowCore(
        XAssetType type,
        AssetKey key) =>
        Document.Rows.FirstOrDefault(row =>
            row.AssetType == type &&
            row.TargetRowIdentity is { } identity &&
            identity.SerializedIndex < _revision.LinkRequest.Roots.Count &&
            _revision.LinkRequest.Roots[identity.SerializedIndex] is
            { Intent: LinkRootIntent.External, Asset: { } rootKey } &&
            rootKey == key);

    private bool TryGetReferenceKeyCore(
        WorkspaceAssetCatalogEntry dependency,
        out AssetKey key,
        out string serializedName)
    {
        key = default;
        serializedName = string.Empty;
        // An external root would become authoritative over an embedded target provider.
        if (dependency.DependencyIdentity is not { } identity ||
            identity.DocumentId != Document.DocumentId ||
            !Workspace.AssetCatalog.DependencyEntries.Any(entry =>
                ReferenceEquals(entry, dependency)) ||
            (dependency.ProviderZone?.IsTarget == true &&
                !dependency.IsReferencePlaceholder) ||
            !ZoneLinker.SupportsExternalReference(dependency.AssetType) ||
            dependency.OriginalName is not { } name)
        {
            return false;
        }

        string logicalName = name.StartsWith(',') ? name[1..] : name;
        if (logicalName.StartsWith(',') ||
            logicalName.Any(character => character == '\0' || character > byte.MaxValue))
        {
            return false;
        }

        try
        {
            key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(dependency.AssetType),
                logicalName);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (key.NormalizedName != dependency.NormalizedName)
            return false;

        serializedName = "," + logicalName;
        return true;
    }
}
