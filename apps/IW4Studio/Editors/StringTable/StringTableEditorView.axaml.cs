using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IW4.Formats.SourceFormat.StringTable;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.StringTable;

public sealed partial class StringTableEditorView : UserControl
{
    public StringTableEditorView()
    {
        InitializeComponent();
    }

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StringTableEditorViewModel)?.RevertDraft();

    private void ApplyChangesButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StringTableEditorViewModel)?.ApplyChanges();

    private async void ImportCsvButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StringTableEditorViewModel { IsEditable: true } viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import CSV into string table",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("CSV files") { Patterns = ["*.csv"] },
                        FilePickerFileTypes.All
                    ]
                });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null)
                return;

            string assetName = viewModel.OriginalName;
            await using Stream stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var table = await Task.Run(() =>
                new StringTableExchange().ReadCsv(reader, assetName));
            if (DataContext != viewModel)
                return;

            viewModel.ImportCsv(table, file.Name);
            FocusCell(0, 0);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            OverflowException)
        {
            viewModel.ReportImportFailure($"Could not import CSV: {exception.Message}");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void AddRowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StringTableEditorViewModel viewModel)
            InsertRow(viewModel.RowCount);
    }

    private void AddColumnButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StringTableEditorViewModel viewModel)
            return;

        int column = viewModel.ColumnCount;
        viewModel.AddColumn();
        if (viewModel.ColumnCount > column)
            FocusCell(0, column);
    }

    private void InsertRowAboveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StringTableRowEditorViewModel row })
            InsertRow(row.Row);
    }

    private void InsertRowBelowMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StringTableRowEditorViewModel row })
            InsertRow(row.Row + 1);
    }

    private void InsertRow(int row)
    {
        if (DataContext is not StringTableEditorViewModel viewModel)
            return;

        int previousRowCount = viewModel.RowCount;
        viewModel.InsertRow(row);
        if (viewModel.RowCount > previousRowCount)
            FocusCell(row, 0, scrollToEnd: row == previousRowCount);
    }

    private void FocusCell(int row, int column, bool scrollToEnd = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (scrollToEnd)
                TableBodyScrollViewer.ScrollToEnd();
            else if (row == 0)
                TableBodyScrollViewer.Offset = new Vector(TableBodyScrollViewer.Offset.X, 0);

            Dispatcher.UIThread.Post(() =>
            {
                TextBox? input = this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(control =>
                        control.DataContext is StringTableCellEditorViewModel cell &&
                        cell.Row == row && cell.Column == column);
                input?.BringIntoView();
                input?.Focus();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

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
