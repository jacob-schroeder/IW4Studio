using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewView : UserControl
{
    public MenuPreviewView() => AvaloniaXamlLoader.Load(this);

    private void PreviewControl_NodeSelected(
        object? sender,
        MenuPreviewNodeSelectedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.SelectPreviewNode(e.NodeId);
    }

    private void PreviewControl_MaterialResolutionCompleted(
        object? sender,
        MenuPreviewMaterialResolutionCompletedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.ReportMaterialPreviewStatus(e.Status);
    }

    private void PreviewControl_TextResolutionCompleted(
        object? sender,
        MenuPreviewTextResolutionCompletedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.ReportTextPreviewStatus(e.Status);
    }
}
