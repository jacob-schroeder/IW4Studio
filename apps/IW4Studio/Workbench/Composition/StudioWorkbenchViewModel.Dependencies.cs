using System.ComponentModel;
using Avalonia.Threading;
using IW4.Studio.Desktop.Workbench.Tools;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Documents;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Desktop.Workbench.Composition;

public sealed partial class StudioWorkbenchViewModel
{
    private WorkspaceAssetDependencyIndex? _dependencyIndex;
    private CancellationTokenSource? _dependencyBuild;
    private long _dependencyRevision = -1;

    private void DependencyAssetsChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshAssetDependencies();
        else
            Dispatcher.UIThread.Post(RefreshAssetDependencies);
    }

    private void ContentsSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FastFileAssets.SelectedContentsRow) && SelectedEditorTab is null)
            RefreshAssetDependencies();
    }

    private WorkspaceAssetCatalogEntry? SelectedDependencyAsset()
    {
        if (SelectedEditorTab is not { } tab)
        {
            FastFileContentsRow? row = FastFileAssets.SelectedContentsRow;
            return row?.PoolSlot is { } slot
                ? _dependencyIndex?.FindActiveProvider(slot.ActiveProviderId.Value) ??
                    _selectionRouter.Resolve(slot.ToSelection()).CatalogEntry
                : row?.CatalogEntry?.Entry;
        }

        if (tab.Selection.ProviderId is { } selectedId &&
            _dependencyIndex?.FindActiveProvider(selectedId.Value) is { } exact)
            return exact;
        if (tab.Route?.CatalogEntry is { } entry)
        {
            if (entry.TargetRowIdentity is not { } identity)
                return entry;
            Editor.EditingSession.Document.TryGetRow(identity, out var live);
            return live;
        }

        // Package tabs identify a payload of an Image or Sound. Show its owning asset
        // only if the selected physical fastfile supplies that exact catalog provider.
        if (tab.StreamedImage is null && tab.StreamedSound is null)
            return null;
        string? file = tab.StreamedImage?.OwningFastFileName ?? tab.StreamedSound?.OwningFastFileName;
        var provider = AssetPool.Rows.FirstOrDefault(row => row.AssetType == tab.Selection.AssetType &&
            row.NormalizedName == tab.Selection.NormalizedName && row.ProviderFileName == file);
        return provider is null ? null : _dependencyIndex?.FindActiveProvider(provider.ActiveProviderId.Value) ??
            _selectionRouter.Resolve(provider.ToSelection()).CatalogEntry;
    }

    private void RefreshAssetDependencies()
    {
        if (_disposed)
            return;
        if (RightActiveToolId != StudioToolIds.AssetDependencies)
        {
            CancelDependencyBuild();
            if (_dependencyIndex is not null && _dependencyRevision != Editor.EditingSession.Revision)
                _dependencyIndex = null;
            AssetDependencies.SetSelection(null);
            return;
        }

        long revision = Editor.EditingSession.Revision;
        bool invalidated = revision != _dependencyRevision;
        if (invalidated)
        {
            CancelDependencyBuild();
            _dependencyIndex = null;
            _dependencyRevision = revision;
        }
        WorkspaceAssetCatalogEntry? entry = SelectedDependencyAsset();
        if (!invalidated && ReferenceEquals(entry, AssetDependencies.Selection) &&
            (_dependencyIndex is not null || _dependencyBuild is not null || AssetDependencies.HasError))
            return;
        AssetDependencies.SetSelection(entry);
        if (entry is null)
        {
            CancelDependencyBuild();
            return;
        }

        if (_dependencyIndex is { } index)
        {
            AssetDependencies.SetResults(index.UsedBy(entry), index.Uses(entry));
            return;
        }

        AssetDependencies.SetLoading();
        if (_dependencyBuild is null)
        {
            _dependencyBuild = new CancellationTokenSource();
            _ = LoadDependenciesAsync(_dependencyBuild, revision);
        }
    }

    private void CancelDependencyBuild()
    {
        _dependencyBuild?.Cancel();
        _dependencyBuild = null;
    }

    private async Task LoadDependenciesAsync(CancellationTokenSource request, long revision)
    {
        WorkspaceAssetDependencyIndex? index = null;
        string? error = null;
        try
        {
            index = await Task.Run(() => Editor.EditingSession.BuildAssetDependencyIndex(request.Token),
                request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(request, _dependencyBuild))
                    return;

                _dependencyBuild = null;
                if (revision != Editor.EditingSession.Revision)
                {
                    RefreshAssetDependencies();
                    return;
                }
                if (index is null)
                    AssetDependencies.SetError($"Could not read asset dependencies. {error}");
                else
                {
                    _dependencyIndex = index;
                    var entry = SelectedDependencyAsset();
                    AssetDependencies.SetSelection(entry);
                    if (entry is not null)
                        AssetDependencies.SetResults(index.UsedBy(entry), index.Uses(entry));
                }
            });
        }
        finally
        {
            request.Dispose();
        }
    }
}
