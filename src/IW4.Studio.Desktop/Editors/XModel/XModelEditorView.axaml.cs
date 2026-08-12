using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XModel;

public sealed partial class XModelEditorView : UserControl
{
    public XModelEditorView() => AvaloniaXamlLoader.Load(this);

    private void FitButton_Click(object? sender, RoutedEventArgs e) =>
        this.FindControl<XModelPreviewControl>("Preview")?.Fit();

    private void RevertButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XModelEditorViewModel)?.RevertDraft();
}
