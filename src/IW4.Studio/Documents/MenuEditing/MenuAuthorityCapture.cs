using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Produces one revision-consistent, detached authority input. This is kept
/// separate from edit routing so capture-time graph handling cannot leak into
/// the coordinator's public API.
/// </summary>
internal sealed class MenuAuthorityCapture
{
    private readonly FastFileEditingSession _editingSession;
    private readonly AssetAuthoringAdapterRegistry _adapters;

    public MenuAuthorityCapture(
        FastFileEditingSession editingSession,
        AssetAuthoringAdapterRegistry adapters)
    {
        _editingSession = editingSession;
        _adapters = adapters;
    }

    public CapturedMenuAuthorityState Capture()
    {
        // The save capture supplies retained current drafts. Rows that have
        // never been opened are imported into short-lived local drafts below;
        // authority discovery must not create hidden document-wide editors.
        FastFileEditingSaveSnapshot save = _editingSession.CaptureForSave();
        var targetRows = new Dictionary<TargetZoneRowIdentity, TargetZoneRowSource>();
        var menuRows = new Dictionary<TargetZoneRowIdentity, CapturedMenuRow>();
        var menuFileRows = new Dictionary<TargetZoneRowIdentity, CapturedMenuFileRow>();
        var occurrences = new List<MenuAuthorityOccurrence>();

        for (int rowIndex = 0; rowIndex < save.TargetRows.Count; rowIndex++)
        {
            TargetZoneRowSource row = save.TargetRows[rowIndex];
            targetRows.Add(row.Identity, row);
            if (row.State != TargetZoneRowSourceState.Definition)
                continue;

            switch (row.SerializedType)
            {
                case XAssetType.Menu:
                    CaptureMenu(
                        save,
                        row,
                        rowIndex,
                        menuRows,
                        occurrences);
                    break;

                case XAssetType.MenuFile:
                    CaptureMenuFile(
                        save,
                        row,
                        rowIndex,
                        menuFileRows,
                        occurrences);
                    break;
            }
        }

        return new CapturedMenuAuthorityState(
            save.Revision,
            targetRows,
            menuRows,
            menuFileRows,
            occurrences,
            MenuAuthorityIndex.Build(occurrences));
    }

    public MenuEditorSnapshot? CaptureReadOnlyProvider(string normalizedName)
    {
        WorkspaceAssetCatalogEntry? entry = _editingSession.Workspace.AssetCatalog.Entries
            .Where(candidate =>
                candidate.AssetType == XAssetType.Menu &&
                candidate.Access == WorkspaceAssetAccess.ReadOnly &&
                candidate.ResolvedProvider is not null &&
                string.Equals(
                    candidate.NormalizedName,
                    normalizedName,
                    StringComparison.Ordinal))
            .OrderBy(candidate => candidate.TargetRowIdentity is null ? 1 : 0)
            .ThenBy(candidate =>
                candidate.TargetRowIdentity?.SerializedIndex ?? int.MaxValue)
            .FirstOrDefault();
        if (entry is null)
            return null;

        AssetEditorSession editor = _adapters.CreateSurface(
                _editingSession,
                entry) as AssetEditorSession
            ?? throw new InvalidOperationException(
                $"Read-only Menu '{entry.OriginalName}' has no authoring viewer.");
        return MenuReadOnlySnapshot.CaptureResolvedProvider(editor).Menu;
    }

    public static MenuEditorSnapshot SnapshotForOwner(
        CapturedMenuAuthorityState state,
        MenuAuthorityOccurrence owner)
    {
        return owner.Kind switch
        {
            MenuAuthorityOccurrenceKind.TopLevelDefinition =>
                state.MenuRows.TryGetValue(
                    owner.RowIdentity,
                    out CapturedMenuRow? row)
                    ? row.Snapshot
                    : throw new InvalidDataException(
                        "The top-level Menu authority has no captured editor snapshot."),
            MenuAuthorityOccurrenceKind.MenuFileInlineDefinition =>
                SnapshotForInlineOwner(state, owner),
            _ => throw new InvalidDataException(
                "A reference-only Menu occurrence cannot own a snapshot.")
        };
    }

    private void CaptureMenu(
        FastFileEditingSaveSnapshot save,
        TargetZoneRowSource row,
        int rowIndex,
        Dictionary<TargetZoneRowIdentity, CapturedMenuRow> menuRows,
        List<MenuAuthorityOccurrence> occurrences)
    {
        MenuDraft draft = RequireDraft<MenuDraft>(save, row);
        MenuBuildData data = draft.Data;
        MenuEditorSnapshot snapshot = draft.Snapshot;
        string name = snapshot.Name
            ?? row.OriginalSerializedName
            ?? throw new InvalidDataException(
                $"Target Menu row {row.SerializedIndex} has no logical identity.");
        menuRows.Add(row.Identity, new CapturedMenuRow(snapshot));
        occurrences.Add(new MenuAuthorityOccurrence(
            row.Identity,
            rowIndex,
            -1,
            null,
            MenuAuthorityOccurrenceKind.TopLevelDefinition,
            name,
            data,
            null));
    }

