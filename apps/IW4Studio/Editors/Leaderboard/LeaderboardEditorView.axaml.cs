using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using IW4.Game.Assets.Leaderboard;
using IW4.Studio.Desktop.ViewModels;
using Material.Icons;
using Material.Icons.Avalonia;

namespace IW4.Studio.Desktop.Editors.Leaderboard;

public sealed partial class LeaderboardEditorView : UserControl
{
    private const double PlayerWidth = 150;
    private const double ColumnWidth = 140;
    private const double CompactWidth = 700;
    private LeaderboardEditorViewModel _editor =>
        DataContext as LeaderboardEditorViewModel ?? throw new InvalidOperationException("A leaderboard editor is required.");
    private readonly List<(int Index, Border Header)> _headers = [];
    private readonly Dictionary<int, List<Border>> _cells = [];
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _refreshing;
    private bool _compact;
    private IPointer? _dragPointer;
    private Border? _dragHeader;
    private int _dragSource = -1;
    private Point _pressPoint;
    private Point _pointerPoint;
    private bool _moving;
    private int _dropNeighbor = -1;
    private bool _dropAfter;

    public LeaderboardEditorView()
    {
        InitializeComponent();
        IsEnabled = false;
        _scrollTimer.Tick += (_, _) => AutoScroll();

        WireText(TitleInput, () => _editor.SelectedColumn?.Name ?? string.Empty,
            text => _editor.SetTitle(text));
        WireNumber(PrecisionInput, () => _editor.SelectedColumn?.Precision ?? 0,
            value => _editor.SetPrecision(value));
        WireNumber(BoardIdInput, () => _editor.BoardId, value => _editor.SetBoardId(value));
        WireNumber(ColumnIdInput, () => _editor.SelectedColumn?.Id ?? 0,
            value => _editor.SetColumnId(value));
        WireNumber(PropertyIdInput, () => _editor.SelectedColumn?.PropertyId ?? 0,
            value => _editor.SetPropertyId(value));

        SourcePicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && SourcePicker.SelectedItem is LeaderboardStatChoice choice)
                _editor.SetSourceStat(choice.Key);
        };
        TypePicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && TypePicker.SelectedItem is Choice<LbColType> choice)
                _editor.SetType(choice.Value);
        };
        AggregationPicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && AggregationPicker.SelectedItem is Choice<LbAggType> choice)
                _editor.SetAggregation(choice.Value);
        };
        XpRolePicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && XpRolePicker.SelectedItem is LeaderboardRoleChoice choice)
                _editor.SetXpColumnId(choice.Id);
        };
        PrestigeRolePicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && PrestigeRolePicker.SelectedItem is LeaderboardRoleChoice choice)
                _editor.SetPrestigeColumnId(choice.Id);
        };
        VisibleCheck.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && VisibleCheck.IsChecked is bool visible)
                _editor.SetVisible(visible);
        };

    }

    internal LeaderboardEditorView(LeaderboardEditorViewModel editor) : this()
    {
        DataContext = editor;
        editor.Changed += Refresh;
        IsEnabled = true;
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LeaderboardEditorViewModel editor)
            editor.RefreshWorkspace();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndDrag();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (_dragPointer is not null)
            EndDrag();
        _refreshing = true;
        try
        {
            HeadingText.Text = _editor.Heading;
            StatusText.Text = _editor.Status;
            UndoButton.IsEnabled = _editor.CanUndo;
            ApplyButton.IsEnabled = _editor.CanApply;
            AddButton.IsEnabled = _editor.IsEditable;

            RenderPreview();
            RenderHidden();
            RenderInspector();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderPreview()
    {
        double offset = PreviewScrollViewer.Offset.X;
        PreviewHeaderPanel.Children.Clear();
        PreviewRowsPanel.Children.Clear();
        _headers.Clear();
        _cells.Clear();

        if (_editor.ShowPlayerLabels)
            PreviewHeaderPanel.Children.Add(HeaderLabel("Player", PlayerWidth));

        for (int index = 0; index < _editor.Columns.Count; index++)
        {
            LbColumnDef column = _editor.Columns[index];
            if (column.Hidden)
                continue;

            int capturedIndex = index;
            var header = new Border
            {
                Width = ColumnWidth,
                MinHeight = 48,
                Focusable = true,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            header.Classes.Add("lbHeader");
            header.Classes.Set("selected", index == _editor.SelectedIndex);
            header.SetValue(AutomationProperties.NameProperty,
                $"Move {ColumnTitle(column)}; select or drag to reorder. Alt + Left or Right moves by one column.");
            var grip = new MaterialIcon { Kind = MaterialIconKind.DragVertical, Width = 17, Height = 17 };
            grip.Classes.Add("lbGrip");
            header.Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(9, 5),
                Spacing = 7,
                Children =
                {
                    grip,
                    new TextBlock
                    {
                        Text = ColumnTitle(column),
                        FontWeight = FontWeight.Medium,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = ColumnWidth - 39
                    }
                }
            };
            header.PointerPressed += (_, e) => HeaderPressed(header, capturedIndex, e);
            header.PointerMoved += (_, e) => HeaderMoved(e);
            header.PointerReleased += (_, e) => HeaderReleased(e);
            header.PointerCaptureLost += (_, _) => EndDrag();
            header.KeyDown += (_, e) => HeaderKeyDown(capturedIndex, e);
            PreviewHeaderPanel.Children.Add(header);
            _headers.Add((index, header));
        }

        for (int player = 0; player < LeaderboardEditorViewModel.Players.Count; player++)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            if (_editor.ShowPlayerLabels)
            {
                Border playerCell = ValueCell($"{player + 1}. {LeaderboardEditorViewModel.Players[player]}", PlayerWidth, true);
                playerCell.Classes.Add("lbCell");
                row.Children.Add(playerCell);
            }
            foreach ((int index, _) in _headers)
            {
                Border cell = ValueCell(_editor.SampleValue(index, player), ColumnWidth, false);
                cell.Classes.Add("lbCell");
                if (!_cells.TryGetValue(index, out List<Border>? cells))
                    _cells[index] = cells = [];
                cells.Add(cell);
                row.Children.Add(cell);
            }
            PreviewRowsPanel.Children.Add(row);
        }

        Dispatcher.UIThread.Post(() =>
            PreviewScrollViewer.Offset = new Vector(offset, PreviewScrollViewer.Offset.Y),
            DispatcherPriority.Loaded);
    }

    private void RenderHidden()
    {
        HiddenColumnsPanel.Children.Clear();
        bool any = false;
        for (int index = 0; index < _editor.Columns.Count; index++)
        {
            LbColumnDef column = _editor.Columns[index];
            if (!column.Hidden)
                continue;
            any = true;
            int capturedIndex = index;
            var button = new Button
            {
                Content = $"Restore {ColumnTitle(column)}",
                MinHeight = 30,
                Padding = new Thickness(9, 4),
                Margin = new Thickness(0, 0, 6, 4),
                IsEnabled = _editor.IsEditable
            };
            button.Click += (_, _) =>
            {
                _editor.Select(capturedIndex);
                _editor.SetVisible(true);
            };
            HiddenColumnsPanel.Children.Add(button);
        }
        if (!any)
        {
            var none = new TextBlock { Text = "None", FontSize = 11 };
            none.Classes.Add("lbMuted");
            HiddenColumnsPanel.Children.Add(none);
        }
    }

    private void RenderInspector()
    {
        LbColumnDef? column = _editor.SelectedColumn;
        bool selected = column is not null;
        bool editable = selected && _editor.IsEditable;
        SelectedTitle.Text = selected ? ColumnTitle(column!) : "No column selected";
        TitleInput.Text = column?.Name ?? string.Empty;
        TitleInput.IsEnabled = editable;

        SourcePicker.ItemsSource = _editor.SourceChoices;
        SourcePicker.SelectedItem = _editor.SourceChoices.FirstOrDefault(choice =>
            string.Equals(choice.Key ?? string.Empty, column?.StatName ?? string.Empty, StringComparison.Ordinal));
        SourcePicker.IsEnabled = editable;
        SourceKey.Text = string.IsNullOrEmpty(column?.StatName) ? string.Empty : $"Raw key: {column.StatName}";

        TypePicker.ItemsSource = TypeChoices(column?.Type);
        TypePicker.SelectedItem = ((IEnumerable<Choice<LbColType>>)TypePicker.ItemsSource!)
            .FirstOrDefault(choice => choice.Value == column?.Type);
        TypePicker.IsEnabled = editable;
        AggregationPicker.ItemsSource = AggregationChoices(column?.Aggregation);
        AggregationPicker.SelectedItem = ((IEnumerable<Choice<LbAggType>>)AggregationPicker.ItemsSource!)
            .FirstOrDefault(choice => choice.Value == column?.Aggregation);
        AggregationPicker.IsEnabled = editable;
        SampleHistoryText.Text = selected ? _editor.SampleHistory : "—";
        SampleResultText.Text = selected
            ? $"{column!.Aggregation} across these results · {_editor.SampleResult}"
            : "Select a column to see sample results.";
        PrecisionPanel.IsVisible = column?.Type is LbColType.Number or LbColType.Time or LbColType.Percent;
        PrecisionInput.Text = column?.Precision.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        PrecisionInput.IsEnabled = editable;

        LeftButton.IsEnabled = editable && NextVisibleIndex(_editor.SelectedIndex, -1) >= 0;
        RightButton.IsEnabled = editable && NextVisibleIndex(_editor.SelectedIndex, 1) >= 0;
        VisibleCheck.IsChecked = column is not null && !column.Hidden;
        VisibleCheck.IsEnabled = editable;

        BoardIdInput.Text = _editor.BoardId.ToString(CultureInfo.CurrentCulture);
        BoardIdInput.IsEnabled = _editor.IsEditable;
        ColumnIdInput.Text = column?.Id.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        ColumnIdInput.IsEnabled = editable;
        PropertyIdInput.Text = column?.PropertyId.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        PropertyIdInput.IsEnabled = editable;
        XpRolePicker.ItemsSource = _editor.RoleChoices;
        XpRolePicker.SelectedItem = _editor.RoleChoices.FirstOrDefault(choice => choice.Id == _editor.XpColumnId);
        XpRolePicker.IsEnabled = _editor.IsEditable;
        PrestigeRolePicker.ItemsSource = _editor.RoleChoices;
        PrestigeRolePicker.SelectedItem = _editor.RoleChoices.FirstOrDefault(choice => choice.Id == _editor.PrestigeColumnId);
        PrestigeRolePicker.IsEnabled = _editor.IsEditable;
    }

    private static Border HeaderLabel(string text, double width)
    {
        var header = new Border
        {
            Width = width,
            MinHeight = 48,
            Padding = new Thickness(13, 12),
            Child = new TextBlock { Text = text, FontWeight = FontWeight.Medium }
        };
        header.Classes.Add("lbHeader");
        return header;
    }

    private static Border ValueCell(string text, double width, bool player) => new()
    {
        Width = width,
        MinHeight = 45,
        Padding = new Thickness(player ? 13 : 8, 11),
        BorderThickness = new Thickness(0, 0, 1, 1),
        Child = new TextBlock
        {
            Text = text,
            TextAlignment = player ? TextAlignment.Left : TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12
        }
    };

    private void HeaderPressed(Border header, int index, PointerPressedEventArgs e)
    {
        if (_dragPointer is not null || e.GetCurrentPoint(header).Properties.IsRightButtonPressed)
            return;
        header.Focus();
        if (!_editor.IsEditable)
        {
            _editor.Select(index);
            e.Handled = true;
            return;
        }
        _dragPointer = e.Pointer;
        _dragHeader = header;
        _dragSource = index;
        _pressPoint = e.GetPosition(PreviewArea);
        _pointerPoint = _pressPoint;
        _moving = false;
        _dropNeighbor = -1;
        e.Pointer.Capture(header);
        e.Handled = true;
    }

    private void HeaderMoved(PointerEventArgs e)
    {
        if (e.Pointer != _dragPointer)
            return;
        _pointerPoint = e.GetPosition(PreviewArea);
        if (!_moving)
        {
            if (Math.Sqrt(Math.Pow(_pointerPoint.X - _pressPoint.X, 2) +
                Math.Pow(_pointerPoint.Y - _pressPoint.Y, 2)) < 5)
                return;
            _moving = true;
            if (_dragHeader is not null)
                _dragHeader.Classes.Set("dragSource", true);
            DragChipText.Text = ColumnTitle(_editor.Columns[_dragSource]);
            DragChip.IsVisible = true;
            _scrollTimer.Start();
        }
        UpdateDragTarget();
        e.Handled = true;
    }

    private void HeaderReleased(PointerReleasedEventArgs e)
    {
        if (e.Pointer != _dragPointer)
            return;
        _pointerPoint = e.GetPosition(PreviewArea);
        if (_moving)
            UpdateDragTarget();
        bool moving = _moving;
        int source = _dragSource;
        int neighbor = _dropNeighbor;
        bool after = _dropAfter;
        EndDrag();
        if (moving && neighbor >= 0)
        {
            int finalIndex = source < neighbor
                ? neighbor - (after ? 0 : 1)
                : neighbor + (after ? 1 : 0);
            _editor.MoveColumn(source, finalIndex);
            FocusHeader(finalIndex);
        }
        else if (!moving)
        {
            _editor.Select(source);
            FocusHeader(source);
        }
        e.Handled = true;
    }

    private void EndDrag()
    {
        _scrollTimer.Stop();
        IPointer? pointer = _dragPointer;
        _dragPointer = null;
        pointer?.Capture(null);
        _dragHeader?.Classes.Set("dragSource", false);
        _dragHeader = null;
        _dragSource = -1;
        _moving = false;
        _dropNeighbor = -1;
        DragChip.IsVisible = false;
        foreach ((int index, Border header) in _headers)
        {
            header.Classes.Set("dropBefore", false);
            header.Classes.Set("dropAfter", false);
            SetCellDrop(index, false, false);
        }
    }

    private void UpdateDragTarget()
    {
        foreach ((int index, Border header) in _headers)
        {
            header.Classes.Set("dropBefore", false);
            header.Classes.Set("dropAfter", false);
            SetCellDrop(index, false, false);
        }
        _dropNeighbor = -1;
        if (!_moving || _headers.Count < 2)
            return;

        Canvas.SetLeft(DragChip, Math.Clamp(_pointerPoint.X - 34, 0, Math.Max(0, PreviewArea.Bounds.Width - DragChip.Bounds.Width)));
        Canvas.SetTop(DragChip, Math.Max(0, _pointerPoint.Y - 38));

        Point? viewport = PreviewScrollViewer.TranslatePoint(new Point(0, 0), PreviewArea);
        if (viewport is null || _pointerPoint.X < viewport.Value.X - 12 ||
            _pointerPoint.X > viewport.Value.X + PreviewScrollViewer.Bounds.Width + 12 ||
            _pointerPoint.Y < viewport.Value.Y - 16 ||
            _pointerPoint.Y > viewport.Value.Y + PreviewScrollViewer.Bounds.Height + 10)
            return;

        foreach ((int index, Border header) in _headers)
        {
            if (index == _dragSource)
                continue;
            Point? origin = header.TranslatePoint(new Point(0, 0), PreviewArea);
            if (origin is null)
                continue;
            if (_pointerPoint.X < origin.Value.X + header.Bounds.Width / 2)
            {
                _dropNeighbor = index;
                _dropAfter = false;
                header.Classes.Set("dropBefore", true);
                SetCellDrop(index, true, false);
                return;
            }
        }
        (int lastIndex, Border lastHeader) = _headers.Last(pair => pair.Index != _dragSource);
        _dropNeighbor = lastIndex;
        _dropAfter = true;
        lastHeader.Classes.Set("dropAfter", true);
        SetCellDrop(lastIndex, false, true);
    }

    private void SetCellDrop(int index, bool before, bool after)
    {
        if (!_cells.TryGetValue(index, out List<Border>? cells))
            return;
        foreach (Border cell in cells)
        {
            cell.Classes.Set("dropBefore", before);
            cell.Classes.Set("dropAfter", after);
        }
    }

    private void AutoScroll()
    {
        if (!_moving)
            return;
        Point? viewport = PreviewScrollViewer.TranslatePoint(new Point(0, 0), PreviewArea);
        if (viewport is null)
            return;
        double left = viewport.Value.X;
        double right = left + PreviewScrollViewer.Bounds.Width;
        double delta = _pointerPoint.X < left + 32 ? -8 : _pointerPoint.X > right - 32 ? 8 : 0;
        if (delta == 0)
            return;
        PreviewScrollViewer.Offset = new Vector(
            Math.Max(0, PreviewScrollViewer.Offset.X + delta), PreviewScrollViewer.Offset.Y);
        UpdateDragTarget();
    }

    private void HeaderKeyDown(int index, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right)
        {
            int target = NextVisibleIndex(index, e.Key == Key.Left ? -1 : 1);
            if (_editor.IsEditable && target >= 0)
            {
                _editor.MoveColumn(index, target);
                FocusHeader(target);
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space)
        {
            _editor.Select(index);
            FocusHeader(index);
            e.Handled = true;
        }
    }

    private void FocusHeader(int index)
    {
        Dispatcher.UIThread.Post(() => _headers.FirstOrDefault(pair => pair.Index == index).Header?.Focus(),
            DispatcherPriority.Loaded);
    }

    private int NextVisibleIndex(int source, int delta)
    {
        for (int index = source + delta; index >= 0 && index < _editor.Columns.Count; index += delta)
            if (!_editor.Columns[index].Hidden)
                return index;
        return -1;
    }

    private void WireText(TextBox input, Func<string> current, Action<string> update)
    {
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                input.Text = current();
                PreviewPane.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Commit();
                PreviewPane.Focus();
                e.Handled = true;
            }
        };
        void Commit()
        {
            if (_refreshing || !_editor.IsEditable || _editor.SelectedColumn is null || input.Text == current())
                return;
            update(input.Text ?? string.Empty);
        }
    }

    private void WireNumber(TextBox input, Func<int> current, Action<int> update)
    {
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                input.Text = current().ToString(CultureInfo.CurrentCulture);
                PreviewPane.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Commit();
                PreviewPane.Focus();
                e.Handled = true;
            }
        };
        void Commit()
        {
            if (_refreshing || !_editor.IsEditable || !input.IsEnabled)
                return;
            if (!int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value))
            {
                input.Text = current().ToString(CultureInfo.CurrentCulture);
                return;
            }
            if (value != current())
                update(value);
        }
    }

    private void Editor_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < CompactWidth;
        if (compact == _compact)
            return;
        _compact = compact;
        BodyGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,1,300");
        BodyGrid.RowDefinitions = new RowDefinitions(compact ? "310,1,*" : "*");
        Grid.SetColumn(BodyDivider, compact ? 0 : 1);
        Grid.SetRow(BodyDivider, compact ? 1 : 0);
        Grid.SetColumn(InspectorPane, compact ? 0 : 2);
        Grid.SetRow(InspectorPane, compact ? 2 : 0);
        if (compact)
        {
            PreviewPane.MinHeight = 310;
            BodyGrid.MinHeight = 640;
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("*");
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(HeaderActions, 0);
            Grid.SetRow(HeaderActions, 1);
            HeaderActions.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            PreviewPane.MinHeight = 0;
            BodyGrid.MinHeight = 0;
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(HeaderActions, 1);
            Grid.SetRow(HeaderActions, 0);
            HeaderActions.Margin = default;
        }
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => _editor.Undo();
    private void Apply_Click(object? sender, RoutedEventArgs e) => _editor.Apply();
    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        _editor.AddColumn();
        TitleInput.Focus();
        TitleInput.SelectAll();
    }
    private void MoveLeft_Click(object? sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveRight_Click(object? sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        int index = _editor.SelectedIndex;
        int target = index >= 0 ? NextVisibleIndex(index, delta) : -1;
        if (target >= 0)
        {
            _editor.MoveColumn(index, target);
            FocusHeader(target);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        if (e.Key == Key.Escape && _dragPointer is not null)
        {
            EndDrag();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is not TextBox)
        {
            _editor.Undo();
            e.Handled = true;
        }
    }

    private static string ColumnTitle(LbColumnDef column) =>
        string.IsNullOrWhiteSpace(column.Name) ? "Untitled" : column.Name;

    private static IReadOnlyList<Choice<LbColType>> TypeChoices(LbColType? current)
    {
        var choices = new List<Choice<LbColType>>
        {
            new(LbColType.Number, "Number"), new(LbColType.Time, "Time"),
            new(LbColType.LevelXp, "Level XP"), new(LbColType.Prestige, "Prestige"),
            new(LbColType.BigNumber, "Big number"), new(LbColType.Percent, "Percent")
        };
        if (current is { } value && choices.All(choice => choice.Value != value))
            choices.Add(new Choice<LbColType>(value, $"Current asset · {(int)value}"));
        return choices;
    }

    private static IReadOnlyList<Choice<LbAggType>> AggregationChoices(LbAggType? current)
    {
        var choices = new List<Choice<LbAggType>>
        {
            new(LbAggType.Min, "Min"), new(LbAggType.Max, "Max"),
            new(LbAggType.Sum, "Sum"), new(LbAggType.Last, "Last")
        };
        if (current is { } value && choices.All(choice => choice.Value != value))
            choices.Add(new Choice<LbAggType>(value, $"Current asset · {(int)value}"));
        return choices;
    }

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
