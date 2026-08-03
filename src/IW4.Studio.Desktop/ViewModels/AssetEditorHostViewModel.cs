using System.ComponentModel;
using Avalonia.Controls;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// One catalog editor host. Releasing it closes only the visual editor session;
/// the editing session retains the detached draft for a later reopen.
/// </summary>
public sealed class AssetEditorHostViewModel : ObservableObject
{
    private readonly INotifyPropertyChanged? _hostedPropertySource;
    private bool _isDirty;

    internal AssetEditorHostViewModel(
        AssetExplorerEntryViewModel entry,
        AssetEditorSurface surface,
        AssetEditorViewHost? viewHost)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        ViewHost = viewHost;
        _hostedPropertySource = viewHost?.ViewModel as INotifyPropertyChanged;
        if (_hostedPropertySource is not null)
            _hostedPropertySource.PropertyChanged += HostedViewModel_PropertyChanged;
        _isDirty = ReadDirtyState();
    }

    public AssetExplorerEntryViewModel Entry { get; }

    public AssetEditorSurface Surface { get; }

    public AssetEditorViewHost? ViewHost { get; }

    public AssetEditorSession? BackendEditor => Surface as AssetEditorSession;

    public StructuralAssetInspector? StructuralInspector => Surface as StructuralAssetInspector;

    public Control? HostedView => ViewHost?.View;

    public object? HostedViewModel => ViewHost?.ViewModel;

    public bool HasHostedEditor => HostedView is not null;

    public bool IsDirty => _isDirty;

    public string Title => Entry.Name;

    public string InspectorReason => StructuralInspector?.Reason
        ?? "The selected editor has no Desktop view host.";

    public void Dispose()
    {
        if (_hostedPropertySource is not null)
            _hostedPropertySource.PropertyChanged -= HostedViewModel_PropertyChanged;

        BackendEditor?.Close();
        if (HostedViewModel is IDisposable disposable)
            disposable.Dispose();
    }

    internal void RefreshState()
    {
        bool isDirty = ReadDirtyState();
        SetProperty(ref _isDirty, isDirty, nameof(IsDirty));
    }

    private void HostedViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) =>
        RefreshState();

    private bool ReadDirtyState() =>
        BackendEditor?.HasUnsavedChanges == true;
}
