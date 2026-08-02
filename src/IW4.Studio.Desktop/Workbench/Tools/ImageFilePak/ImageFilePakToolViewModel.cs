using Avalonia.Media.Imaging;
using IW4.Assets.Assets.Image;
using IW4.Render.Textures;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

/// <summary>
/// Searchable, read-only view over every streamed GfxImage definition loaded
/// into the current workspace. Preview decoding is deferred until selection.
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
    private Bitmap? _preview;
    private CancellationTokenSource? _previewCancellation;
    private string _searchText = string.Empty;
    private string _previewMessage = "Select a streamed image to preview it.";
    private bool _isPreviewLoading;
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
            BeginPreviewLoad(value);
        }
    }

    public Bitmap? Preview
    {
        get => _preview;
        private set
        {
            Bitmap? previous = _preview;
            if (!SetProperty(ref _preview, value))
                return;

            previous?.Dispose();
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetProperty(ref _isPreviewLoading, value);
    }

    public int TotalCount => _allEntries.Count;

    public int VisibleCount => VisibleEntries.Count;

    public bool HasEntries => VisibleCount != 0;

    public bool HasSelection => SelectedEntry is not null;

    public bool HasNoSelection => !HasSelection;

    public bool HasPreview => Preview is not null;

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
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        Preview = null;
    }

    private static IReadOnlyList<ImageFilePakEntryViewModel> CaptureEntries(
        FastFileWorkspace workspace)
    {
        var entries = new List<ImageFilePakEntryViewModel>();
        foreach (WorkspaceZone zone in workspace.LoadedZones)
        {
            GfxImageAsset[] streamedImages = zone.LoadResult.Context
                .GfxImagesByAddress
                .Values
                .Distinct()
                .Where(image =>
                    image.StreamData.Any(part => part.HasStreamingData))
                .OrderBy(image => image.Offset)
                .ThenBy(image => image.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (GfxImageAsset image in streamedImages)
            {
                entries.Add(new ImageFilePakEntryViewModel(
                    image,
                    zone.LoadResult.ImagePayloadResolver,
                    zone.PhysicalPath,
                    new WorkbenchStreamedImageIdentity(
                        workspace.Document.DocumentId,
                        entries.Count)));
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
        BeginPreviewLoad(desired);
    }

    private void BeginPreviewLoad(ImageFilePakEntryViewModel? entry)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        Preview = null;

        if (entry is null)
        {
            IsPreviewLoading = false;
            PreviewMessage = "Select a streamed image to preview it.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        IsPreviewLoading = true;
        PreviewMessage = "Loading streamed image preview…";
        _ = LoadPreviewAsync(entry, cancellation);
    }

    private async Task LoadPreviewAsync(
        ImageFilePakEntryViewModel entry,
        CancellationTokenSource cancellation)
    {
        PreviewLoadResult result;
        try
        {
            result = await Task.Run(
                () => PreviewLoadResult.Decode(entry),
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = new PreviewLoadResult(
                false,
                null,
                $"Preview could not be decoded: {exception.Message}");
        }

        if (_disposed ||
            cancellation.IsCancellationRequested ||
            !ReferenceEquals(_previewCancellation, cancellation))
        {
            return;
        }

        _previewCancellation = null;
        cancellation.Dispose();
        IsPreviewLoading = false;
        if (!result.Success || result.Preview is null)
        {
            PreviewMessage = result.Reason;
            return;
        }

        try
        {
            using var stream = new MemoryStream(
                result.Preview.GetPngBytesCopy(),
                writable: false);
            Preview = new Bitmap(stream);
            PreviewMessage =
                $"{result.Preview.Width:N0} × {result.Preview.Height:N0} · " +
                result.Preview.Format;
        }
        catch (Exception exception)
        {
            PreviewMessage = $"Preview could not be created: {exception.Message}";
        }
    }

    private sealed record PreviewLoadResult(
        bool Success,
        GfxImagePreviewSnapshot? Preview,
        string Reason)
    {
        public static PreviewLoadResult Decode(
            ImageFilePakEntryViewModel entry) =>
            entry.TryDecodePreview(
                out GfxImagePreviewSnapshot? preview,
                out string reason)
                ? new PreviewLoadResult(true, preview, string.Empty)
                : new PreviewLoadResult(false, null, reason);
    }
}
