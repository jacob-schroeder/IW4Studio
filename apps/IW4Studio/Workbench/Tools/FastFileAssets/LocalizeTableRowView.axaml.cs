using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class LocalizeTableRowView : UserControl
{
    public LocalizeTableRowView()
    {
        InitializeComponent();
        InlineValue.AddHandler(KeyDownEvent, InlineValue_KeyDown, RoutingStrategies.Tunnel);
        InlineValue.TextChanged += InlineValue_TextChanged;
        GotFocus += (_, _) => SelectRow();
    }

    private FastFileContentsView? Contents => this.FindAncestorOfType<FastFileContentsView>();

    private void SelectRow()
    {
        if (Contents?.DataContext is FastFileAssetsNavigatorViewModel navigator &&
            this.FindAncestorOfType<ListBoxItem>()?.DataContext is FastFileContentsRow row)
            navigator.SelectedContentsRow = row;
    }

    private void ToggleValue_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalizeTableRowViewModel row ||
            Contents?.DataContext is not FastFileAssetsNavigatorViewModel navigator)
            return;
        SelectRow();
        navigator.LocalizeTable.ToggleExpanded(row);
        if (row.IsExpanded)
            FocusExpanded(row);
        else if (row.ShowInlineInput)
            InlineValue.Focus();
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        SelectRow();
        (DataContext as LocalizeTableRowViewModel)?.Undo();
    }

    private void InlineValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift) ||
            DataContext is not LocalizeTableRowViewModel row || Contents is not { } contents)
            return;

        e.Handled = true;
        if (e.KeyModifiers == KeyModifiers.Shift)
            ExpandFromInline(row);
        else
            contents.FocusNextInlineValue();
    }

    private void InlineValue_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // Multiline paste arrives intact because the compact TextBox accepts returns.
        if (InlineValue.IsFocused && DataContext is LocalizeTableRowViewModel { IsExpanded: false } row &&
            InlineValue.Text is { } text && text.IndexOfAny(['\r', '\n']) >= 0)
        {
            row.ValueInput = text;
            ExpandFromInline(row);
        }
    }

    private void ExpandFromInline(LocalizeTableRowViewModel row)
    {
        if (Contents?.DataContext is not FastFileAssetsNavigatorViewModel navigator)
            return;
        int caret = InlineValue.CaretIndex;
        navigator.LocalizeTable.Expand(row);
        FocusExpanded(row, caret);
    }

    private void FocusExpanded(LocalizeTableRowViewModel row, int? caret = null) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, row) || !row.IsExpanded)
                return;
            FullValue.Focus();
            if (caret is { } offset)
                FullValue.CaretIndex = Math.Min(offset, FullValue.Text?.Length ?? 0);
        }, DispatcherPriority.Loaded);

    internal void FocusValue()
    {
        if (DataContext is not LocalizeTableRowViewModel { IsContentAvailable: true } row)
            return;
        if (row.ShowInlineInput)
            InlineValue.Focus();
        else if (Contents?.DataContext is FastFileAssetsNavigatorViewModel navigator)
        {
            navigator.LocalizeTable.Expand(row);
            FocusExpanded(row);
        }
    }
}
