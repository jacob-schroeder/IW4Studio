using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Inspector;

public sealed partial class InspectorColorPickerWindow : Window
{
    public InspectorColorPickerWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
    }

    internal InspectorColorPickerWindow(
        string propertyName,
        InspectorColorValue initialValue)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        Title = $"Select {propertyName} color";
        ColorTitle.Text = $"{propertyName} color";
        ColorView.Color = ToAvaloniaColor(initialValue);
    }

    private void CancelButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close((InspectorColorValue?)null);

    private void SetButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close(ToInspectorColor(ColorView.Color));

    private static InspectorColorValue ToInspectorColor(Color color) =>
        new(
            color.R / (float)byte.MaxValue,
            color.G / (float)byte.MaxValue,
            color.B / (float)byte.MaxValue,
            color.A / (float)byte.MaxValue);

    private static Color ToAvaloniaColor(InspectorColorValue value) =>
        Color.FromArgb(
            ToColorComponent(value.Alpha),
            ToColorComponent(value.Red),
            ToColorComponent(value.Green),
            ToColorComponent(value.Blue));

    private static byte ToColorComponent(float value) =>
        !float.IsFinite(value)
            ? (byte)0
            : (byte)Math.Round(
                Math.Clamp(value, 0f, 1f) * byte.MaxValue,
                MidpointRounding.AwayFromZero);
}
