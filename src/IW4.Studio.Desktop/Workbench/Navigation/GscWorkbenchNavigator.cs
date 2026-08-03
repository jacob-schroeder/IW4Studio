using IW4.FastFiles.Zone;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Tools.AssetPool;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Documents;
using IW4.Studio.Gsc;

namespace IW4.Studio.Desktop.Workbench.Navigation;

/// <summary>
/// Resolves a host-neutral GSC location through the effective authored/runtime
/// workspace, opens the matching workbench document, and selects its exact
/// source span.
/// </summary>
internal sealed class GscWorkbenchNavigator
{
    private readonly FastFileWorkspace _workspace;
    private readonly GscWorkspaceIndexService _gscWorkspace;
    private readonly FastFileAssetsNavigatorViewModel _fastFileAssets;
    private readonly AssetPoolNavigatorViewModel _assetPool;
    private readonly EditorViewModel _editor;

    internal GscWorkbenchNavigator(
        FastFileWorkspace workspace,
        GscWorkspaceIndexService gscWorkspace,
        FastFileAssetsNavigatorViewModel fastFileAssets,
        AssetPoolNavigatorViewModel assetPool,
        EditorViewModel editor)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _gscWorkspace = gscWorkspace
            ?? throw new ArgumentNullException(nameof(gscWorkspace));
        _fastFileAssets = fastFileAssets
            ?? throw new ArgumentNullException(nameof(fastFileAssets));
        _assetPool = assetPool ?? throw new ArgumentNullException(nameof(assetPool));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    /// <returns>Null on success; otherwise a user-facing failure reason.</returns>
    internal string? NavigateTo(GscSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            return NavigateCore(location);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                KeyNotFoundException)
        {
            return exception.Message;
        }
    }

    private string? NavigateCore(GscSourceLocation location)
    {
        GscWorkspaceSnapshot snapshot = _gscWorkspace.GetSnapshot();
        GscWorkspaceAuthoredDocument? authoredDocument = snapshot
            .AuthoredDocuments
            .LastOrDefault(candidate =>
                GscScriptPath.FromAssetName(candidate.AssetName) == location.Path);

        string? selectionFailure;
        if (authoredDocument is not null)
        {
            selectionFailure = SelectAuthoredRawFile(authoredDocument);
        }
        else
        {
            GscWorkspaceRawFileSlot? slot = snapshot.Slots.SingleOrDefault(
                candidate =>
                    candidate.Source is not null &&
                    GscScriptPath.FromAssetName(candidate.AssetName) == location.Path);
            if (slot is null)
            {
                return "the RawFile is no longer present in the workspace index.";
            }

            selectionFailure = SelectRawFile(slot);
        }

        if (selectionFailure is not null)
            return selectionFailure;

        if (_editor.SelectedEditorHost?.HostedView is not IEditorTextNavigator navigator)
            return "the selected RawFile does not expose text navigation.";

        string indexedSource = snapshot.Index
            .GetDocument(location.Path)
            .Snapshot.Source.Text;
        string source = indexedSource;
        if (_editor.SelectedEditorHost.HostedViewModel is
            RawFileEditorViewModel rawFileEditor)
        {
            source = rawFileEditor.PayloadInput;
            if (!string.Equals(source, indexedSource, StringComparison.Ordinal))
            {
                return "the destination RawFile has unapplied changes; apply " +
                       "or discard them before navigating to an indexed span.";
            }
        }

        if (location.Span.End > source.Length)
            return "the indexed source span is outside the selected editor buffer.";

        GscLinePosition position = new GscSourceText(source)
            .GetLinePosition(location.Span.Start);
        navigator.NavigateTo(new EditorTextLocation(
            location.Span.Start,
            location.Span.Length,
            position.Line,
            position.Character));
        return null;
    }

    private string? SelectAuthoredRawFile(
        GscWorkspaceAuthoredDocument document)
    {
        FastFileAssetNavigatorRow? targetRow = _fastFileAssets.AllRows
            .SingleOrDefault(row => row.Identity == document.RowIdentity);
        if (targetRow is null)
        {
            return "the applied RawFile target row is absent from the " +
                   "navigator snapshot.";
        }

        _fastFileAssets.SelectedRow = targetRow;
        WorkspaceAssetCatalogEntry? selectedEntry =
            _editor.SelectedEditorHost?.Entry.Entry;
        if (selectedEntry?.TargetRowIdentity != document.RowIdentity)
        {
            return "workbench catalog routing did not open the applied " +
                   "RawFile target row.";
        }

        return null;
    }

    private string? SelectRawFile(GscWorkspaceRawFileSlot slot)
    {
        GscWorkspaceProviderProvenance activeProvider = slot.Providers.Single(
            provider => provider.IsActive);
        WorkspaceAssetCatalogEntry[] targetEntries = _workspace.AssetCatalog
            .TargetEntries
            .Where(entry =>
                entry.AssetType == XAssetType.RawFile &&
                string.Equals(
                    entry.NormalizedName,
                    slot.NormalizedAssetName,
                    StringComparison.Ordinal))
            .ToArray();
        if (targetEntries.Length == 0)
            return SelectPoolRawFile(slot);

        WorkspaceAssetCatalogEntry? targetEntry = ResolveActiveTargetEntry(
            slot,
            activeProvider,
            targetEntries,
            out string? failureReason);
        return targetEntry is null
            ? failureReason
            : SelectTargetRawFile(slot, targetEntry);
    }

    private static WorkspaceAssetCatalogEntry? ResolveActiveTargetEntry(
        GscWorkspaceRawFileSlot slot,
        GscWorkspaceProviderProvenance activeProvider,
        IReadOnlyList<WorkspaceAssetCatalogEntry> targetEntries,
        out string? failureReason)
    {
        WorkspaceAssetCatalogEntry? resolvedReference = targetEntries
            .Where(entry =>
                entry.Origin == WorkspaceAssetOrigin.TargetResolvedReference &&
                entry.ResolvedProvider?.ProviderId == slot.ActiveProviderId)
            .OrderBy(entry => entry.TargetRowIdentity!.Value.SerializedIndex)
            .FirstOrDefault();
        if (resolvedReference is not null)
        {
            failureReason = null;
            return resolvedReference;
        }

        if (!targetEntries.Any(entry =>
                entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition))
        {
            failureReason =
                $"target catalog rows exist for this path, but none exposes active " +
                $"provider {slot.ActiveProviderId}.";
            return null;
        }

        // An editable target editor presents its authored baseline, not an
        // arbitrary provider currently shadowing that runtime pool identity.
        if (activeProvider.IsTargetZone != true)
        {
            failureReason =
                $"active provider {slot.ActiveProviderId} shadows a target-owned " +
                "RawFile, but Studio currently exposes only the authored target " +
                "baseline for that catalog identity.";
            return null;
        }

        WorkspaceAssetCatalogEntry[] activeDefinitions = targetEntries
            .Where(entry =>
                entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                entry.ResolvedProvider?.ProviderId == slot.ActiveProviderId)
            .ToArray();
        if (activeDefinitions.Length != 1)
        {
            failureReason =
                $"the active target provider {slot.ActiveProviderId} cannot be " +
                "mapped to exactly one authored RawFile row.";
            return null;
        }

        failureReason = null;
        return activeDefinitions[0];
    }

    private string? SelectTargetRawFile(
        GscWorkspaceRawFileSlot slot,
        WorkspaceAssetCatalogEntry targetEntry)
    {
        TargetZoneRowIdentity targetIdentity = targetEntry.TargetRowIdentity
            ?? throw new InvalidDataException(
                "A resolved GSC target entry has no target-row identity.");
        FastFileAssetNavigatorRow? targetRow = _fastFileAssets.AllRows
            .SingleOrDefault(row => row.Identity == targetIdentity);
        if (targetRow is null)
            return "the active RawFile target row is absent from the navigator snapshot.";

        _fastFileAssets.SelectedRow = targetRow;
        WorkspaceAssetCatalogEntry? selectedEntry = _editor.SelectedEditorHost?.Entry.Entry;
        if (selectedEntry?.TargetRowIdentity != targetIdentity ||
            selectedEntry?.ResolvedProvider?.ProviderId != slot.ActiveProviderId)
        {
            return $"workbench catalog routing did not open active provider " +
                   $"{slot.ActiveProviderId}.";
        }

        return null;
    }

    private string? SelectPoolRawFile(GscWorkspaceRawFileSlot slot)
    {
        AssetPoolSlotSnapshot? poolRow = _assetPool.AllRows.FirstOrDefault(
            row =>
                row.Address == slot.Address &&
                row.ActiveProviderId == slot.ActiveProviderId);
        if (poolRow is null)
        {
            return "the exact active provider is absent from the asset-pool " +
                   "navigator snapshot.";
        }

        _assetPool.SelectedRow = poolRow;
        if (_editor.SelectedEditorHost?.Entry.Entry.ResolvedProvider?.ProviderId !=
            slot.ActiveProviderId)
        {
            return $"workbench catalog routing did not open active provider " +
                   $"{slot.ActiveProviderId}.";
        }

        return null;
    }
}
