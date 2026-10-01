using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Iw4Radiant.Views;

public partial class MistPainterInspector : UserControl
{
    public MistPainterInspector()
    {
        InitializeComponent();
        Spacing.ValueChanged += (_, _) => Changed?.Invoke();
        Radius.ValueChanged += (_, _) => Changed?.Invoke();
        LibraryButton.Click += (_, _) => LibraryRequested?.Invoke();
    }

    internal event Action? Changed;
    internal event Action? LibraryRequested;
    internal bool IsErasing => EraseMode.IsChecked == true;
    internal float BrushSpacing => (float)(Spacing.Value ?? 160);
    internal float EraseRadius => (float)(Radius.Value ?? 80);

    private void Paint_Click(object? sender, RoutedEventArgs e) => SetMode(erase: false);
    private void Erase_Click(object? sender, RoutedEventArgs e) => SetMode(erase: true);

    private void SetMode(bool erase)
    {
        PaintMode.IsChecked = !erase;
        EraseMode.IsChecked = erase;
        Changed?.Invoke();
    }
}
