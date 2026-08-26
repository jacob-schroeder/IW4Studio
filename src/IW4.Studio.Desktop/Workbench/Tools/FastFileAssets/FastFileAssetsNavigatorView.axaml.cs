using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.D3dbsp;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileAssetsNavigatorView : UserControl
{
    private bool _isD3dbspImportInProgress;
    private XAssetType? _contextAssetType;

    public FastFileAssetsNavigatorView() => AvaloniaXamlLoader.Load(this);

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
        if (e.Source is Control
            {
                DataContext: FastFileAssetNavigatorNode node
            })
        {
            _contextAssetType = node.AssetType;
            return;
        }

        _contextAssetType = !e.TryGetPosition(this, out _) &&
            DataContext is FastFileAssetsNavigatorViewModel viewModel
                ? viewModel.SelectedNode?.AssetType
                : null;
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
