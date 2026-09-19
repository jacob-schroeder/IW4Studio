using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XAnim;

public sealed partial class XAnimEditorView : UserControl
{
    public XAnimEditorView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        (DataContext as XAnimPreviewViewModel)?.PausePlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XAnimPreviewViewModel)?.TogglePlayback();

    private void RestartButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XAnimPreviewViewModel)?.RestartPlayback();
}
