using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Font;

public sealed partial class FontViewerView : UserControl
{
    public FontViewerView() => AvaloniaXamlLoader.Load(this);

    private void ResetPreviewButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as FontViewerViewModel)?.ResetPreviewText();

    private void PreviewControl_MaterialResolutionCompleted(
        object? sender,
        MenuPreviewMaterialResolutionCompletedEventArgs e) =>
        (DataContext as FontViewerViewModel)?
            .ReportMaterialPreviewStatus(e.Status);

    private void PreviewControl_TextResolutionCompleted(
        object? sender,
        MenuPreviewTextResolutionCompletedEventArgs e) =>
        (DataContext as FontViewerViewModel)?
            .ReportTextPreviewStatus(e.Status);
}
