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
    private static readonly XAssetType[] CapturedAssetTypes =
    [
        XAssetType.Menu,
        XAssetType.MenuFile
    ];

    private readonly FastFileEditingSession _editingSession;
    private readonly AssetAuthoringAdapterRegistry _adapters;
    // Capture is serialized by MenuEditingCoordinator._captureGate. These
    // fragments belong only to immutable source rows; retained drafts always
    // bypass and evict them.
    private readonly Dictionary<TargetZoneRowIdentity, MenuRowFragment>
        _sourceMenus = [];
    private readonly Dictionary<TargetZoneRowIdentity, MenuFileRowFragment>
        _sourceMenuFiles = [];

    public MenuAuthorityCapture(
        FastFileEditingSession editingSession,
        AssetAuthoringAdapterRegistry adapters)
    {
        _editingSession = editingSession;
        _adapters = adapters;
    }

    public CapturedMenuAuthorityState Capture()
    {
        // The filtered capture supplies retained current Menu drafts without
        // cloning unrelated document state. Source rows that have never been
        // opened are imported once into immutable cached fragments; authority
        // discovery must not create hidden document-wide editors.
        FastFileEditingRowsSnapshot capture = _editingSession.CaptureRows(
            CapturedAssetTypes);
        var targetRows = new Dictionary<TargetZoneRowIdentity, TargetZoneRowSource>();
        var menuRows = new Dictionary<TargetZoneRowIdentity, CapturedMenuRow>();
        var menuFileRows = new Dictionary<TargetZoneRowIdentity, CapturedMenuFileRow>();
        var occurrences = new List<MenuAuthorityOccurrence>();

        foreach (FastFileEditingCapturedRow capturedRow in capture.Rows)
        {
            TargetZoneRowSource row = capturedRow.Row;
            targetRows.Add(row.Identity, row);
            if (row.State != TargetZoneRowSourceState.Definition)
            {
                EvictSourceFragment(row.Identity);
                continue;
            }

            switch (row.SerializedType)
            {
                case XAssetType.Menu:
                    CaptureMenu(
                        capturedRow,
                        menuRows,
                        occurrences);
                    break;

                case XAssetType.MenuFile:
                    CaptureMenuFile(
                        capturedRow,
                        menuFileRows,
                        occurrences);
                    break;
            }
        }

        PruneSourceFragments(targetRows.Keys);

        return new CapturedMenuAuthorityState(
            capture.Revision,
            capture.RetainedDraftCount,
            targetRows,
            menuRows,
            menuFileRows,
            occurrences,
            MenuAuthorityIndex.Build(occurrences));
    }

    public FastFileEditingCaptureVersion GetCaptureVersion() =>
        _editingSession.GetCaptureVersion(CapturedAssetTypes);

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
        FastFileEditingCapturedRow capturedRow,
        Dictionary<TargetZoneRowIdentity, CapturedMenuRow> menuRows,
        List<MenuAuthorityOccurrence> occurrences)
    {
        TargetZoneRowSource row = capturedRow.Row;
        MenuRowFragment fragment;
        if (capturedRow.Draft is { } capturedDraft)
        {
            EvictSourceFragment(row.Identity);
            fragment = CreateMenuFragment(
                row,
                RequireDraft<MenuDraft>(row, capturedDraft));
        }
        else
        {
            fragment = GetSourceMenuFragment(row);
        }

        menuRows.Add(row.Identity, fragment.Row);
        occurrences.Add(new MenuAuthorityOccurrence(
            row.Identity,
            capturedRow.TraversalIndex,
            -1,
            null,
            MenuAuthorityOccurrenceKind.TopLevelDefinition,
            fragment.Name,
            fragment.Data,
            null));
    }

    private void CaptureMenuFile(
        FastFileEditingCapturedRow capturedRow,
        Dictionary<TargetZoneRowIdentity, CapturedMenuFileRow> menuFileRows,
        List<MenuAuthorityOccurrence> occurrences)
    {
        TargetZoneRowSource row = capturedRow.Row;
        MenuFileRowFragment fragment;
        if (capturedRow.Draft is { } capturedDraft)
        {
            EvictSourceFragment(row.Identity);
            fragment = CreateMenuFileFragment(
                row,
                RequireDraft<MenuFileDraft>(row, capturedDraft));
        }
        else
        {
            fragment = GetSourceMenuFileFragment(row);
        }

        MenuFileBuildData data = fragment.Data;
        MenuFileEditorSnapshot snapshot = fragment.Row.Snapshot;
        menuFileRows.Add(row.Identity, fragment.Row);
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
                capturedRow.TraversalIndex,
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

    private MenuRowFragment GetSourceMenuFragment(TargetZoneRowSource row)
    {
        if (_sourceMenus.TryGetValue(
                row.Identity,
                out MenuRowFragment? cached) &&
            ReferenceEquals(cached.Source, row))
        {
            return cached;
        }

        EvictSourceFragment(row.Identity);
        MenuRowFragment fragment = CreateMenuFragment(
            row,
            CreateSourceDraft<MenuDraft>(row));
        _sourceMenus.Add(row.Identity, fragment);
        return fragment;
    }

    private MenuFileRowFragment GetSourceMenuFileFragment(
        TargetZoneRowSource row)
    {
        if (_sourceMenuFiles.TryGetValue(
                row.Identity,
                out MenuFileRowFragment? cached) &&
            ReferenceEquals(cached.Source, row))
        {
            return cached;
        }

        EvictSourceFragment(row.Identity);
        MenuFileRowFragment fragment = CreateMenuFileFragment(
            row,
            CreateSourceDraft<MenuFileDraft>(row));
        _sourceMenuFiles.Add(row.Identity, fragment);
        return fragment;
    }

    private static MenuRowFragment CreateMenuFragment(
        TargetZoneRowSource row,
        MenuDraft draft)
    {
        MenuBuildData data = draft.Data;
        MenuEditorSnapshot snapshot = draft.Snapshot;
        string name = snapshot.Name
            ?? row.OriginalSerializedName
            ?? throw new InvalidDataException(
                $"Target Menu row {row.SerializedIndex} has no logical identity.");
        return new MenuRowFragment(
            row,
            new CapturedMenuRow(snapshot),
            name,
            data);
    }

    private static MenuFileRowFragment CreateMenuFileFragment(
        TargetZoneRowSource row,
        MenuFileDraft draft)
    {
        MenuFileBuildData data = draft.Data;
        MenuFileEditorSnapshot snapshot = draft.Snapshot;
        if (data.MenuLinks.Count != snapshot.Registrations.Count)
        {
            throw new InvalidDataException(
                $"Target MenuFile row {row.SerializedIndex} has mismatched registration data and identities.");
        }

        return new MenuFileRowFragment(
            row,
            new CapturedMenuFileRow(snapshot),
            data);
    }

    private void EvictSourceFragment(TargetZoneRowIdentity identity)
    {
        _sourceMenus.Remove(identity);
        _sourceMenuFiles.Remove(identity);
    }

    private void PruneSourceFragments(
        IEnumerable<TargetZoneRowIdentity> liveRows)
    {
        HashSet<TargetZoneRowIdentity> live = liveRows.ToHashSet();
        foreach (TargetZoneRowIdentity identity in
                 _sourceMenus.Keys.Where(identity => !live.Contains(identity)).ToArray())
        {
            _sourceMenus.Remove(identity);
        }
        foreach (TargetZoneRowIdentity identity in
                 _sourceMenuFiles.Keys.Where(identity => !live.Contains(identity)).ToArray())
        {
            _sourceMenuFiles.Remove(identity);
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

    private static T RequireDraft<T>(
        TargetZoneRowSource row,
        object captured)
        where T : notnull
    {
        return captured is T typed
            ? typed
            : throw new InvalidDataException(
                $"Target {row.SerializedType} row {row.SerializedIndex} captured '{captured?.GetType().Name ?? "null"}', not {typeof(T).Name}.");
    }

    private T CreateSourceDraft<T>(TargetZoneRowSource row)
        where T : notnull
    {
        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(
            row.SerializedType);
        object authored = adapter.ImportAuthoredSnapshot(row);
        object draft = adapter.CreateDraft(authored);
        return draft is T local
            ? local
            : throw new InvalidDataException(
                $"The {row.SerializedType} adapter created '{draft?.GetType().Name ?? "null"}', not {typeof(T).Name}.");
    }

    private sealed record MenuRowFragment(
        TargetZoneRowSource Source,
        CapturedMenuRow Row,
        string Name,
        MenuBuildData Data);

    private sealed record MenuFileRowFragment(
        TargetZoneRowSource Source,
        CapturedMenuFileRow Row,
        MenuFileBuildData Data);
}

internal sealed record CapturedMenuRow(MenuEditorSnapshot Snapshot);

internal sealed record CapturedMenuFileRow(MenuFileEditorSnapshot Snapshot);

internal sealed class CapturedMenuAuthorityState
{
    public CapturedMenuAuthorityState(
        long revision,
        int retainedDraftCount,
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
        RetainedDraftCount = retainedDraftCount;
        TargetRows = targetRows;
        MenuRows = menuRows;
        MenuFileRows = menuFileRows;
        Occurrences = Array.AsReadOnly(occurrences.ToArray());
        Authorities = authorities;
    }

    public long Revision { get; }

    internal int RetainedDraftCount { get; }

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
