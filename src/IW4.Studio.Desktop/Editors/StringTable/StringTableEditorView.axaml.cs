using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.StringTable;

public sealed partial class StringTableEditorView : UserControl
{
    public StringTableEditorView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StringTableEditorViewModel)?.RevertDraft();
}
