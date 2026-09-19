using IW4.Assets.Assets.Image;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

/// <summary>
/// Searchable, read-only view over every streamed GfxImage definition loaded
/// into the current workspace. Preview documents own their decode lifetime.
/// </summary>
public sealed class ImageFilePakToolViewModel : ObservableObject, IDisposable
{
    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly IReadOnlyList<ImageFilePakEntryViewModel> _allEntries;
    private readonly IReadOnlyDictionary<
        WorkbenchStreamedImageIdentity,
        ImageFilePakEntryViewModel> _entriesByIdentity;
    private IReadOnlyList<ImageFilePakEntryViewModel> _visibleEntries;
    private ImageFilePakEntryViewModel? _selectedEntry;
    private string _searchText = string.Empty;
    private bool _disposed;

    public ImageFilePakToolViewModel(
        FastFileWorkspace workspace,
        IWorkbenchSelectionContext selectionContext)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        _selectionContext = selectionContext
            ?? throw new ArgumentNullException(nameof(selectionContext));
        _allEntries = CaptureEntries(workspace);
        _entriesByIdentity = _allEntries.ToDictionary(entry => entry.Identity);
        _visibleEntries = _allEntries;
        _selectionContext.SelectionChanged +=
            SelectionContext_SelectionChanged;
    }

    public IReadOnlyList<ImageFilePakEntryViewModel> VisibleEntries
    {
        get => _visibleEntries;
        private set => SetProperty(ref _visibleEntries, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            RebuildProjection();
        }
    }

    public ImageFilePakEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            if (value is not null)
                _selectionContext.Select(value.ToSelection());
        }
    }

    public int TotalCount => _allEntries.Count;

    public int VisibleCount => VisibleEntries.Count;

    public bool HasEntries => VisibleCount != 0;

    public bool HasSelection => SelectedEntry is not null;

    public bool HasNoSelection => !HasSelection;

    public string ResultText => string.IsNullOrWhiteSpace(SearchText)
        ? $"{TotalCount:N0} streamed images"
        : $"{VisibleCount:N0} of {TotalCount:N0}";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _selectionContext.SelectionChanged -=
            SelectionContext_SelectionChanged;
    }

    internal ImageFilePakEntryViewModel RequireEntry(
        WorkbenchStreamedImageIdentity identity) =>
        _entriesByIdentity.TryGetValue(
            identity,
            out ImageFilePakEntryViewModel? entry)
            ? entry
            : throw new KeyNotFoundException(
                "The streamed image is not part of this workspace.");

    private static IReadOnlyList<ImageFilePakEntryViewModel> CaptureEntries(
        FastFileWorkspace workspace)
    {
        var entries = new List<ImageFilePakEntryViewModel>();
        foreach (WorkspaceZone zone in workspace.LoadedZones)
        {
            GfxImageAsset[] streamedImages = zone.LoadResult.Context
                .GfxImagesByAddress.Values.Distinct()
                .Where(image => image.StreamData.Any(part => part.HasStreamingData))
                .OrderBy(image => image.Offset).ThenBy(image => image.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (GfxImageAsset image in streamedImages)
            {
                entries.Add(new ImageFilePakEntryViewModel(
                    image,
                    zone.LoadResult.ImagePayloadResolver,
                    zone.PhysicalPath,
                    new WorkbenchStreamedImageIdentity(workspace.Document.DocumentId, entries.Count)));
            }
        }

        return Array.AsReadOnly(entries.ToArray());
    }

    private void RebuildProjection()
    {
        string query = SearchText.Trim();
        ImageFilePakEntryViewModel[] visible = query.Length == 0
            ? _allEntries.ToArray()
            : _allEntries
                .Where(entry => string.Join(
                        ' ',
                        entry.Name,
                        entry.OwningFastFileName,
                        entry.PackageText,
                        entry.DimensionsText)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        bool selectedEntryIsHidden =
            SelectedEntry is not null &&
            !visible.Contains(SelectedEntry);
        VisibleEntries = Array.AsReadOnly(visible);
        if (selectedEntryIsHidden)
        {
            _selectionContext.Clear(
                WorkbenchAssetSelectionSource.ImageFilePak);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ResultText));
    }

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        ImageFilePakEntryViewModel? desired =
            args.Current?.Identity.StreamedImageIdentity is { } identity &&
            _entriesByIdentity.TryGetValue(
                identity,
                out ImageFilePakEntryViewModel? entry)
                ? entry
                : null;
        if (ReferenceEquals(_selectedEntry, desired))
            return;

        _selectedEntry = desired;
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }
}
