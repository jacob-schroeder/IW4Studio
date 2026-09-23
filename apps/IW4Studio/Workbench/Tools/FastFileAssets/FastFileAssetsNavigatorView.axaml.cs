using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.D3dbsp;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileAssetsNavigatorView : UserControl
{
    private bool _isD3dbspImportInProgress;
    private bool _isRawFileImportInProgress;
    private bool _contextIsRawFileGroup;
    private XAssetType? _contextAssetType;

    public FastFileAssetsNavigatorView() => InitializeComponent();

    private async void AddAssetMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner ||
            !viewModel.CanAddAssets)
        {
            return;
        }

        e.Handled = true;
        var dialog = new AddAssetDialogWindow(
            viewModel.AddableAssetTypes,
            _contextAssetType,
            viewModel.ValidateNewAssetName,
            viewModel.AddAsset);
        _ = await dialog.ShowDialog<bool>(owner);
    }

    private void AssetsGrid_ContextRequested(
        object? sender,
        ContextRequestedEventArgs e)
    {
        FastFileAssetNavigatorNode? node = (e.Source as Control)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<FastFileAssetNavigatorNode>()
            .FirstOrDefault();
        node ??= !e.TryGetPosition(this, out _) &&
            DataContext is FastFileAssetsNavigatorViewModel viewModel
                ? viewModel.SelectedNode
                : null;
        _contextAssetType = node?.AssetType;
        _contextIsRawFileGroup = node is
            { IsGroup: true, AssetType: XAssetType.RawFile };
        ImportRawFileFolderMenuItem.IsVisible = _contextIsRawFileGroup;
        ImportRawFileFolderMenuItem.IsEnabled = !_isRawFileImportInProgress;
    }

    private async void ImportRawFileFolderMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (!_contextIsRawFileGroup || _isRawFileImportInProgress ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        e.Handled = true;
        _isRawFileImportInProgress = true;
        ImportRawFileFolderMenuItem.IsEnabled = false;
        try
        {
            IReadOnlyList<IStorageFolder> folders =
                await owner.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "Import RawFiles — select the asset root folder",
                        AllowMultiple = false
                    });
            if (folders.Count == 0)
                return;

            string folderPath = folders[0].TryGetLocalPath()
                ?? throw new NotSupportedException(
                    "RawFile folder import requires a local folder.");
            await viewModel.ImportRawFileFolderAsync(folderPath);
        }
        catch (OperationCanceledException)
        {
            // Closing the editing session cancels pending file reads.
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or NotSupportedException or
                   ArgumentException or InvalidOperationException or OverflowException)
        {
            viewModel.ReportRawFileImportFailure(exception.Message);
        }
        finally
        {
            _isRawFileImportInProgress = false;
            ImportRawFileFolderMenuItem.IsEnabled = true;
        }
    }

    private async void ImportD3dbspMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isD3dbspImportInProgress ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        e.Handled = true;
        if (owner.StorageProvider is not { } storage)
        {
            viewModel.ReportD3dbspImportFailure(
                "the desktop file picker is unavailable.");
            return;
        }

        _isD3dbspImportInProgress = true;
        string? temporaryInputPath = null;
        try
        {
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
            string fileName = source.Name.EndsWith(
                    ".d3dbsp",
                    StringComparison.OrdinalIgnoreCase)
                ? source.Name
                : Path.GetFileNameWithoutExtension(source.Name) + ".d3dbsp";
            string suggestedAssetName = $"maps/mp/{fileName}";
            var dialog = new ImportD3dbspDialogWindow(
                inputPath,
                suggestedAssetName,
                viewModel.SuggestedD3dbspFragmentProgramUploadCapacity,
                viewModel.ImportD3dbspAsync);
            _ = await dialog.ShowDialog<bool>(owner);
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or NotSupportedException or
                   ArgumentException)
        {
            viewModel.ReportD3dbspImportFailure(exception.Message);
        }
        finally
        {
            D3dbspDesktopFileStorage.TryDelete(temporaryInputPath);
            _isD3dbspImportInProgress = false;
        }
    }
}
