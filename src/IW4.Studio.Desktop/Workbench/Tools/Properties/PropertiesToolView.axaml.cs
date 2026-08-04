using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.Editors.Inspector;

namespace IW4.Studio.Desktop.Workbench.Tools.Properties;

public sealed partial class PropertiesToolView : UserControl
{
    public PropertiesToolView() => AvaloniaXamlLoader.Load(this);

    private static void StagedInput_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Control { DataContext: IInspectorStagedPropertyRow row })
            _ = row.CommitInput();
    }

    private static void StagedInput_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: IInspectorStagedPropertyRow row
            })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = row.CommitInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.ResetInput();
            e.Handled = true;
        }
    }

    private async void ColorPreview_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: InspectorColorPropertyRowViewModel row
            } ||
            !row.IsEditable ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new InspectorColorPickerWindow(
            row.Label,
            row.CurrentValue);
        InspectorColorValue? selection =
            await dialog.ShowDialog<InspectorColorValue?>(owner);
        if (selection is { } color)
            _ = row.SetValue(color);
    }
}
