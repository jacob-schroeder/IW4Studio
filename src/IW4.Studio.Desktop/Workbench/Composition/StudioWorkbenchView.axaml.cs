using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using IW4.Studio.Desktop.Workbench.Docking;

namespace IW4.Studio.Desktop.Workbench.Composition;

public sealed partial class StudioWorkbenchView : UserControl
{
    private const double DragThreshold = 6;

    private DockRailDropCoordinator? _dropCoordinator;
    private DockRailDragPayload? _dragPayload;
    private Point _pressedPosition;
    private Control? _capturedControl;
    private bool _isDraggingTool;
    private string? _suppressClickToolId;
    private WorkbenchEditorTabViewModel? _contextMenuTab;

    public StudioWorkbenchView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _dropCoordinator = DataContext is StudioWorkbenchViewModel viewModel
                ? new DockRailDropCoordinator(viewModel.DockLayout)
                : null;
            CancelToolDrag();
        };
    }

    private void ToolButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string toolId } ||
            DataContext is not StudioWorkbenchViewModel viewModel)
        {
            return;
        }

        if (string.Equals(_suppressClickToolId, toolId, StringComparison.Ordinal))
        {
            _suppressClickToolId = null;
            e.Handled = true;
            return;
        }

        viewModel.ActivateTool(toolId);
    }

    private void ToolDrag_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string toolId } control ||
            DataContext is not StudioWorkbenchViewModel viewModel ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        DockToolState? tool = viewModel.DockLayout.State.FindTool(toolId);
        if (tool is null || !tool.IsImplemented)
            return;

        _dragPayload = new DockRailDragPayload(tool.Id, tool.AllowedRegion);
        _pressedPosition = e.GetPosition(WorkbenchRoot);
        _capturedControl = control;
        e.Pointer.Capture(control);
    }

    private void ToolDrag_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPayload is null ||
            _capturedControl is null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point current = e.GetPosition(WorkbenchRoot);
        if (!_isDraggingTool &&
            Math.Abs(current.X - _pressedPosition.X) < DragThreshold &&
            Math.Abs(current.Y - _pressedPosition.Y) < DragThreshold)
        {
            return;
        }

        _isDraggingTool = true;
        ShowDropCue(_dragPayload.AllowedRegion, isPointerInside: IsInsideAllowedRegion(e));
        e.Handled = true;
    }

    private void ToolDrag_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPayload is null)
            return;

        if (_isDraggingTool)
        {
            if (_dropCoordinator is not null &&
                TryGetDropRegion(e, out DockRegion dropRegion))
            {
                int insertionIndex = CalculateInsertionIndex(dropRegion, e);
                _dropCoordinator.DropTool(
                    _dragPayload,
                    dropRegion,
                    insertionIndex);
            }

            _suppressClickToolId =
                _capturedControl is ToggleButton
                    ? _dragPayload.ToolId
                    : null;
            e.Handled = true;
        }

        e.Pointer.Capture(null);
        CancelToolDrag();
    }

    private void CollapseLeftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StudioWorkbenchViewModel)?.CollapseRegion(DockRegion.Left);

    private void CollapseBottomButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StudioWorkbenchViewModel)?.CollapseRegion(DockRegion.Bottom);

    private void CollapseRightButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as StudioWorkbenchViewModel)?.CollapseRegion(DockRegion.Right);

    private void CloseEditorTabButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                DataContext: WorkbenchEditorTabViewModel tab
            })
        {
            (DataContext as StudioWorkbenchViewModel)?
                .RequestCloseEditorTab(tab);
            e.Handled = true;
        }
    }

    private void EditorTab_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            sender is not Control
            {
                DataContext: WorkbenchEditorTabViewModel tab
            })
        {
            return;
        }

        _contextMenuTab = tab;
        if (DataContext is StudioWorkbenchViewModel viewModel)
            viewModel.SelectedEditorTab = tab;
    }

    private void CloseEditorTabMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (TryGetContextMenuTab(
                sender,
                out WorkbenchEditorTabViewModel? tab) &&
            tab is not null)
        {
            (DataContext as StudioWorkbenchViewModel)?
                .RequestCloseEditorTab(tab);
            e.Handled = true;
        }
    }

    private void CloseOtherEditorTabsMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (TryGetContextMenuTab(
                sender,
                out WorkbenchEditorTabViewModel? tab) &&
            tab is not null)
        {
            (DataContext as StudioWorkbenchViewModel)?
                .RequestCloseOtherEditorTabs(tab);
            e.Handled = true;
        }
    }

    private void CloseAllEditorTabsMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is StudioWorkbenchViewModel viewModel)
        {
            viewModel.RequestCloseAllEditorTabs();
            e.Handled = true;
        }
    }

    private static void CloseEditorTabButton_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            e.Handled = true;
    }

    private bool TryGetContextMenuTab(
        object? sender,
        out WorkbenchEditorTabViewModel? tab)
    {
        tab = (sender as Control)?.Tag as WorkbenchEditorTabViewModel
            ?? _contextMenuTab
            ?? (DataContext as StudioWorkbenchViewModel)?.SelectedEditorTab;
        return tab is not null;
    }

    private void EditorTabStrip_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox ||
            DataContext is not StudioWorkbenchViewModel viewModel)
        {
            return;
        }

        if (listBox.SelectedItem is null &&
            viewModel.SelectedEditorTab is { } selectedTab)
        {
            listBox.SelectedItem = selectedTab;
            return;
        }

        if (listBox.SelectedItem is not null)
        {
            listBox.ScrollIntoView(listBox.SelectedItem);
            if (this.FindControl<ScrollViewer>("CenterEditorScrollViewer") is { } editorScrollViewer)
            {
                editorScrollViewer.Offset = new Vector(
                    editorScrollViewer.Offset.X,
                    0);
            }
        }
    }

    private void CenterEditorScrollViewer_SizeChanged(
        object? sender,
        SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        // Preserve full-height behavior for short documents while allowing
        // taller editor content to establish a scroll extent.
        if (this.FindControl<ContentControl>("CenterScrollableEditorContent") is { } editorContent)
        {
            editorContent.MinHeight = Math.Max(
                0,
                scrollViewer.Bounds.Height - 28);
        }
    }

    private static void EditorTabStrip_PointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        ScrollViewer? scrollViewer = listBox
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (scrollViewer is null)
            return;

        double horizontalRange = Math.Max(
            0,
            scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        if (horizontalRange == 0)
            return;

        double wheelDelta = e.Delta.Y != 0
            ? e.Delta.Y
            : e.Delta.X;
        double nextOffset = Math.Clamp(
            scrollViewer.Offset.X - (wheelDelta * 48),
            0,
            horizontalRange);
        scrollViewer.Offset = new Vector(
            nextOffset,
        scrollViewer.Offset.Y);
        e.Handled = true;
    }

    private void StudioWorkbenchView_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        bool hasCloseModifier =
            e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!hasCloseModifier || e.Key != Key.W)
            return;

        if (DataContext is StudioWorkbenchViewModel
            {
                SelectedEditorTab: { } selectedTab
            } viewModel)
        {
            viewModel.RequestCloseEditorTab(selectedTab);
            e.Handled = true;
        }
    }

    private void LeftSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is StudioWorkbenchViewModel viewModel)
            viewModel.ResizeRegion(DockRegion.Left, LeftPane.Bounds.Width);
    }

    private void BottomSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is StudioWorkbenchViewModel viewModel)
            viewModel.ResizeRegion(DockRegion.Bottom, BottomPane.Bounds.Height);
    }

    private void RightSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is StudioWorkbenchViewModel viewModel)
            viewModel.ResizeRegion(DockRegion.Right, RightPane.Bounds.Width);
    }

    private void ShowDropCue(DockRegion allowedRegion, bool isPointerInside)
    {
        HideDropCues();
        Border cue = allowedRegion switch
        {
            DockRegion.Left => LeftDropCue,
            DockRegion.Bottom => BottomDropCue,
            DockRegion.Right => RightDropCue,
            _ => throw new ArgumentOutOfRangeException(nameof(allowedRegion))
        };
        cue.Opacity = isPointerInside ? 0.92 : 0.34;
    }

    private void HideDropCues()
    {
        LeftDropCue.Opacity = 0;
        BottomDropCue.Opacity = 0;
        RightDropCue.Opacity = 0;
    }

    private bool IsInsideAllowedRegion(PointerEventArgs e) =>
        _dragPayload is not null &&
        IsInsideRegion(_dragPayload.AllowedRegion, e);

    private bool TryGetDropRegion(PointerEventArgs e, out DockRegion region)
    {
        foreach (DockRegion candidate in Enum.GetValues<DockRegion>())
        {
            if (IsInsideRegion(candidate, e))
            {
                region = candidate;
                return true;
            }
        }

        region = default;
        return false;
    }

    private bool IsInsideRegion(DockRegion region, PointerEventArgs e) =>
        region switch
        {
            DockRegion.Left =>
                ContainsPointer(LeftPane, e) ||
                ContainsPointer(LeftTopToolItems, e),
            DockRegion.Bottom =>
                ContainsPointer(BottomPane, e) ||
                ContainsPointer(LeftBottomToolItems, e),
            DockRegion.Right =>
                ContainsPointer(RightPane, e) ||
                ContainsPointer(RightToolItems, e),
            _ => false
        };

    private int CalculateInsertionIndex(
        DockRegion region,
        PointerEventArgs e)
    {
        if (DataContext is not StudioWorkbenchViewModel viewModel)
            return 0;

        ItemsControl rail = region switch
        {
            DockRegion.Left => LeftTopToolItems,
            DockRegion.Bottom => LeftBottomToolItems,
            DockRegion.Right => RightToolItems,
            _ => throw new ArgumentOutOfRangeException(nameof(region))
        };
        int count = viewModel.DockLayout.State.Region(region).Tools.Count;
        if (count == 0)
            return 0;

        double height = Math.Max(rail.Bounds.Height, count * 42d);
        double y = Math.Clamp(e.GetPosition(rail).Y, 0, height);
        return Math.Clamp(
            (int)Math.Round(y / height * count),
            0,
            count);
    }

    private static bool ContainsPointer(
        Control control,
        PointerEventArgs e)
    {
        if (!control.IsVisible)
            return false;

        Point position = e.GetPosition(control);
        return new Rect(control.Bounds.Size).Contains(position);
    }

    private void CancelToolDrag()
    {
        _capturedControl = null;
        _dragPayload = null;
        _isDraggingTool = false;
        HideDropCues();
    }
}
