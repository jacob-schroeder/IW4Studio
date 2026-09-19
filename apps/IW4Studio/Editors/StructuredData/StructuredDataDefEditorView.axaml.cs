using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.StructuredData;

public sealed partial class StructuredDataDefEditorView : UserControl
{
    private const double CompactBodyWidth = 560;
    private const double CompactHeaderWidth = 460;
    private bool _usesCompactBodyLayout;
    private bool _usesCompactHeaderLayout;

    public StructuredDataDefEditorView() => AvaloniaXamlLoader.Load(this);

    private void Editor_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateHeaderLayout(e.NewSize.Width < CompactHeaderWidth);
        UpdateBodyLayout(e.NewSize.Width < CompactBodyWidth);
    }

    private void UpdateHeaderLayout(bool useCompactLayout)
    {
        if (_usesCompactHeaderLayout == useCompactLayout)
            return;

        _usesCompactHeaderLayout = useCompactLayout;
        EditorHeaderGrid.ColumnDefinitions = new ColumnDefinitions(
            useCompactLayout ? "*" : "*,Auto");
        EditorHeaderGrid.RowDefinitions = new RowDefinitions(
            useCompactLayout ? "Auto,Auto" : "Auto");
        Grid.SetColumn(EditorHeaderActions, useCompactLayout ? 0 : 1);
        Grid.SetRow(EditorHeaderActions, useCompactLayout ? 1 : 0);
        EditorHeaderActions.Margin = useCompactLayout
            ? new Thickness(0, 14, 0, 0)
            : default;
    }

    private void UpdateBodyLayout(bool useCompactLayout)
    {
        if (_usesCompactBodyLayout == useCompactLayout)
            return;

        _usesCompactBodyLayout = useCompactLayout;
        EditorBodyGrid.ColumnDefinitions = new ColumnDefinitions(
            useCompactLayout ? "*" : "1.6*,1,5*");
        EditorBodyGrid.RowDefinitions = new RowDefinitions(
            useCompactLayout ? "150,1,*" : "*");

        Grid.SetColumn(BodyDivider, useCompactLayout ? 0 : 1);
        Grid.SetRow(BodyDivider, useCompactLayout ? 1 : 0);
        Grid.SetColumn(DetailPane, useCompactLayout ? 0 : 2);
        Grid.SetRow(DetailPane, useCompactLayout ? 2 : 0);
    }

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StructuredDataDefEditorViewModel)?.RevertDraft();

    private void ApplyDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StructuredDataDefEditorViewModel)?.ApplyDraft();
}
