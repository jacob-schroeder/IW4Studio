using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Document-scoped Menu authority router. It owns detached current and
/// baseline graphs; a session provider is published before either state is
/// committed to the coordinator.
/// </summary>
public sealed partial class MenuEditingCoordinator : IDisposable
{
    private readonly FastFileEditingSession _session;
    private readonly Dictionary<TargetZoneRowIdentity, MenuRow> _menus = [];
    private readonly Dictionary<TargetZoneRowIdentity, MenuFileRow> _menuFiles = [];
    private long _authorityRevision;
    private bool _disposed;

    public MenuEditingCoordinator(
        FastFileEditingSession editingSession,
        AssetAuthoringAdapterRegistry adapters)
    {
        _session = editingSession ??
            throw new ArgumentNullException(nameof(editingSession));
        ArgumentNullException.ThrowIfNull(adapters);
        _ = adapters.RequireAdapter(XAssetType.Menu);
        _ = adapters.RequireAdapter(XAssetType.MenuFile);

        _ = SynchronizeRows();
        _session.TargetRowsChanged += EditingSession_TargetRowsChanged;
    }

    public event EventHandler<MenuEditingCoordinatorChangedEventArgs>? Changed;

    public Guid DocumentId => _session.Document.DocumentId;

    public void Dispose()
    {
        if (_disposed)
            return;

        _session.TargetRowsChanged -= EditingSession_TargetRowsChanged;
        _disposed = true;
    }

    private void EditingSession_TargetRowsChanged(object? sender, EventArgs args)
    {
        if (!_disposed && SynchronizeRows())
        {
            AdvanceAuthorityRevision();
            RaiseChanged(
                MenuEditingCoordinatorChangeKind.EditingSessionChanged,
                rowIdentity: null,
                normalizedMenuName: null,
                resolution: null);
        }
    }

    private bool SynchronizeRows()
    {
        bool changed = false;
        foreach (WorkspaceAssetCatalogEntry entry in _session.Document.Rows)
        {
            if (entry.TargetRowIdentity is not { } identity)
                continue;

            if (entry.Access == WorkspaceAssetAccess.Editable &&
                entry.AssetType == XAssetType.Menu &&
                !_menus.ContainsKey(identity) &&
                _session.CaptureCurrentDefinition(identity) is MenuDefAsset menu)
            {
                _menus.Add(identity, new MenuRow(menu));
                changed = true;
            }
            else if (entry.Access == WorkspaceAssetAccess.Editable &&
                entry.AssetType == XAssetType.MenuFile &&
                !_menuFiles.ContainsKey(identity) &&
                _session.CaptureCurrentDefinition(identity) is MenuFileAsset menuFile)
            {
                _menuFiles.Add(identity, new MenuFileRow(menuFile));
                changed = true;
            }
        }

        return changed;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void RequireCurrent(MenuAuthorityResolutionSnapshot expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Revision != _authorityRevision)
        {
            throw new InvalidOperationException(
                "The Menu document changed; resolve it again before applying an edit.");
        }
    }

    private MenuRow RequireMenu(TargetZoneRowIdentity identity) =>
        _menus.TryGetValue(identity, out MenuRow? row)
            ? row
            : throw new KeyNotFoundException(
                $"Target Menu row {identity.SerializedIndex} is not editable.");

    private MenuFileRow RequireMenuFile(TargetZoneRowIdentity identity) =>
        _menuFiles.TryGetValue(identity, out MenuFileRow? row)
            ? row
            : throw new KeyNotFoundException(
                $"Target MenuFile row {identity.SerializedIndex} is not editable.");

    private void RaiseChanged(
        MenuEditingCoordinatorChangeKind kind,
        TargetZoneRowIdentity? rowIdentity,
        string? normalizedMenuName,
        MenuAuthorityResolutionSnapshot? resolution) =>
        Changed?.Invoke(this, new MenuEditingCoordinatorChangedEventArgs(
            kind,
            _authorityRevision,
            rowIdentity,
            normalizedMenuName,
            resolution));

    private void AdvanceAuthorityRevision() =>
        _authorityRevision = checked(_authorityRevision + 1);

    private sealed class MenuRow
    {
        public MenuRow(MenuDefAsset baseline)
        {
            Current = new MenuGraphClone(false).CloneMenu(baseline);
            Identity = MenuDocumentIdentity.Create(Current);
        }

        public MenuDefAsset Current { get; set; }
        public MenuDocumentIdentity Identity { get; set; }
    }

    private sealed class MenuFileRow
    {
        public MenuFileRow(MenuFileAsset baseline)
        {
            Current = MenuAssetProjector.Clone(baseline);
            Identity = MenuFileDocumentIdentity.Create(Current);
        }

        public MenuFileAsset Current { get; set; }
        public MenuFileDocumentIdentity Identity { get; set; }
    }

    private sealed record Occurrence(
        TargetZoneRowIdentity RowIdentity,
        int RegistrationIndex,
        MenuRegistrationId? RegistrationId,
        MenuAuthorityOccurrenceKind Kind,
        MenuDefAsset? Menu,
        MenuDocumentIdentity? Identity)
    {
        public bool IsSame(Occurrence other) =>
            RowIdentity == other.RowIdentity &&
            RegistrationIndex == other.RegistrationIndex &&
            Kind == other.Kind;
    }
}
