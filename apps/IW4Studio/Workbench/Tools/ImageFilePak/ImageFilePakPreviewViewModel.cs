using Avalonia.Media.Imaging;
using IW4.Render.Textures;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

/// <summary>
/// Tab-owned streamed-image preview. Its decoded bitmap and cancellation
/// lifetime are independent from navigator selection and filtering.
/// </summary>
public sealed class ImageFilePakPreviewViewModel : ObservableObject, IDisposable
{
    private readonly ImageFilePakEntryViewModel _entry;
    private readonly CancellationTokenSource _cancellation = new();
    private Bitmap? _preview;
    private string _previewMessage = "Loading streamed image preview…";
    private bool _isPreviewLoading = true;
    private bool _disposed;

    internal ImageFilePakPreviewViewModel(ImageFilePakEntryViewModel entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _ = LoadPreviewAsync();
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

    public bool HasPreview => Preview is not null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        Preview = null;
    }

    private async Task LoadPreviewAsync()
    {
        PreviewLoadResult result;
        try
        {
            result = await Task.Run(
                () => PreviewLoadResult.Decode(_entry),
                _cancellation.Token);
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

        if (_disposed || _cancellation.IsCancellationRequested)
            return;

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
