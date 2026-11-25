using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileContentsView
{
    private void SelectedAssetActions_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            viewModel.SelectedContentsRow is not { } row)
            return;

        PrepareContentsMenu(viewModel, row);
        AssetTable.ContextFlyout?.ShowAt(button);
        e.Handled = true;
    }

    private void UpdateAuthoringMenu(FastFileAssetsNavigatorViewModel viewModel, FastFileContentsRow? row)
    {
        OpenContentsMenuItem.IsVisible = row is not null;
        OpenContentsMenuItem.Header = row?.CatalogEntry is
            { IsTargetRow: true, HasUsableEditor: true, Access: WorkspaceAssetAccess.Editable } &&
            row.PoolSlot is null ? "Edit" : "Open";
        OverrideWeaponMenuItem.IsVisible = viewModel.CanOverrideWeapon(row);
        AddReferenceMenuItem.IsVisible = viewModel.CanAddReference(row);
    }

    private void OpenContentsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FastFileAssetsNavigatorViewModel viewModel && _contextContentsRow is { } row)
            OpenRow(viewModel, row);
    }

    private void CopyWeapon_Click(object? sender, RoutedEventArgs e) =>
        AuthorSelectedAsset(useReference: false);

    private void AddReference_Click(object? sender, RoutedEventArgs e) =>
        AuthorSelectedAsset(useReference: true);

    private async void UseWeaponFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            _contextContentsRow is not { } row || !viewModel.CanOverrideWeapon(row) ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Choose a weapon file for {row.Name}",
                AllowMultiple = false
            });
            if (files.Count == 0)
                return;
            string path = files[0].TryGetLocalPath() ??
                throw new NotSupportedException("Choose a local WEAPONFILE source.");
            viewModel.OverrideWeapon(row, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsAssetActionError(ex))
        {
            viewModel.ReportAssetSourceExport(ex.Message);
        }
    }

    private void AuthorSelectedAsset(bool useReference)
    {
        if (DataContext is not FastFileAssetsNavigatorViewModel viewModel || _contextContentsRow is not { } row)
            return;

        try
        {
            if (useReference)
                viewModel.AddReference(row);
            else
                viewModel.OverrideWeapon(row, null);
        }
        catch (Exception ex) when (IsAssetActionError(ex))
        {
            viewModel.ReportAssetSourceExport(ex.Message);
        }
    }

    private static bool IsAssetActionError(Exception error) => error is
        IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or
        NotSupportedException or ArgumentException or FormatException or OverflowException or KeyNotFoundException;
}
