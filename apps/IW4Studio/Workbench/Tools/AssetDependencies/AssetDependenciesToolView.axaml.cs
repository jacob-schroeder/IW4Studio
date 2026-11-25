using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Workbench.Tools.AssetDependencies;

public sealed partial class AssetDependenciesToolView : UserControl
{
    public AssetDependenciesToolView() => InitializeComponent();

    private void ScopeButton_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not AssetDependenciesToolViewModel viewModel)
            return;

        if (ReferenceEquals(sender, UsedByButton))
            viewModel.IsUsedByScope = true;
        else if (ReferenceEquals(sender, UsesButton))
            viewModel.IsUsesScope = true;
    }
}
