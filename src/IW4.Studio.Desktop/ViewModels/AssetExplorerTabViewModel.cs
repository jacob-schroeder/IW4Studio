using Avalonia.Controls;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// One tab/host projection. Closing it closes only the visual editor session;
/// the Step 06 editing session retains the detached draft for a later reopen.
/// </summary>
public sealed class AssetExplorerTabViewModel
{
    internal AssetExplorerTabViewModel(
        AssetExplorerEntryViewModel entry,
        AssetEditorSurface surface,
        AssetEditorViewHost? viewHost)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        ViewHost = viewHost;
    }

    public AssetExplorerEntryViewModel Entry { get; }

    public AssetEditorSurface Surface { get; }

    public AssetEditorViewHost? ViewHost { get; }

    public AssetEditorSession? BackendEditor => Surface as AssetEditorSession;

    public StructuralAssetInspector? StructuralInspector => Surface as StructuralAssetInspector;

    public Control? HostedView => ViewHost?.View;

    public object? HostedViewModel => ViewHost?.ViewModel;

    public bool HasHostedEditor => HostedView is not null;

    public string Title => Entry.Name;

    public string InspectorReason => StructuralInspector?.Reason
        ?? "The selected editor has no Desktop view host.";

    public void Dispose()
    {
        BackendEditor?.Close();
        if (HostedViewModel is IDisposable disposable)
            disposable.Dispose();
    }
}
