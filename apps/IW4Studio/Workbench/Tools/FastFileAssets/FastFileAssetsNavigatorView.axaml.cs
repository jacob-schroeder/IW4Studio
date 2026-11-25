using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileAssetsNavigatorView : UserControl
{
    public FastFileAssetsNavigatorView()
    {
        InitializeComponent();
        CategoryList.AddHandler(PointerPressedEvent, Categories_PointerPressed, RoutingStrategies.Tunnel);
    }

    private void AllTypes_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FastFileAssetsNavigatorViewModel viewModel)
        {
            if (sender is ToggleButton button)
                button.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            viewModel.SelectAllTypes();
            e.Handled = true;
        }
    }

    private void Categories_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel)
            return;

        FastFileAssetNavigatorGroup? group = (e.Source as Control)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<FastFileAssetNavigatorGroup>()
            .FirstOrDefault();
        if (group is not null && ReferenceEquals(group, viewModel.SelectedCategory))
            viewModel.RequestContents();
    }

    private void Categories_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            DataContext is not FastFileAssetsNavigatorViewModel viewModel ||
            viewModel.SelectedCategory is null)
            return;

        viewModel.RequestContents();
        e.Handled = true;
    }
}
