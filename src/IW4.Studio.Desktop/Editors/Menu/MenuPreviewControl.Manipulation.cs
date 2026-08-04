using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Preview;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private const double MoveActivationDistance = 3;
    private const double ResizeHandleSize = 8;
    private const double ResizeHandleHitSize = 12;
    private const double MinimumVisualSpan = 1;

    private static readonly IBrush ResizeHandleFill =
        new SolidColorBrush(Color.FromRgb(241, 247, 252));
    private static readonly Pen ResizeHandleBorder =
        new(new SolidColorBrush(Color.FromRgb(74, 184, 255)), 1.5);
    private static readonly Cursor MoveCursor =
        new(StandardCursorType.SizeAll);
    private static readonly Cursor HorizontalResizeCursor =
        new(StandardCursorType.SizeWestEast);
    private static readonly Cursor VerticalResizeCursor =
        new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestResizeCursor =
        new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastResizeCursor =
        new(StandardCursorType.TopRightCorner);
    private static readonly Cursor SouthEastResizeCursor =
        new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor SouthWestResizeCursor =
        new(StandardCursorType.BottomLeftCorner);

    private GeometryManipulation? _geometryManipulation;
    private IPointer? _geometryPointer;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_geometryManipulation is not null ||
            Scene is not { } scene)
        {
            return;
        }

        PointerPointProperties properties =
            e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed &&
            !properties.IsRightButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        if (!properties.IsLeftButtonPressed)
            return;

        PreviewTransform transform = CreateTransform(scene.Settings);
        Point localPosition = e.GetPosition(this);
        if (!StageBounds(scene.Settings, transform).Contains(localPosition))
            return;

        if (IsDirectManipulationEnabled &&
            TryGetSelectedRegion(scene, out MenuPreviewHitRegion selectedRegion) &&
            HitTestResizeHandle(
                transform.Map(selectedRegion.Bounds),
                localPosition) is { } resizeOperation)
        {
            BeginGeometryManipulation(
                e.Pointer,
                selectedRegion,
                resizeOperation,
                localPosition,
                transform);
            e.Handled = true;
            return;
        }

        if (HitTestNode(scene, transform, localPosition) is not { } nodeId)
        {
            return;
        }

        NodeSelected?.Invoke(
            this,
            new MenuPreviewNodeSelectedEventArgs(nodeId));

        // Selection is synchronous. Re-read both properties so the view model
        // remains the authority for which node kinds are directly editable.
        if (IsDirectManipulationEnabled &&
            SelectedNodeId == nodeId &&
            Scene is { } selectedScene &&
            TryGetSelectedRegion(
                selectedScene,
                out MenuPreviewHitRegion region))
        {
            BeginGeometryManipulation(
                e.Pointer,
                region,
                GeometryOperation.Move,
                localPosition,
                transform);
        }

        e.Handled = true;
    }

    internal MenuNodeId? HitTestNode(Point localPosition)
    {
        if (Scene is not { } scene)
            return null;

        PreviewTransform transform = CreateTransform(scene.Settings);
        return HitTestNode(scene, transform, localPosition);
    }

    private static MenuNodeId? HitTestNode(
        MenuPreviewScene scene,
        PreviewTransform transform,
        Point localPosition)
    {
        if (!StageBounds(scene.Settings, transform).Contains(localPosition) ||
            !transform.TryUnmap(localPosition, out Point virtualPosition))
        {
            return null;
        }

        return scene.HitTest(
            (float)virtualPosition.X,
            (float)virtualPosition.Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_geometryManipulation is not { } manipulation ||
            !ReferenceEquals(e.Pointer, _geometryPointer))
        {
            UpdateGeometryCursor(e.GetPosition(this));
            return;
        }

        Point localPosition = e.GetPosition(this);
        UpdateGeometryCandidate(manipulation, localPosition);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left ||
            _geometryManipulation is not { } manipulation ||
            !ReferenceEquals(e.Pointer, _geometryPointer))
        {
            return;
        }

        UpdateGeometryCandidate(manipulation, e.GetPosition(this));
        CompleteGeometryManipulation(e.Pointer);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || _geometryManipulation is null)
            return;

        CancelGeometryManipulation();
        e.Handled = true;
    }

    private void UpdateGeometryCandidate(
        GeometryManipulation manipulation,
        Point localPosition)
    {
        if (!manipulation.IsActivated)
        {
            Vector localDelta = localPosition - manipulation.PressLocal;
            if (localDelta.SquaredLength <
                MoveActivationDistance * MoveActivationDistance)
            {
                return;
            }
            manipulation.IsActivated = true;
        }

        if (!manipulation.Transform.TryUnmap(
                localPosition,
                out Point virtualPosition))
        {
            return;
        }

        MenuPreviewRect candidate = Resize(
            manipulation.OriginalBounds,
            manipulation.Operation,
            virtualPosition.X - manipulation.PressVirtual.X,
            virtualPosition.Y - manipulation.PressVirtual.Y);
        if (candidate != manipulation.CandidateBounds)
        {
            manipulation.CandidateBounds = candidate;
            InvalidateVisual();
        }
    }

    private void BeginGeometryManipulation(
        IPointer pointer,
        MenuPreviewHitRegion region,
        GeometryOperation operation,
        Point localPosition,
        PreviewTransform transform)
    {
        if (!transform.TryUnmap(localPosition, out Point virtualPosition) ||
            !IsFinite(region.Bounds))
        {
            return;
        }

        _geometryManipulation = new GeometryManipulation(
            region.NodeId,
            region.Placement,
            operation,
            localPosition,
            virtualPosition,
            transform);
        _geometryPointer = pointer;
        Cursor = CursorFor(operation);
        pointer.Capture(this);
        InvalidateVisual();
    }

    private void CompleteGeometryManipulation(IPointer pointer)
    {
        GeometryManipulation manipulation = _geometryManipulation!;
        _geometryManipulation = null;
        _geometryPointer = null;
        Cursor = null;
        pointer.Capture(null);
        InvalidateVisual();

        if (!manipulation.IsActivated ||
            manipulation.CandidateBounds == manipulation.OriginalBounds)
        {
            return;
        }

        GeometryCommitted?.Invoke(
            this,
            new MenuPreviewGeometryCommittedEventArgs(
                manipulation.NodeId,
                manipulation.OriginalBounds,
                manipulation.CandidateBounds));
    }

    private void CancelGeometryManipulation()
    {
        IPointer? pointer = _geometryPointer;
        bool hadManipulation = _geometryManipulation is not null;
        _geometryManipulation = null;
        _geometryPointer = null;
        Cursor = null;
        pointer?.Capture(null);
        if (hadManipulation)
            InvalidateVisual();
    }

    private void MenuPreviewControl_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        if (_geometryManipulation is null)
            return;

        _geometryManipulation = null;
        _geometryPointer = null;
        Cursor = null;
        InvalidateVisual();
    }

    private void UpdateGeometryCursor(Point localPosition)
    {
        if (!IsDirectManipulationEnabled ||
            Scene is not { } scene ||
            !TryGetSelectedRegion(scene, out MenuPreviewHitRegion region))
        {
            Cursor = null;
            return;
        }

        PreviewTransform transform = CreateTransform(scene.Settings);
        if (!StageBounds(scene.Settings, transform).Contains(localPosition))
        {
            Cursor = null;
            return;
        }

        Rect bounds = transform.Map(region.Bounds);
        GeometryOperation? operation = HitTestResizeHandle(
            bounds,
            localPosition);
        if (operation is null && bounds.Contains(localPosition))
            operation = GeometryOperation.Move;
        Cursor = operation is { } value ? CursorFor(value) : null;
    }

    private bool TryGetSelectedRegion(
        MenuPreviewScene scene,
        out MenuPreviewHitRegion region)
    {
        region = (SelectedNodeId is { } selected
            ? scene.HitRegions
                .Where(value => value.NodeId == selected)
                .OrderByDescending(value => value.ZIndex)
                .FirstOrDefault()
            : null)!;
        return region is not null;
    }

    private MenuPreviewPlacement EffectivePlacement(
        MenuPreviewPrimitive primitive,
        MenuPreviewSettings settings)
    {
        if (_geometryManipulation is { IsActivated: true } state &&
            IsManipulatedPrimitive(primitive, state))
        {
            MenuRectangleValue outerSource =
                state.OriginalPlacement.VirtualRectangle;
            MenuPreviewRect outerVirtualBounds = MenuRectTransform.Unresolve(
                state.CandidateBounds,
                outerSource.HorizontalAlignment,
                outerSource.VerticalAlignment,
                settings);
            MenuRectangleValue source = primitive.Placement.VirtualRectangle;
            MenuRectangleValue effective = source with
            {
                X = outerVirtualBounds.X + source.X - outerSource.X,
                Y = outerVirtualBounds.Y + source.Y - outerSource.Y,
                Width = outerVirtualBounds.Width +
                    source.Width - outerSource.Width,
                Height = outerVirtualBounds.Height +
                    source.Height - outerSource.Height
            };
            return MenuRectTransform.Place(effective, settings);
        }
        return primitive.Placement;
    }

    private MenuPreviewRect EffectiveSelectionBounds(MenuPreviewHitRegion region) =>
        _geometryManipulation is
        {
            IsActivated: true
        } state &&
        state.NodeId == region.NodeId &&
        state.OriginalBounds == region.Bounds
            ? state.CandidateBounds
            : region.Bounds;

    private static bool IsManipulatedPrimitive(
        MenuPreviewPrimitive primitive,
        GeometryManipulation state) =>
            state.NodeId == primitive.NodeId &&
            state.CandidateBounds != state.OriginalBounds;

    private void DrawResizeHandles(DrawingContext context, Rect bounds)
    {
        if (!IsDirectManipulationEnabled)
            return;

        foreach (GeometryOperation operation in ResizeOperations)
        {
            Point center = HandleCenter(bounds, operation);
            context.DrawRectangle(
                ResizeHandleFill,
                ResizeHandleBorder,
                CenteredRect(center, ResizeHandleSize));
        }
    }

    private static GeometryOperation? HitTestResizeHandle(
        Rect bounds,
        Point position)
    {
        GeometryOperation? closest = null;
        double closestDistance = double.MaxValue;
        foreach (GeometryOperation operation in ResizeOperations)
        {
            Point center = HandleCenter(bounds, operation);
            if (!CenteredRect(center, ResizeHandleHitSize).Contains(position))
                continue;

            Vector delta = position - center;
            double distance = delta.SquaredLength;
            if (distance >= closestDistance)
                continue;
            closest = operation;
            closestDistance = distance;
        }
        return closest;
    }

    private static Point HandleCenter(Rect bounds, GeometryOperation operation) =>
        operation switch
        {
            GeometryOperation.NorthWest => bounds.TopLeft,
            GeometryOperation.North => new Point(bounds.Center.X, bounds.Top),
            GeometryOperation.NorthEast => bounds.TopRight,
            GeometryOperation.East => new Point(bounds.Right, bounds.Center.Y),
            GeometryOperation.SouthEast => bounds.BottomRight,
            GeometryOperation.South => new Point(bounds.Center.X, bounds.Bottom),
            GeometryOperation.SouthWest => bounds.BottomLeft,
            GeometryOperation.West => new Point(bounds.Left, bounds.Center.Y),
            _ => bounds.Center
        };

    private static Rect CenteredRect(Point center, double size) =>
        new(center.X - size * 0.5, center.Y - size * 0.5, size, size);

    private static MenuPreviewRect Resize(
        MenuPreviewRect original,
        GeometryOperation operation,
        double deltaX,
        double deltaY)
    {
        if (operation == GeometryOperation.Move)
        {
            return TryFloat(original.X + deltaX, out float movedX) &&
                TryFloat(original.Y + deltaY, out float movedY)
                    ? original with { X = movedX, Y = movedY }
                    : original;
        }

        bool resizeWest = operation is
            GeometryOperation.NorthWest or
            GeometryOperation.West or
            GeometryOperation.SouthWest;
        bool resizeEast = operation is
            GeometryOperation.NorthEast or
            GeometryOperation.East or
            GeometryOperation.SouthEast;
        bool resizeNorth = operation is
            GeometryOperation.NorthWest or
            GeometryOperation.North or
            GeometryOperation.NorthEast;
        bool resizeSouth = operation is
            GeometryOperation.SouthWest or
            GeometryOperation.South or
            GeometryOperation.SouthEast;
        if (!TryResizeAxis(
                original.X,
                original.Width,
                deltaX,
                resizeWest,
                resizeEast,
                out float x,
                out float width) ||
            !TryResizeAxis(
                original.Y,
                original.Height,
                deltaY,
                resizeNorth,
                resizeSouth,
                out float y,
                out float height))
        {
            return original;
        }
        return new MenuPreviewRect(x, y, width, height);
    }

    private static bool TryResizeAxis(
        float rawStart,
        float rawSpan,
        double delta,
        bool resizeMinimum,
        bool resizeMaximum,
        out float candidateStart,
        out float candidateSpan)
    {
        double rawEnd = (double)rawStart + rawSpan;
        double minimum = Math.Min(rawStart, rawEnd);
        double maximum = Math.Max(rawStart, rawEnd);
        if (resizeMinimum)
            minimum = Math.Min(minimum + delta, maximum - MinimumVisualSpan);
        else if (resizeMaximum)
            maximum = Math.Max(maximum + delta, minimum + MinimumVisualSpan);

        bool isForward = rawSpan >= 0;
        double nextStart = isForward ? minimum : maximum;
        double nextEnd = isForward ? maximum : minimum;
        candidateSpan = default;
        return TryFloat(nextStart, out candidateStart) &&
            TryFloat(nextEnd - nextStart, out candidateSpan);
    }

    private static bool IsFinite(MenuPreviewRect value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Width) &&
        float.IsFinite(value.Height);

    private static bool TryFloat(double value, out float result)
    {
        result = (float)value;
        return double.IsFinite(value) && float.IsFinite(result);
    }

    private static Cursor CursorFor(GeometryOperation operation) =>
        operation switch
        {
            GeometryOperation.Move => MoveCursor,
            GeometryOperation.North or GeometryOperation.South =>
                VerticalResizeCursor,
            GeometryOperation.East or GeometryOperation.West =>
                HorizontalResizeCursor,
            GeometryOperation.NorthWest => NorthWestResizeCursor,
            GeometryOperation.NorthEast => NorthEastResizeCursor,
            GeometryOperation.SouthEast => SouthEastResizeCursor,
            GeometryOperation.SouthWest => SouthWestResizeCursor,
            _ => MoveCursor
        };

    private static readonly GeometryOperation[] ResizeOperations =
    [
        GeometryOperation.NorthWest,
        GeometryOperation.North,
        GeometryOperation.NorthEast,
        GeometryOperation.East,
        GeometryOperation.SouthEast,
        GeometryOperation.South,
        GeometryOperation.SouthWest,
        GeometryOperation.West
    ];

    private enum GeometryOperation
    {
        Move,
        NorthWest,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West
    }

    private sealed class GeometryManipulation(
        MenuNodeId nodeId,
        MenuPreviewPlacement originalPlacement,
        GeometryOperation operation,
        Point pressLocal,
        Point pressVirtual,
        PreviewTransform transform)
    {
        public MenuNodeId NodeId { get; } = nodeId;
        public MenuPreviewPlacement OriginalPlacement { get; } =
            originalPlacement;
        public MenuPreviewRect OriginalBounds =>
            OriginalPlacement.OutputBounds;
        public GeometryOperation Operation { get; } = operation;
        public Point PressLocal { get; } = pressLocal;
        public Point PressVirtual { get; } = pressVirtual;
        public PreviewTransform Transform { get; } = transform;
        public bool IsActivated { get; set; }
        public MenuPreviewRect CandidateBounds { get; set; } =
            originalPlacement.OutputBounds;
    }
}
