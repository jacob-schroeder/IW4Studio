using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Localize;

public sealed partial class LocalizeEditorView : UserControl
{
    public LocalizeEditorView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as LocalizeEditorViewModel)?.RevertDraft();

    private void ApplyChangesButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as LocalizeEditorViewModel)?.ApplyChanges();
}
