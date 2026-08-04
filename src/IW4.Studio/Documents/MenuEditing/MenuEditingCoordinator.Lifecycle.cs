using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    private void RequireMenuAdapter<TDraft>(XAssetType assetType)
        where TDraft : notnull
    {
        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(assetType);
        if (adapter.DraftType != typeof(TDraft))
        {
            throw new InvalidOperationException(
                $"The {assetType} authoring adapter declares draft type '{adapter.DraftType.FullName}', not '{typeof(TDraft).FullName}'.");
        }
    }

    private void RaiseChanged(
        MenuEditingCoordinatorChangeKind kind,
        TargetZoneRowIdentity? rowIdentity,
        string? normalizedMenuName,
        MenuAuthorityResolutionSnapshot? resolution)
    {
        Changed?.Invoke(
            this,
            new MenuEditingCoordinatorChangedEventArgs(
                kind,
                _editingSession.Revision,
                rowIdentity,
                normalizedMenuName,
                resolution));
    }

    private void OnTargetRowsChanged(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        RaiseChanged(
            MenuEditingCoordinatorChangeKind.TargetRowsChanged,
            rowIdentity: null,
            normalizedMenuName: null,
            resolution: null);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}
