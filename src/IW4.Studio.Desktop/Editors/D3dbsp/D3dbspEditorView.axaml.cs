using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IW4.Assets.D3dbsp;

namespace IW4.Studio.Desktop.Editors.D3dbsp;

public sealed partial class D3dbspEditorView : UserControl
{
    private bool _isImportInProgress;
    private bool _isExportInProgress;

    public D3dbspEditorView() => AvaloniaXamlLoader.Load(this);

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isImportInProgress || DataContext is not D3dbspEditorViewModel viewModel)
            return;

        _isImportInProgress = true;
        string? temporaryInputPath = null;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportFailure(
                    "D3DBSP import blocked: the desktop file picker is unavailable.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import compiled IW4 D3DBSP",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        D3dbspDesktopFileStorage.FileType,
                        FilePickerFileTypes.All
                    ]
                });
            IStorageFile? source = files.FirstOrDefault();
            if (source is null)
                return;

            (string inputPath, temporaryInputPath) =
                await D3dbspDesktopFileStorage.ResolveInputPathAsync(source);
            await viewModel.ImportAsync(inputPath, source.Name);
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportFailure($"D3DBSP import failed: {exception.Message}");
        }
        finally
        {
            D3dbspDesktopFileStorage.TryDelete(temporaryInputPath);
            _isImportInProgress = false;
        }
    }

    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isExportInProgress || DataContext is not D3dbspEditorViewModel viewModel)
            return;

        _isExportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportFailure(
                    "D3DBSP export blocked: the desktop file picker is unavailable.");
                return;
            }

            D3dbspFile? file = await viewModel.CreateExportAsync();
            if (file is null)
                return;

            IStorageFolder? downloads = await storage.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            IStorageFile? destination = await storage.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export compiled IW4 D3DBSP",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = Path.GetFileName(
                        viewModel.AssetName.Replace('\\', '/')),
                    DefaultExtension = "d3dbsp",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        D3dbspDesktopFileStorage.FileType,
                        FilePickerFileTypes.All
                    ]
                });
            if (destination is null)
            {
                viewModel.ReportExportCancelled();
                return;
            }

            await D3dbspDesktopFileStorage.WriteExportAsync(
                file,
                destination);
            viewModel.ReportExportSuccess(
                destination.TryGetLocalPath() ?? destination.Name);
        }
        catch (OperationCanceledException)
        {
            viewModel.ReportExportCancelled();
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or InvalidDataException or
                   InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            viewModel.ReportFailure($"D3DBSP export failed: {exception.Message}");
        }
        finally
        {
            if (viewModel.IsBusy)
            {
                viewModel.ReportFailure(
                    "D3DBSP export stopped before a file was written.");
            }
            _isExportInProgress = false;
        }
    }

}
