using System.ComponentModel;
using Avalonia.Controls;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Ordered workbench document tab. Catalog-backed tabs retain their editor
/// host, while previews and fallback pages retain the scalar selection needed
/// to recreate their active presentation.
/// </summary>
public sealed class WorkbenchEditorTabViewModel : ObservableObject, IDisposable
{
    private WorkbenchAssetSelection _selection;
    private WorkbenchAssetSelectionRoute? _route;
    private readonly IDisposable? _ownedContent;

    internal WorkbenchEditorTabViewModel(
        WorkbenchEditorTabKey key,
        WorkbenchAssetSelection selection,
        WorkbenchAssetSelectionRoute? route,
        AssetEditorHostViewModel? catalogEditor,
        Control? standaloneView,
        ImageFilePakEntryViewModel? streamedImage,
        IDisposable? ownedContent)
    {
        if (catalogEditor is not null && standaloneView is not null)
        {
            throw new ArgumentException(
                "A workbench tab cannot host both a catalog editor and a standalone view.");
        }

        Key = key;
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _route = route;
        CatalogEditor = catalogEditor;
        StandaloneView = standaloneView;
        StreamedImage = streamedImage;
        _ownedContent = ownedContent;
        if (CatalogEditor is not null)
            CatalogEditor.PropertyChanged += CatalogEditor_PropertyChanged;
    }

    internal WorkbenchEditorTabKey Key { get; }

    public WorkbenchAssetSelection Selection => _selection;

    public AssetEditorHostViewModel? CatalogEditor { get; }

    public Control? StandaloneView { get; }

    public Control? HostedView => CatalogEditor?.HostedView ?? StandaloneView;

    public bool UsesWorkbenchScrollViewer =>
        CatalogEditor?.UsesWorkbenchScrollViewer != false;

    public ImageFilePakEntryViewModel? StreamedImage { get; }

    public string Title => Selection.DisplayName;

    public string Kind => Selection.AssetType.ToString();

    public string AccessBadge => Selection.Access switch
    {
        WorkspaceAssetAccess.Editable => "EDITABLE",
        WorkspaceAssetAccess.ReadOnly => "READ ONLY",
        WorkspaceAssetAccess.ContentUnavailable => "CONTENT UNAVAILABLE",
        _ => throw new InvalidDataException(
            $"Unknown workspace access '{Selection.Access}'.")
    };

    public string ProviderZone => Selection.ProviderZone ?? string.Empty;

    public string IconToken => Selection.Source switch
    {
        WorkbenchAssetSelectionSource.ImageFilePak => "ImageOutline",
        WorkbenchAssetSelectionSource.AssetPool => "DatabaseOutline",
        _ => "FileCodeOutline"
    };

    public string ToolTipText => string.Join(
        Environment.NewLine,
        Title,
        $"Type: {Kind}",
        $"Access: {AccessBadge}",
        string.IsNullOrWhiteSpace(ProviderZone)
            ? "Provider: workspace"
            : $"Provider: {ProviderZone}",
        IsDirty ? "Unapplied editor changes" : "Editor content applied");

    public bool IsDirty => CatalogEditor?.IsDirty == true;

    internal WorkbenchAssetSelectionRoute? Route => _route;

    internal void UpdateSelection(
        WorkbenchAssetSelection selection,
        WorkbenchAssetSelectionRoute? route)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (Equals(_selection, selection) && Equals(_route, route))
            return;

        _selection = selection;
        _route = route;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(AccessBadge));
        OnPropertyChanged(nameof(ProviderZone));
        OnPropertyChanged(nameof(IconToken));
        OnPropertyChanged(nameof(ToolTipText));
    }

    public void Dispose()
    {
        if (CatalogEditor is not null)
            CatalogEditor.PropertyChanged -= CatalogEditor_PropertyChanged;

        _ownedContent?.Dispose();
    }

    private void CatalogEditor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AssetEditorHostViewModel.IsDirty))
            return;

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(ToolTipText));
    }
}

internal readonly record struct WorkbenchEditorTabKey(
    AssetExplorerItemIdentity? CatalogIdentity,
    WorkbenchAssetSelectionIdentity? SelectionIdentity)
{
    public static WorkbenchEditorTabKey Create(
        WorkbenchAssetSelection selection,
        WorkbenchAssetSelectionRoute? route)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (route is { OpensCatalogEditor: true, CatalogEntry: { } entry })
        {
            return new WorkbenchEditorTabKey(
                AssetExplorerItemIdentity.From(entry),
                SelectionIdentity: null);
        }

        return new WorkbenchEditorTabKey(
            CatalogIdentity: null,
            selection.Identity);
    }
}

public sealed class WorkbenchEditorTabCloseRequestedEventArgs(
    WorkbenchEditorTabViewModel tab) : EventArgs
{
    public WorkbenchEditorTabViewModel Tab { get; } =
        tab ?? throw new ArgumentNullException(nameof(tab));
}

public sealed class WorkbenchEditorTabsCloseRequestedEventArgs(
    IEnumerable<WorkbenchEditorTabViewModel> tabs) : EventArgs
{
    public IReadOnlyList<WorkbenchEditorTabViewModel> Tabs { get; } =
        Array.AsReadOnly(
            (tabs ?? throw new ArgumentNullException(nameof(tabs))).ToArray());
}
