using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using IW4.Game.Assets;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.D3dbsp;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileContentsView : UserControl
{
    private bool _isD3dbspImportInProgress;
    private bool _isRawFileImportInProgress;
    private bool _isAssetExportInProgress;
    private XAssetType? _contextAssetType;
    private AssetExplorerEntryViewModel? _contextRow;
    private FastFileContentsRow? _contextContentsRow;

    public FastFileContentsView() => InitializeComponent();

    private void ScopeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            sender is not ToggleButton button)
            return;

        if (ReferenceEquals(button, ThisFastFileScopeButton))
            viewModel.IsThisFastFileScope = true;
        else if (ReferenceEquals(button, DependenciesScopeButton))
            viewModel.IsDependenciesScope = true;
        else if (ReferenceEquals(button, AssetPoolScopeButton))
            viewModel.IsAssetPoolScope = true;
        button.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
    }

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
            viewModel.IsThisFastFileScope ? viewModel.SelectedCategory?.AssetType : null,
            viewModel.ValidateNewAssetName,
            viewModel.AddAsset);
        _ = await dialog.ShowDialog<bool>(owner);
    }

    private void Contents_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel)
            return;

        FastFileContentsRow? row = (e.Source as Control)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<FastFileContentsRow>()
            .FirstOrDefault();
        row ??= !e.TryGetPosition(this, out _) ? viewModel.SelectedContentsRow : null;
        PrepareContentsMenu(viewModel, row);
    }

    private void PrepareContentsMenu(
        FastFileAssetsNavigatorViewModel viewModel,
        FastFileContentsRow? row)
    {
        _contextContentsRow = row;
        _contextRow = row?.CatalogEntry;
        _contextAssetType = row?.AssetType ?? viewModel.SelectedCategory?.AssetType;
        ExportAssetMenuItem.IsVisible = row?.PoolSlot is null && CanExportAsset(_contextRow);
        ExportAssetMenuItem.IsEnabled = !_isAssetExportInProgress;
        ImportAssetMenuItem.IsVisible = viewModel.IsThisFastFileScope &&
            (row is null || row.CatalogEntry is { IsTargetRow: true } &&
                AssetSourceTransfer.CanImport(row.AssetType));
        ImportRawFileFolderMenuItem.IsVisible = viewModel.IsThisFastFileScope &&
            viewModel.SelectedCategory?.AssetType == XAssetType.RawFile;
        ImportRawFileFolderMenuItem.IsEnabled = !_isRawFileImportInProgress;
        UpdateAuthoringMenu(viewModel, row);
    }

    private void ContentsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        FastFileContentsRow? row = (e.Source as Control)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<FastFileContentsRow>()
            .FirstOrDefault();
        if (row is not null && DataContext is FastFileAssetsNavigatorViewModel viewModel)
        {
            viewModel.OpenContentsRow(row);
            e.Handled = true;
        }
    }

    private void ContentsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter)
            return;

        OpenSelectedRow();
        e.Handled = true;
    }

    private void OpenSelectedRow()
    {
        if (DataContext is FastFileAssetsNavigatorViewModel viewModel &&
            ContentsList.SelectedItem is FastFileContentsRow row)
            viewModel.OpenContentsRow(row);
    }

    private async void ExportAssetMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_isAssetExportInProgress || _contextRow is not { Identity.TargetRowIdentity: { } identity } row ||
            !CanExportAsset(row) ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        _isAssetExportInProgress = true;
        try
        {
            BaseAsset definition = viewModel.CaptureAssetSourceDefinition(identity);
            string fileName = AssetSourceTransfer.SuggestExportFileName(definition);
            byte[] content = await Task.Run(() => AssetSourceTransfer.Write(definition));
            IStorageFile? destination = await AssetSourceFilePicker.PickDestinationAsync(
                owner.StorageProvider, row.AssetType, fileName);
            if (destination is null)
                return;

            await AssetExportFile.WriteAsync(destination, content);
            viewModel.ReportAssetSourceExport($"Exported {row.Name} to {destination.Name}.");
        }
        catch (OperationCanceledException)
        {
            // The picker was dismissed or its owner closed.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or NotSupportedException or
            ArgumentException or OverflowException)
        {
            viewModel.ReportAssetSourceExport($"Asset export failed. {exception.Message}");
        }
        finally
        {
            _isAssetExportInProgress = false;
        }
    }

    private async void ImportAssetMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            !viewModel.IsThisFastFileScope ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        bool fromRowContext = ReferenceEquals(sender, ImportAssetMenuItem);
        XAssetType? categoryType = viewModel.SelectedCategory?.AssetType;
        XAssetType? preferredType =
            (fromRowContext ? _contextAssetType : categoryType) is { } type &&
            AssetSourceTransfer.CanImport(type) ? type : null;
        string? preferredName = fromRowContext && preferredType is not null &&
            _contextRow is
            { Origin: WorkspaceAssetOrigin.TargetOwnedDefinition, Access: WorkspaceAssetAccess.Editable, Entry.IsGeneratedConfigStringBaseline: false } row
                ? row.Entry.OriginalName : null;
        var dialog = new ImportAssetDialogWindow(viewModel, preferredType, preferredName);
        _ = await dialog.ShowDialog<bool>(owner);
    }

    private static bool CanExportAsset(AssetExplorerEntryViewModel? row) => row is
        { Origin: WorkspaceAssetOrigin.TargetOwnedDefinition, Access: WorkspaceAssetAccess.Editable, Entry.IsGeneratedConfigStringBaseline: false } &&
        AssetSourceTransfer.Supports(row.AssetType);

    private async void ImportRawFileFolderMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isRawFileImportInProgress ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            !viewModel.IsThisFastFileScope ||
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
            !viewModel.IsThisFastFileScope ||
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
