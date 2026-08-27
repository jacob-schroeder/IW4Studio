using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.AssetExchange.SourceFormat.Image;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Material;

public sealed partial class MaterialEditorView : UserControl
{
    private bool _isImportInProgress;
    private bool _isExportInProgress;

    public MaterialEditorView() => AvaloniaXamlLoader.Load(this);

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isImportInProgress ||
            DataContext is not MaterialEditorViewModel viewModel ||
            !viewModel.TryCaptureImportTarget(
                out MaterialImageImportTarget? target) ||
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
                    Title = "Import Material texture",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("IW4 IWI or DDS images")
                        {
                            Patterns = ["*.iwi", "*.IWI", "*.dds", "*.DDS"]
                        },
                        new FilePickerFileType("IW4 IWI images")
                        {
                            Patterns = ["*.iwi", "*.IWI"]
                        },
                        new FilePickerFileType("DirectDraw Surface images")
                        {
                            Patterns = ["*.dds", "*.DDS"]
                        }
                    ]
                });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null)
                return;

            ImageFileFormat format = RequireFormat(file.Name);
            byte[] sourceBytes;
            await using (Stream stream = await file.OpenReadAsync())
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                sourceBytes = memory.ToArray();
            }

            MaterialImageImportCandidate candidate = await Task.Run(() =>
            {
                using var stream = new MemoryStream(sourceBytes, writable: false);
                ImageFileDocument document = new ImageExchange().Read(
                    stream,
                    format);
                return MaterialEditorViewModel.CompileImport(target, document);
            });
            _ = viewModel.TryStageImport(
                candidate,
                file.TryGetLocalPath() ?? file.Name,
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

    private void ExportIwiMenuItem_Click(
        object? sender,
        RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(
            async () => await ExportAsync(ImageFileFormat.Iwi8),
            DispatcherPriority.Background);

    private void ExportDdsMenuItem_Click(
        object? sender,
        RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(
            async () => await ExportAsync(ImageFileFormat.Dds),
            DispatcherPriority.Background);

    private async Task ExportAsync(ImageFileFormat format)
    {
        if (_isExportInProgress ||
            DataContext is not MaterialEditorViewModel viewModel ||
            !viewModel.TryCaptureExport(out MaterialImageExportTarget? target) ||
            target is null)
        {
            return;
        }

        _isExportInProgress = true;
        try
        {
            MaterialImageExportPayload export = await Task.Run(() =>
                viewModel.CreateExport(target, format));
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportExportFailure(
                    "The desktop file picker is unavailable.");
                return;
            }

            string extension = format == ImageFileFormat.Iwi8 ? "iwi" : "dds";
            string formatName = format == ImageFileFormat.Iwi8
                ? "IW4 IWI images"
                : "DirectDraw Surface images";
            IStorageFile? destination = await storage.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = format == ImageFileFormat.Iwi8
                        ? "Export Material texture as IW4 IWI"
                        : "Export Material texture as DDS",
                    SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(
                        export.SuggestedFileName,
                        extension),
                    DefaultExtension = extension,
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType(formatName)
                        {
                            Patterns = [$"*.{extension}", $"*.{extension.ToUpperInvariant()}"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            if (destination is null)
                return;

            await WriteSafelyAsync(destination, export.Bytes);
            viewModel.ReportExportSuccess(
                destination.TryGetLocalPath() ?? destination.Name,
                format);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            viewModel.ReportExportFailure(exception.Message);
        }
        finally
        {
            _isExportInProgress = false;
        }
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MaterialEditorViewModel)?.ApplyChanges();

    private void RevertButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MaterialEditorViewModel)?.RevertChanges();

    private static ImageFileFormat RequireFormat(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".iwi" => ImageFileFormat.Iwi8,
            ".dds" => ImageFileFormat.Dds,
            _ => throw new InvalidDataException(
                "Material image import requires an .iwi or .dds file.")
        };

    private static async Task WriteSafelyAsync(
        IStorageFile destination,
        ReadOnlyMemory<byte> bytes)
    {
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
                await File.WriteAllBytesAsync(temporaryPath, bytes.ToArray());
                File.Move(temporaryPath, localPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            return;
        }

        await using Stream stream = await destination.OpenWriteAsync();
        if (!stream.CanSeek)
        {
            throw new NotSupportedException(
                "The selected storage destination does not support a safe replacement write.");
        }
        stream.SetLength(0);
        stream.Position = 0;
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
