using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    public MenuAuthorityEditResult RevertTopLevelMenu(
        TargetZoneRowIdentity rowIdentity,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedResolution);
        CapturedMenuAuthorityState state = _capture.Capture();
        RequireExpectedRevision(state, expectedResolution);
        string currentName = TopLevelMenuName(state, rowIdentity);
        MenuAuthorityResolutionSnapshot current = RequireCurrentResolution(
            state,
            expectedResolution,
            currentName);
        MenuAuthorityOwnerSnapshot owner = current.Owner
            ?? throw new InvalidOperationException(
                $"Menu '{current.RequestedName}' has no target definition to revert.");
        if (owner.Kind != MenuAuthorityOccurrenceKind.TopLevelDefinition ||
            owner.RowIdentity != rowIdentity)
        {
            throw new InvalidOperationException(
                $"Target Menu row {rowIdentity.SerializedIndex} cannot be " +
                "reverted through this logical selection because it does " +
                "not own the resolved top-level Menu authority.");
        }

        int materializingOccurrences = current.Occurrences.Count(
            occurrence => occurrence.MaterializesDefinition);
        if (materializingOccurrences != 1)
        {
            throw new InvalidOperationException(
                $"Menu '{current.RequestedName}' has " +
                $"{materializingOccurrences} complete target definitions. " +
                "A row-only revert would split their synchronized authority, " +
                "so this shared Menu must be edited forward instead.");
        }

        bool changed = _editingSession.RevertOneAtRevision(
            state.Revision,
            rowIdentity);
        MenuAuthorityResolutionSnapshot updated = Resolve(
            _capture.Capture(),
            expectedResolution.RequestedName);
        if (changed)
        {
            RaiseChanged(
                MenuEditingCoordinatorChangeKind.MenuReverted,
                rowIdentity,
                expectedResolution.NormalizedName,
                updated);
        }

        return new MenuAuthorityEditResult(changed, updated);
    }

    /// <summary>
    /// Returns whether reverting one complete MenuFile row can leave every
    /// logical Menu authority synchronized. The result is advisory; the
    /// revision-consistent preflight is repeated by <see cref="RevertMenuFile"/>.
    /// </summary>
    public bool CanRevertMenuFile(TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        CapturedMenuAuthorityState state = _capture.Capture();
        _ = state.RequireMenuFileRow(rowIdentity);
        SavedMenuFileBaseline baseline = CaptureSavedMenuFileBaseline(
            state.Revision,
            rowIdentity);
        return FindMenuFileRevertConflict(
            state,
            rowIdentity,
            baseline.Data) is null;
    }

    /// <summary>
    /// Reverts the complete owning MenuFile row. If it was a newly added row,
    /// the result reports removal and has no refreshed MenuFile snapshot.
    /// A row whose inline definitions are mirrored outside that row cannot be
    /// reverted independently because doing so would split logical authority.
    /// </summary>
    public MenuFileRevertResult RevertMenuFile(
        TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        CapturedMenuAuthorityState state = _capture.Capture();
        _ = state.RequireMenuFileRow(rowIdentity);
        SavedMenuFileBaseline baseline = CaptureSavedMenuFileBaseline(
            state.Revision,
            rowIdentity);
        if (FindMenuFileRevertConflict(
                state,
                rowIdentity,
                baseline.Data) is { } conflict)
        {
            throw new InvalidOperationException(
                $"MenuFile row {rowIdentity.SerializedIndex} cannot be " +
                $"reverted because its saved Menu '{conflict.MenuName}' " +
                $"differs from the current complete definition in target " +
                $"row {conflict.ConflictingRowIdentity.SerializedIndex}. A " +
                "row-only revert would split their synchronized authority.");
        }

        bool changed = _editingSession.RevertOneAtRevision(
            state.Revision,
            baseline.SavedRevision,
            rowIdentity);
        bool removed = !_editingSession.Document.TryGetRow(rowIdentity, out _);
        MenuFileEditorSnapshot? snapshot = removed
            ? null
            : _capture.Capture().RequireMenuFileRow(rowIdentity).Snapshot;
        if (changed)
        {
            RaiseChanged(
                MenuEditingCoordinatorChangeKind.MenuFileReverted,
                rowIdentity,
                normalizedMenuName: null,
                resolution: null);
        }

        return new MenuFileRevertResult(changed, removed, snapshot);
    }

    private SavedMenuFileBaseline CaptureSavedMenuFileBaseline(
        long revision,
        TargetZoneRowIdentity rowIdentity)
    {
        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(
            XAssetType.MenuFile);
        SavedAuthoredDraftCapture baseline =
            _editingSession.CaptureSavedAuthoredDraftAtRevision(
                revision,
                rowIdentity,
                adapter);
        return baseline.Draft is MenuFileDraft menuFile
            ? new SavedMenuFileBaseline(
                baseline.SavedRevision,
                menuFile.Data)
            : throw new InvalidDataException(
                $"The saved MenuFile baseline for target row " +
                $"{rowIdentity.SerializedIndex} is " +
                $"'{baseline.Draft.GetType().FullName}', not " +
                $"'{typeof(MenuFileDraft).FullName}'.");
    }

    private static MenuFileRevertConflict? FindMenuFileRevertConflict(
        CapturedMenuAuthorityState state,
        TargetZoneRowIdentity rowIdentity,
        MenuFileBuildData baseline)
    {
        foreach (NestedXAssetBuildLink link in baseline.MenuLinks)
        {
            if (link.IncomingDefinition is not MenuBuildData savedMenu)
                continue;

            string normalizedName = XAssetStableIdentity.NormalizeLookupName(
                link.Reference.OriginalSerializedName);
            string savedProjection = MenuSemanticProjection.Serialize(
                savedMenu.Definition);
            foreach (MenuAuthorityOccurrence current in state.Occurrences.Where(
                         occurrence =>
                             occurrence.RowIdentity != rowIdentity &&
                             occurrence.MaterializesDefinition &&
                             string.Equals(
                                 occurrence.NormalizedName,
                                 normalizedName,
                                 StringComparison.Ordinal)))
            {
                string currentProjection = MenuSemanticProjection.Serialize(
                    current.Definition!.Definition);
                if (!string.Equals(
                        savedProjection,
                        currentProjection,
                        StringComparison.Ordinal))
                {
                    return new MenuFileRevertConflict(
                        XAssetStableIdentity.GetLookupSpelling(
                            link.Reference.OriginalSerializedName),
                        current.RowIdentity);
                }
            }
        }

        return null;
    }

    private sealed record MenuFileRevertConflict(
        string MenuName,
        TargetZoneRowIdentity ConflictingRowIdentity);

    private sealed record SavedMenuFileBaseline(
        long SavedRevision,
        MenuFileBuildData Data);
}
