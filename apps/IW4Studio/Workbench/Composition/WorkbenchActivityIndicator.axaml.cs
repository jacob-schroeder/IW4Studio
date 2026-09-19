using Avalonia;
using Avalonia.Controls;

namespace IW4.Studio.Desktop.Workbench.Composition;

public sealed partial class WorkbenchActivityIndicator : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<WorkbenchActivityIndicator, bool>(
            nameof(IsActive));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<WorkbenchActivityIndicator, string>(
            nameof(Label),
            string.Empty);

    public WorkbenchActivityIndicator() => InitializeComponent();

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