    private void CaptureMenuFile(
        FastFileEditingSaveSnapshot save,
        TargetZoneRowSource row,
        int rowIndex,
        Dictionary<TargetZoneRowIdentity, CapturedMenuFileRow> menuFileRows,
        List<MenuAuthorityOccurrence> occurrences)
    {
        MenuFileDraft draft = RequireDraft<MenuFileDraft>(save, row);
        MenuFileBuildData data = draft.Data;
        MenuFileEditorSnapshot snapshot = draft.Snapshot;
        if (data.MenuLinks.Count != snapshot.Registrations.Count)
        {
            throw new InvalidDataException(
                $"Target MenuFile row {row.SerializedIndex} has mismatched registration data and identities.");
        }

        menuFileRows.Add(row.Identity, new CapturedMenuFileRow(snapshot));
        for (int registrationIndex = 0;
             registrationIndex < data.MenuLinks.Count;
             registrationIndex++)
        {
            NestedXAssetBuildLink link = data.MenuLinks[registrationIndex];
            MenuFileRegistrationSnapshot registration =
                snapshot.Registrations[registrationIndex];
            MenuBuildData? definition = link.IncomingDefinition as MenuBuildData;
            occurrences.Add(new MenuAuthorityOccurrence(
                row.Identity,
                rowIndex,
                registrationIndex,
                registration.Id,
                definition is null
                    ? MenuAuthorityOccurrenceKind.MenuFileRegistration
                    : MenuAuthorityOccurrenceKind.MenuFileInlineDefinition,
                link.Reference.OriginalSerializedName,
                definition,
                link.SourceForm));
        }
    }

    private static MenuEditorSnapshot SnapshotForInlineOwner(
        CapturedMenuAuthorityState state,
        MenuAuthorityOccurrence owner)
    {
        if (!state.MenuFileRows.TryGetValue(
                owner.RowIdentity,
                out CapturedMenuFileRow? row))
        {
            throw new InvalidDataException(
                "The inline Menu authority has no captured MenuFile snapshot.");
        }

        MenuRegistrationId registrationId = owner.RegistrationId
            ?? throw new InvalidDataException(
                "The inline Menu authority has no stable registration identity.");
        MenuFileRegistrationSnapshot registration = row.Snapshot.Registrations
            .SingleOrDefault(value => value.Id == registrationId)
            ?? throw new InvalidDataException(
                "The inline Menu authority registration is absent from its captured MenuFile snapshot.");
        return registration.Menu
            ?? throw new InvalidDataException(
                "The inline Menu authority has no detached editor snapshot.");
    }

    private T RequireDraft<T>(
        FastFileEditingSaveSnapshot save,
        TargetZoneRowSource row)
        where T : notnull
    {
        if (save.TryGetDraftObject(row.Identity, out object? captured))
        {
            return captured is T typed
                ? typed
                : throw new InvalidDataException(
                    $"Target {row.SerializedType} row {row.SerializedIndex} captured '{captured?.GetType().Name ?? "null"}', not {typeof(T).Name}.");
        }

        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(
            row.SerializedType);
        object authored = adapter.ImportAuthoredSnapshot(row);
        object draft = adapter.CreateDraft(authored);
        return draft is T local
            ? local
            : throw new InvalidDataException(
                $"The {row.SerializedType} adapter created '{draft?.GetType().Name ?? "null"}', not {typeof(T).Name}.");
    }
}

internal sealed record CapturedMenuRow(MenuEditorSnapshot Snapshot);

internal sealed record CapturedMenuFileRow(MenuFileEditorSnapshot Snapshot);

internal sealed class CapturedMenuAuthorityState
{
    public CapturedMenuAuthorityState(
        long revision,
        IReadOnlyDictionary<TargetZoneRowIdentity, TargetZoneRowSource>
            targetRows,
        IReadOnlyDictionary<TargetZoneRowIdentity, CapturedMenuRow>
            menuRows,
        IReadOnlyDictionary<TargetZoneRowIdentity, CapturedMenuFileRow>
            menuFileRows,
        IEnumerable<MenuAuthorityOccurrence> occurrences,
        MenuAuthorityIndex authorities)
    {
        Revision = revision;
        TargetRows = targetRows;
        MenuRows = menuRows;
        MenuFileRows = menuFileRows;
        Occurrences = Array.AsReadOnly(occurrences.ToArray());
        Authorities = authorities;
    }

    public long Revision { get; }

    public IReadOnlyDictionary<TargetZoneRowIdentity, TargetZoneRowSource>
        TargetRows { get; }

    public IReadOnlyDictionary<TargetZoneRowIdentity, CapturedMenuRow>
        MenuRows { get; }

    public IReadOnlyDictionary<TargetZoneRowIdentity, CapturedMenuFileRow>
        MenuFileRows { get; }

    public IReadOnlyList<MenuAuthorityOccurrence> Occurrences { get; }

    public MenuAuthorityIndex Authorities { get; }

    public TargetZoneRowSource RequireTargetRow(
        TargetZoneRowIdentity identity,
        XAssetType expectedType)
    {
        if (!TargetRows.TryGetValue(identity, out TargetZoneRowSource? row))
        {
            throw new KeyNotFoundException(
                $"Target row {identity.SerializedIndex} is not part of this Menu editing document.");
        }
        if (row.SerializedType != expectedType)
        {
            throw new InvalidOperationException(
                $"Target row {identity.SerializedIndex} is {row.SerializedType}, not {expectedType}.");
        }

        return row;
    }

    public CapturedMenuFileRow RequireMenuFileRow(
        TargetZoneRowIdentity identity)
    {
        _ = RequireTargetRow(identity, XAssetType.MenuFile);
        return MenuFileRows.TryGetValue(identity, out CapturedMenuFileRow? row)
            ? row
            : throw new InvalidOperationException(
                $"Target MenuFile row {identity.SerializedIndex} is not an editable definition.");
    }
}
