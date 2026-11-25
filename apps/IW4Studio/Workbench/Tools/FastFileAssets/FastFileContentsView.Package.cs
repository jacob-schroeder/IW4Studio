using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileContentsView
{
    private async void ExportPackageMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPackageOperationInProgress ||
            _contextContentsRow is not { } clicked ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        _isPackageOperationInProgress = true;
        try
        {
            var session = viewModel.EditingSession;
            var selected = ContentsList.SelectedItems?.OfType<FastFileContentsRow>().ToArray() ?? [];
            if (!selected.Contains(clicked)) selected = [clicked];
            var graph = selected.Any(row => row.PoolSlot is not null)
                ? await Task.Run(() => session.BuildAssetDependencyIndex(session.CancellationToken)) : null;
            var entries = selected.Select(row => (row.PoolSlot is { } slot
                ? graph?.FindActiveProvider(slot.ActiveProviderId.Value) : row.CatalogEntry?.Entry) ??
                throw new InvalidOperationException($"The provider for {row.Name} is unavailable.")).ToArray();
            await AssetPackageDesktopFlow.ExportAsync(owner, session, entries, viewModel.ReportAssetSourceExport);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            viewModel.ReportAssetSourceExport($"Package export failed. {exception.Message}");
        }
        finally
        {
            _isPackageOperationInProgress = false;
        }
    }

    private async void ImportPackageMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPackageOperationInProgress ||
            DataContext is not FastFileAssetsNavigatorViewModel { IsThisFastFileScope: true } viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        _isPackageOperationInProgress = true;
        try
        {
            await AssetPackageDesktopFlow.ImportAsync(
                owner, viewModel.EditingSession, viewModel.ApplyPackageImport,
                viewModel.ReportAssetSourceExport);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            viewModel.ReportAssetSourceExport($"Package import failed. {exception.Message}");
        }
        finally
        {
            _isPackageOperationInProgress = false;
        }
    }
}
