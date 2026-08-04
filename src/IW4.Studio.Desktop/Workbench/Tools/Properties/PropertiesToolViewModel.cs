using System.ComponentModel;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

namespace IW4.Studio.Desktop.Workbench.Tools.Properties;

/// <summary>
/// Stable metadata projection for the current workbench selection. Properties
/// are supplied explicitly by the selection contract; arbitrary editor view
/// models are never reflected.
/// </summary>
public sealed class PropertiesToolViewModel : ObservableObject, IDisposable
{
    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly ImageFilePakToolViewModel? _imageFilePak;
    private WorkbenchAssetSelection? _selection;
    private ImageFilePakEntryViewModel? _streamedImage;
    private IAssetEditorProperties? _editorPropertiesSource;
    private IAssetEditorInspectorSource? _editorInspectorSource;
    private bool _disposed;

    public PropertiesToolViewModel(
        IWorkbenchSelectionContext selectionContext,
        ImageFilePakToolViewModel? imageFilePak = null)
    {
        _selectionContext = selectionContext
            ?? throw new ArgumentNullException(nameof(selectionContext));
        _imageFilePak = imageFilePak;
        _selection = selectionContext.Current;
        _streamedImage = _selection?.Source ==
            WorkbenchAssetSelectionSource.ImageFilePak
                ? _imageFilePak?.SelectedEntry
                : null;
        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
    }

    public bool HasSelection => _selection is not null;

    public bool HasNoSelection => !HasSelection;

    public string Name => _selection?.DisplayName ?? "No selection";

    public string AssetType => _selection?.AssetType.ToString() ?? "—";

    public string Navigator => _selection?.Source switch
    {
        WorkbenchAssetSelectionSource.FastFileAssets => "Fastfile Assets",
        WorkbenchAssetSelectionSource.AssetPool => "Asset Pool",
        WorkbenchAssetSelectionSource.ImageFilePak =>
            "Imagefile.pak Viewer",
        _ => "—"
    };

    public string ProviderZone =>
        string.IsNullOrWhiteSpace(_selection?.ProviderZone)
            ? "—"
            : _selection!.ProviderZone!;

    public string Access => _selection?.Source switch
    {
        WorkbenchAssetSelectionSource.AssetPool => "Runtime inspection",
        WorkbenchAssetSelectionSource.ImageFilePak => "Read-only stream",
        _ => _selection?.Access.ToString() ?? "—"
    };

    public string Origin => _selection?.Origin ?? "—";

    public string Editor => _selection is null
        ? "—"
        : _selection.HasEditor
            ? $"{_selection.AssetType} Editor"
            : "No editor implemented";

    public string Identity => _selection?.Identity switch
    {
        { TargetRowIdentity: { } target } =>
            $"Target row #{target.SerializedIndex:N0}",
        { AssetPoolAddress: { } address } =>
            $"Pool slot {address.Slot:N0} · " +
            $"0x{unchecked((uint)address.RawValue):X8}",
        { StreamedImageIdentity: { } streamed } =>
            $"Streamed image #{streamed.Ordinal:N0}",
        _ => "—"
    };

    public ImageFilePakEntryViewModel? StreamedImage =>
        _streamedImage;

    public bool HasStreamedImageDetails =>
        StreamedImage is not null;

    public bool HasEditorProperties =>
        HasSelection &&
        _editorPropertiesSource?.EditorProperties.Count > 0;

    public string EditorPropertySectionName =>
        _editorPropertiesSource?.PropertySectionName ?? string.Empty;

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
        _editorPropertiesSource?.EditorProperties ?? [];

    public bool HasInspector =>
        HasSelection && InspectorSelection is not null;

    public InspectorSelectionViewModel? InspectorSelection =>
        _editorInspectorSource?.InspectorSelection;

    /// <summary>
    /// Attaches all explicit Properties projections supplied by one hosted
    /// editor. This is the preferred composition seam for new editors.
    /// </summary>
    public void SetEditorSource(object? source)
    {
        SetEditorPropertiesSource(source as IAssetEditorProperties);
        SetEditorInspectorSource(source as IAssetEditorInspectorSource);
    }

    public void SetEditorPropertiesSource(IAssetEditorProperties? source)
    {
        if (ReferenceEquals(_editorPropertiesSource, source))
            return;

        if (_editorPropertiesSource is not null)
        {
            _editorPropertiesSource.PropertyChanged -=
                EditorPropertiesSource_PropertyChanged;
        }

        _editorPropertiesSource = source;
        if (_editorPropertiesSource is not null)
        {
            _editorPropertiesSource.PropertyChanged +=
                EditorPropertiesSource_PropertyChanged;
        }

        NotifyEditorPropertiesChanged();
    }

    public void SetEditorInspectorSource(IAssetEditorInspectorSource? source)
    {
        if (ReferenceEquals(_editorInspectorSource, source))
            return;

        if (_editorInspectorSource is not null)
        {
            _editorInspectorSource.PropertyChanged -=
                EditorInspectorSource_PropertyChanged;
        }

        _editorInspectorSource = source;
        if (_editorInspectorSource is not null)
        {
            _editorInspectorSource.PropertyChanged +=
                EditorInspectorSource_PropertyChanged;
        }

        NotifyEditorInspectorChanged();
    }

    internal void SetDocumentSelection(
        WorkbenchAssetSelection? selection,
        ImageFilePakEntryViewModel? streamedImage)
    {
        if (Equals(_selection, selection) &&
            ReferenceEquals(_streamedImage, streamedImage))
        {
            return;
        }

        _selection = selection;
        _streamedImage = streamedImage;
        SetEditorPropertiesSource(null);
        SetEditorInspectorSource(null);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(AssetType));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(ProviderZone));
        OnPropertyChanged(nameof(Access));
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(StreamedImage));
        OnPropertyChanged(nameof(HasStreamedImageDetails));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _selectionContext.SelectionChanged -= SelectionContext_SelectionChanged;
        if (_editorPropertiesSource is not null)
        {
            _editorPropertiesSource.PropertyChanged -=
                EditorPropertiesSource_PropertyChanged;
            _editorPropertiesSource = null;
        }
        if (_editorInspectorSource is not null)
        {
            _editorInspectorSource.PropertyChanged -=
                EditorInspectorSource_PropertyChanged;
            _editorInspectorSource = null;
        }
    }

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        SetDocumentSelection(
            args.Current,
            args.Current?.Source == WorkbenchAssetSelectionSource.ImageFilePak
                ? _imageFilePak?.SelectedEntry
                : null);
    }

    private void EditorPropertiesSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) =>
        NotifyEditorPropertiesChanged();

    private void EditorInspectorSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) =>
        NotifyEditorInspectorChanged();

    private void NotifyEditorPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasEditorProperties));
        OnPropertyChanged(nameof(EditorPropertySectionName));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void NotifyEditorInspectorChanged()
    {
        OnPropertyChanged(nameof(HasInspector));
        OnPropertyChanged(nameof(InspectorSelection));
    }
}
