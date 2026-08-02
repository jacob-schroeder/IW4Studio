using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.Diagnostics;

public sealed partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => AvaloniaXamlLoader.Load(this);

    private void DiagnosticsList_DoubleTapped(
        object? sender,
        TappedEventArgs args)
    {
        if (sender is ListBox { SelectedItem: WorkbenchDiagnostic diagnostic } &&
            DataContext is DiagnosticsAggregator diagnostics)
        {
            diagnostics.Activate(diagnostic);
        }
    }
}
