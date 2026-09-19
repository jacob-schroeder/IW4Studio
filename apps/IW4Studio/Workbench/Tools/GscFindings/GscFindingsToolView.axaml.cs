using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.Editors;

namespace IW4.Studio.Desktop.Workbench.Tools.GscFindings;

public sealed partial class GscFindingsToolView : UserControl
{
    public GscFindingsToolView() => AvaloniaXamlLoader.Load(this);

    private void FindingButton_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: EditorSourceDiagnostic finding } &&
            DataContext is GscFindingsToolViewModel viewModel)
        {
            viewModel.Activate(finding);
        }
    }
}
