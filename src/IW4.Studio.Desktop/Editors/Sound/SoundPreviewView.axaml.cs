using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Sound;

public sealed partial class SoundPreviewView : UserControl
{
    public SoundPreviewView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundPreviewViewModel)?.PausePlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private void PlayPauseButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as SoundPreviewViewModel)?.TogglePlayback();
}
