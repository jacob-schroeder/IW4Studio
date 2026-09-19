using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.GscUsages;

public sealed partial class GscUsagesToolView : UserControl
{
    public GscUsagesToolView() => AvaloniaXamlLoader.Load(this);

    private void UsagesList_DoubleTapped(
        object? sender,
        TappedEventArgs args)
    {
        if (sender is ListBox { SelectedItem: GscUsagePresentationItem usage } &&
            DataContext is GscUsagesToolViewModel viewModel)
        {
            viewModel.Activate(usage);
        }
    }
}
