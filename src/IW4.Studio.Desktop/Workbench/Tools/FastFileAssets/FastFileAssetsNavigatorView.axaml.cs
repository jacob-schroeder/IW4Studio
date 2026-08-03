using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileAssetsNavigatorView : UserControl
{
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
            viewModel.ValidateNewAssetName,
            viewModel.AddAsset);
        _ = await dialog.ShowDialog<bool>(owner);
    }
}
