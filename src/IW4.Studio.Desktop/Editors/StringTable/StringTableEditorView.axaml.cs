using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

    private void ApplyChangesButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StringTableEditorViewModel)?.ApplyChanges();

    private void TableBodyScrollViewer_ScrollChanged(
        object? sender,
        ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer tableBodyScrollViewer ||
            ColumnHeaderEndSpacer is not { } columnHeaderEndSpacer ||
            ColumnHeaderScrollViewer is not { } columnHeaderScrollViewer)
        {
            return;
        }

        double endSpacing = Math.Max(
            0,
            tableBodyScrollViewer.Bounds.Width -
            tableBodyScrollViewer.Viewport.Width);
        if (Math.Abs(columnHeaderEndSpacer.Width - endSpacing) > 0.01)
        {
            columnHeaderEndSpacer.Width = endSpacing;
            Dispatcher.UIThread.Post(
                () => SynchronizeColumnHeaderOffset(
                    tableBodyScrollViewer,
                    columnHeaderScrollViewer),
                DispatcherPriority.Loaded);
        }

        SynchronizeColumnHeaderOffset(
            tableBodyScrollViewer,
            columnHeaderScrollViewer);
    }

    private static void SynchronizeColumnHeaderOffset(
        ScrollViewer tableBodyScrollViewer,
        ScrollViewer columnHeaderScrollViewer)
    {
        Vector bodyOffset = tableBodyScrollViewer.Offset;
        if (columnHeaderScrollViewer.Offset.X == bodyOffset.X)
            return;

        columnHeaderScrollViewer.Offset = new Vector(bodyOffset.X, 0);
    }
}
