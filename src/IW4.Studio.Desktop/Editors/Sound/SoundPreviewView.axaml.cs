using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Sound;

public sealed partial class SoundPreviewView : UserControl
{
    private bool _isImportInProgress;
    private bool _isExportInProgress;

    public SoundPreviewView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundPreviewViewModel)?.PausePlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private void PlayPauseButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as SoundPreviewViewModel)?.TogglePlayback();

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isImportInProgress ||
            DataContext is not SoundPreviewViewModel viewModel ||
            !viewModel.TryCaptureImportTarget(out SoundImportTarget? target) ||
            target is null)
        {
            return;
        }

        _isImportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportImportFailure(
                    "The desktop file picker is unavailable.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import MPEG Layer III audio",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("MP3 audio")
                        {
                            Patterns = ["*.mp3", "*.MP3"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null)
                return;

            byte[] sourceBytes;
            await using (Stream stream = await file.OpenReadAsync())
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                sourceBytes = memory.ToArray();
            }

            SoundImportCandidate candidate = await Task.Run(() =>
                SoundImportCandidate.Compile(target, sourceBytes));
            _ = viewModel.TryStageImport(
                candidate,
                file.Name,
                out _);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            viewModel.ReportImportFailure(exception.Message);
        }
        finally
        {
            _isImportInProgress = false;
        }
    }

    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isExportInProgress ||
            DataContext is not SoundPreviewViewModel viewModel ||
            !viewModel.TryCaptureExport(out SoundExportPayload? export) ||
            export is null)
        {
            return;
        }

        _isExportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportExportFailure(
                    "The desktop file picker is unavailable.");
                return;
            }

            IStorageFile? destination = await storage.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export MPEG Layer III audio",
                    SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(
                        export.SuggestedFileName,
                        "mp3"),
                    DefaultExtension = "mp3",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("MP3 audio")
                        {
                            Patterns = ["*.mp3", "*.MP3"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            if (destination is null)
                return;

            string? localPath = destination.TryGetLocalPath();
            if (localPath is not null)
            {
                string directory = Path.GetDirectoryName(localPath) ??
                    throw new IOException(
                        "The selected destination has no parent directory.");
                string temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(localPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, export.Bytes);
                    File.Move(temporaryPath, localPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            else
            {
                await using Stream stream = await destination.OpenWriteAsync();
                if (!stream.CanSeek)
                {
                    throw new NotSupportedException(
                        "The selected storage destination does not support a safe replacement write.");
                }
                stream.SetLength(0);
                stream.Position = 0;
                await stream.WriteAsync(export.Bytes);
                await stream.FlushAsync();
            }

            viewModel.ReportExportSuccess(
                destination.TryGetLocalPath() ?? destination.Name);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is
                   IOException or
                   UnauthorizedAccessException or
                   NotSupportedException or
                   ArgumentException or
                   InvalidOperationException)
        {
            viewModel.ReportExportFailure(exception.Message);
        }
        finally
        {
            _isExportInProgress = false;
        }
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as SoundPreviewViewModel)?.ApplyImportedPayload();

    private void RevertButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as SoundPreviewViewModel)?.RevertSound();
}
