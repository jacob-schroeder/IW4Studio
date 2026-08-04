using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void PreviewControl_GeometryCommitted(
        object? sender,
        MenuPreviewGeometryCommittedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
        {
            _ = viewModel.CommitPreviewItemGeometry(
                e.NodeId,
                e.OriginalBounds,
                e.CandidateBounds);
        }
    }

    private void PreviewControl_TextResolutionCompleted(
        object? sender,
        MenuPreviewTextResolutionCompletedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.ReportTextPreviewStatus(e.Status);
    }

    private static void ScenarioStagedInput_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Control
            {
                DataContext: MenuPreviewScenarioInputViewModel input
            })
        {
            _ = input.CommitPendingValue();
        }
    }

    private static void ScenarioStagedInput_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: MenuPreviewScenarioInputViewModel input
            })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = input.CommitPendingValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            input.ResetPendingValue();
            e.Handled = true;
        }
    }
}
