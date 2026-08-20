using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.StructuredData;

public sealed partial class StructuredDataDefEditorView : UserControl
{
    public StructuredDataDefEditorView() => AvaloniaXamlLoader.Load(this);

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StructuredDataDefEditorViewModel)?.RevertDraft();

    private void ApplyDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StructuredDataDefEditorViewModel)?.ApplyDraft();
}
