using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vector = Avalonia.Vector;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

public enum AddonEntityScenePlane { Top, Side }
public enum AddonEntitySceneMode { Normal, Place, Relink }

// Top and Side points use entity-local X/Y and X/Z respectively. Backdrop paths
// supplied through SetBackdrop instead use world coordinates.
public sealed record AddonEntitySceneOutline(IReadOnlyList<Vector2> Top, IReadOnlyList<Vector2> Side);
public sealed record AddonEntitySceneBox(Vector3 Mid, Vector3 Half);
public sealed record AddonEntitySceneObject(
    int Index, string Label, string Kind, Vector3 Origin, float? Radius = null,
    float? YawDegrees = null, AddonEntitySceneBox? Box = null,
    IReadOnlyList<AddonEntitySceneOutline>? Outlines = null,
    string? TargetName = null, string? Target = null, bool CanEdit = true);
public readonly record struct AddonEntitySceneEdit(
    int Index, Vector3 Origin, float? Radius, float? YawDegrees, Vector3? BoxHalf);

public sealed class AddonEntityScene : Control
{
    private static readonly IBrush Stage = new SolidColorBrush(Color.FromRgb(24, 33, 42));
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(223, 232, 233));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(154, 175, 185));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(116, 220, 183));
    private static readonly IBrush TriggerFill = new SolidColorBrush(Color.FromArgb(24, 102, 187, 212));
    private static readonly IBrush SelectedFill = new SolidColorBrush(Color.FromArgb(48, 116, 220, 183));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(35, 179, 202, 209)), 1);
    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.FromArgb(64, 130, 157, 170));
    private static readonly Pen VolumePen = new(new SolidColorBrush(Color.FromRgb(89, 162, 192)), 1.3);
    private static readonly Pen SelectedPen = new(Accent, 2);
    private static readonly Pen MutedPen = new(Muted, 1.5);
    private static readonly Pen LinkPen = new(new SolidColorBrush(Color.FromRgb(225, 185, 111)), 1.4, DashStyle.Dash);
    private static readonly Pen DenseLinkPen = new(new SolidColorBrush(Color.FromArgb(55, 225, 185, 111)), 1);
    private static readonly Pen XPen = new(new SolidColorBrush(Color.FromRgb(245, 129, 112)), 2);
    private static readonly Pen VerticalPen = new(new SolidColorBrush(Color.FromRgb(128, 196, 246)), 2);

    private IReadOnlyList<AddonEntitySceneObject> _objects = [];
    private StreamGeometry? _backdropTop;
    private StreamGeometry? _backdropSide;
    private int _selectedIndex = -1;
    private AddonEntityScenePlane _plane;
    private AddonEntitySceneMode _mode;
    private AddonEntitySceneObject? _ghost;
    private Vector2 _center;
    private double _scale = 1;
    private bool _fitted;
    private bool _pointerInside;
    private Point _pointerPosition;
    private Drag? _drag;

    private enum Handle { None, Center, X, Vertical, Radius, Yaw, BoxCorner, Pan }
    private sealed class Drag
    {
        public required IPointer Pointer { get; init; }
        public required Point Start { get; init; }
        public required Handle Handle { get; init; }
        public required AddonEntitySceneObject? Object { get; init; }
        public required Vector2 Center { get; init; }
        public required double Scale { get; init; }
        public bool Moved { get; set; }
        public AddonEntitySceneObject? Preview { get; set; }
    }

    public AddonEntityScene()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public event Action<int>? SelectionRequested;
    public event Action<AddonEntitySceneEdit>? EditRequested;
    public event Action<Vector3>? PlacementRequested;
    public event Action<int>? RelinkRequested;
    public event Action? CancelRequested;

    public AddonEntityScenePlane Plane
    {
        get => _plane;
        set
        {
            if (_plane == value) return;
            _plane = value;
            FrameSelected();
        }
    }

    public AddonEntitySceneMode Mode
    {
        get => _mode;
        set { _mode = value; InvalidateVisual(); }
    }

    public AddonEntitySceneObject? Ghost
    {
        get => _ghost;
        set { _ghost = value; InvalidateVisual(); }
    }

    public void SetScene(IReadOnlyList<AddonEntitySceneObject> objects, int selectedIndex)
    {
        _objects = objects;
        _selectedIndex = selectedIndex;
        if (!_fitted && Bounds.Width > 0 && Bounds.Height > 0) FitView();
        else InvalidateVisual();
    }

    private bool _showBackdrop;
    internal bool ShowBackdrop
    {
        get => _showBackdrop;
        set { _showBackdrop = value; InvalidateVisual(); }
    }

    internal void SetBackdrop(IReadOnlyList<AddonEntitySceneOutline> paths)
    {
        _backdropTop = BackdropGeometry(paths, top: true);
        _backdropSide = BackdropGeometry(paths, top: false);
        InvalidateVisual();
    }

    private static StreamGeometry? BackdropGeometry(IReadOnlyList<AddonEntitySceneOutline> paths, bool top)
    {
        if (paths.Count == 0) return null;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach (AddonEntitySceneOutline outline in paths)
            {
                IReadOnlyList<Vector2> points = top ? outline.Top : outline.Side;
                if (points.Count < 2) continue;
                context.BeginFigure(new Point(points[0].X, points[0].Y), isFilled: false);
                for (int i = 1; i < points.Count; i++)
                    context.LineTo(new Point(points[i].X, points[i].Y));
                context.EndFigure(points.Count > 2);
            }
        }
        return geometry;
    }

    public void FitView()
    {
        Fit(_objects);
    }

    public void FrameSelected()
    {
        AddonEntitySceneObject? selected = Selected();
        if (selected is not null) Fit([selected]);
        else FitView();
    }

    public void Zoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        _scale = Math.Clamp(_scale * factor, 0.001, 100);
        _fitted = true;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted) FitView();
    }

    private void Fit(IEnumerable<AddonEntitySceneObject> objects)
    {
        float minX = float.PositiveInfinity, minV = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxV = float.NegativeInfinity;
        void Include(Vector2 point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return;
            minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
            minV = Math.Min(minV, point.Y); maxV = Math.Max(maxV, point.Y);
        }
        foreach (AddonEntitySceneObject obj in objects)
        {
            Vector2 origin = PlanePosition(obj.Origin);
            bool hasShape = false;
            if (obj.Box is { } box)
            {
                Vector2 middle = origin + PlanePosition(box.Mid);
                Vector2 half = PlanePosition(box.Half);
                Include(middle - half); Include(middle + half);
                hasShape = true;
            }
            foreach (AddonEntitySceneOutline outline in obj.Outlines ?? [])
                foreach (Vector2 point in Points(outline)) { Include(origin + point); hasShape = true; }
            if (hasShape) continue;
            Include(origin);
            if (obj.Radius is > 0 && _plane == AddonEntityScenePlane.Top)
            {
                Include(origin + new Vector2(obj.Radius.Value));
                Include(origin - new Vector2(obj.Radius.Value));
            }
        }
        if (!float.IsFinite(minX))
        {
            _center = Vector2.Zero;
            _scale = 1;
        }
        else
        {
            _center = new Vector2((minX + maxX) * 0.5f, (minV + maxV) * 0.5f);
            _scale = Math.Clamp(Math.Min(Math.Max(1, Bounds.Width - 90) / Math.Max(180, maxX - minX),
                Math.Max(1, Bounds.Height - 80) / Math.Max(180, maxV - minV)), 0.001, 100);
        }
        _fitted = true;
        InvalidateVisual();
    }

    private Vector2 PlanePosition(Vector3 vector) => new(vector.X,
        _plane == AddonEntityScenePlane.Top ? vector.Y : vector.Z);

    private IReadOnlyList<Vector2> Points(AddonEntitySceneOutline outline) =>
        _plane == AddonEntityScenePlane.Top ? outline.Top : outline.Side;

    private Point Project(Vector2 point) => new(
        Bounds.Width * 0.5 + (point.X - _center.X) * _scale,
        Bounds.Height * 0.5 - (point.Y - _center.Y) * _scale);

    private Vector2 Unproject(Point point) => new(
        (float)(_center.X + (point.X - Bounds.Width * 0.5) / _scale),
        (float)(_center.Y - (point.Y - Bounds.Height * 0.5) / _scale));

    private AddonEntitySceneObject? Selected() => _objects.FirstOrDefault(obj => obj.Index == _selectedIndex);

    private AddonEntitySceneObject Display(AddonEntitySceneObject obj) =>
        _drag?.Preview is { } preview && preview.Index == obj.Index ? preview : obj;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.DrawRectangle(Stage, null, new Rect(Bounds.Size));
        DrawGrid(context);
        if (ShowBackdrop) DrawBackdrop(context);
        AddonEntitySceneObject? selected = Selected();
        if (selected is not null) DrawLinks(context, Display(selected));
        bool labelLinks = selected is not null && _objects.Count(obj => Linked(selected, obj)) <= 6;
        foreach (AddonEntitySceneObject obj in _objects)
            if (obj.Index != _selectedIndex) DrawObject(context, obj, false,
                showRelatedLabel: labelLinks && selected is not null && Linked(selected, obj));
        if (selected is not null) DrawObject(context, Display(selected), true);
        if (_mode == AddonEntitySceneMode.Place && _ghost is not null && _pointerInside)
        {
            Vector2 point = Unproject(_pointerPosition);
            Vector3 origin = _plane == AddonEntityScenePlane.Top
                ? _ghost.Origin with { X = point.X, Y = point.Y }
                : _ghost.Origin with { X = point.X, Z = point.Y };
            DrawObject(context, _ghost with { Origin = origin }, true, ghost: true);
        }
        string label = _plane == AddonEntityScenePlane.Top ? "TOP · X / Y" : "SIDE · X / Z";
        double grid = GridUnits();
        DrawText(context, $"{label}    ·    Grid {grid:G} units", new Point(10, 8), Muted, 11);
    }

    private double GridUnits() => Math.Pow(2, Math.Round(Math.Log2(55 / Math.Max(0.001, _scale))));

    private void DrawGrid(DrawingContext context)
    {
        double step = GridUnits() * _scale;
        if (step < 2 || !double.IsFinite(step)) return;
        Point zero = Project(Vector2.Zero);
        double x0 = ((zero.X % step) + step) % step;
        double y0 = ((zero.Y % step) + step) % step;
        for (double x = x0; x < Bounds.Width; x += step)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = y0; y < Bounds.Height; y += step)
            context.DrawLine(GridPen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawBackdrop(DrawingContext context)
    {
        StreamGeometry? geometry = _plane == AddonEntityScenePlane.Top ? _backdropTop : _backdropSide;
        if (geometry is null) return;
        var transform = new Matrix(_scale, 0, 0, -_scale,
            Bounds.Width * 0.5 - _center.X * _scale,
            Bounds.Height * 0.5 + _center.Y * _scale);
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(transform))
            context.DrawGeometry(null, new Pen(BackdropBrush, 1 / _scale), geometry);
    }

    private void DrawLinks(DrawingContext context, AddonEntitySceneObject selected)
    {
        Pen pen = _objects.Count(obj => Linked(selected, obj)) > 12 ? DenseLinkPen : LinkPen;
        Point from = Project(FootprintCenter(selected));
        foreach (AddonEntitySceneObject other in _objects)
        {
            if (!Linked(selected, other)) continue;
            context.DrawLine(pen, from, Project(FootprintCenter(other)));
        }
    }

    private static bool Linked(AddonEntitySceneObject selected, AddonEntitySceneObject other) =>
        other.Index != selected.Index &&
        ((!string.IsNullOrEmpty(selected.Target) && selected.Target == other.TargetName) ||
         (!string.IsNullOrEmpty(selected.TargetName) && selected.TargetName == other.Target));

    private Vector2 FootprintCenter(AddonEntitySceneObject obj)
    {
        Vector2 origin = PlanePosition(obj.Origin);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (AddonEntitySceneOutline outline in obj.Outlines ?? [])
        foreach (Vector2 point in Points(outline))
        {
            minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
        }
        if (float.IsFinite(minX)) return origin + new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        return origin + (obj.Box is { } box ? PlanePosition(box.Mid) : Vector2.Zero);
    }

    private void DrawObject(DrawingContext context, AddonEntitySceneObject obj, bool selected,
        bool ghost = false, bool showRelatedLabel = false)
    {
        Vector2 origin = PlanePosition(obj.Origin);
        Point center = Project(FootprintCenter(obj));
        Pen pen = selected ? SelectedPen : obj.Kind == "trigger" ? VolumePen : MutedPen;
        foreach (AddonEntitySceneOutline outline in obj.Outlines ?? [])
            DrawOutline(context, outline, origin, obj.Kind == "trigger" ?
                selected ? SelectedFill : TriggerFill : null, pen);
        if (obj.Box is { } box && (obj.Outlines is null || obj.Outlines.Count == 0))
        {
            Vector2 half = PlanePosition(box.Half);
            context.DrawRectangle(selected ? SelectedFill : TriggerFill, pen,
                new Rect(center.X - half.X * _scale, center.Y - half.Y * _scale,
                    2 * half.X * _scale, 2 * half.Y * _scale));
        }
        if (selected && obj.Radius is > 0 && _plane == AddonEntityScenePlane.Top)
            context.DrawEllipse(SelectedFill, SelectedPen, center, obj.Radius.Value * _scale, obj.Radius.Value * _scale);
        if (selected && obj.YawDegrees is { } yaw && _plane == AddonEntityScenePlane.Top)
        {
            double angle = yaw * Math.PI / 180;
            Point tip = center + new Vector(30 * Math.Cos(angle), -30 * Math.Sin(angle));
            context.DrawLine(SelectedPen, center, tip);
            context.DrawEllipse(Stage, SelectedPen, tip, 5, 5);
        }
        DrawMarker(context, obj.Kind, center, selected, pen);
        if (selected && obj.CanEdit && !ghost && _mode == AddonEntitySceneMode.Normal)
            DrawHandles(context, obj, center);
        if (selected || showRelatedLabel)
            DrawText(context, obj.Label, center + new Vector(center.X < Bounds.Width * 0.65 ? 12 : -12, -23),
                selected ? Ink : Muted, 12, center.X >= Bounds.Width * 0.65);
    }

    private static void DrawMarker(DrawingContext context, string kind, Point center, bool selected, Pen pen)
    {
        IBrush fill = selected ? Accent : Stage;
        switch (kind)
        {
            case "start":
                DrawPolygon(context,
                    [center + new Vector(0, -8), center + new Vector(7, 6), center + new Vector(-7, 6)],
                    fill, pen);
                break;
            case "actor":
                DrawPolygon(context,
                    [center + new Vector(0, -8), center + new Vector(7, 0),
                        center + new Vector(0, 8), center + new Vector(-7, 0)], fill, pen);
                break;
            case "pickup":
                context.DrawRectangle(fill, pen, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case "model":
                DrawPolygon(context,
                    [center + new Vector(-7, 0), center + new Vector(-4, -6),
                        center + new Vector(4, -6), center + new Vector(7, 0),
                        center + new Vector(4, 6), center + new Vector(-4, 6)], fill, pen);
                break;
            case "trigger":
                context.DrawEllipse(fill, pen, center, 5, 5);
                context.DrawLine(pen, center + new Vector(-9, 0), center + new Vector(9, 0));
                context.DrawLine(pen, center + new Vector(0, -9), center + new Vector(0, 9));
                break;
            case "marker":
                context.DrawEllipse(null, pen, center, 8, 8);
                context.DrawEllipse(fill, pen, center, 3, 3);
                break;
            default:
                context.DrawEllipse(fill, pen, center, selected ? 6 : 4, selected ? 6 : 4);
                break;
        }
    }

    private void DrawHandles(DrawingContext context, AddonEntitySceneObject obj, Point center)
    {
        Point x = center + new Vector(43, 0), vertical = center + new Vector(0, -43);
        context.DrawLine(XPen, center, x);
        context.DrawLine(VerticalPen, center, vertical);
        context.DrawRectangle(XPen.Brush, new Pen(Stage, 1), new Rect(x.X - 4, x.Y - 4, 8, 8));
        context.DrawRectangle(VerticalPen.Brush, new Pen(Stage, 1),
            new Rect(vertical.X - 4, vertical.Y - 4, 8, 8));
        if (obj.Radius is { } radius && _plane == AddonEntityScenePlane.Top)
        {
            Point grip = center + new Vector(Math.Max(18, radius * _scale), 0);
            context.DrawRectangle(Accent, new Pen(Stage, 2), new Rect(grip.X - 5, grip.Y - 5, 10, 10));
        }
        if (obj.Box is { } box && (obj.Outlines is null || obj.Outlines.Count == 0))
        {
            Vector2 half = PlanePosition(box.Half);
            Point grip = center + new Vector(half.X * _scale, -half.Y * _scale);
            context.DrawRectangle(Accent, new Pen(Stage, 2), new Rect(grip.X - 5, grip.Y - 5, 10, 10));
        }
    }

    private void DrawOutline(DrawingContext context, AddonEntitySceneOutline outline,
        Vector2 origin, IBrush? fill, Pen pen)
    {
        IReadOnlyList<Vector2> points = Points(outline);
        if (points.Count < 2) return;
        Point[] screen = points.Select(point => Project(origin + point)).ToArray();
        if (screen.Length == 2)
        {
            context.DrawLine(pen, screen[0], screen[1]);
            return;
        }
        DrawPolygon(context, screen, fill, pen);
    }

    private static void DrawPolygon(DrawingContext context, IReadOnlyList<Point> points, IBrush? fill, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(points[0], isFilled: fill is not null);
            for (int i = 1; i < points.Count; i++) path.LineTo(points[i]);
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush,
        double size, bool rightAligned = false)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        context.DrawText(formatted, new Point(rightAligned ? at.X - formatted.Width : at.X, at.Y));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerInside = true;
        _pointerPosition = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerInside = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_drag is not null) return;
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
        Focus();
        Point point = e.GetPosition(this);
        if (_mode != AddonEntitySceneMode.Normal)
        {
            _drag = new Drag { Pointer = e.Pointer, Start = point, Handle = Handle.None,
                Object = null, Center = _center, Scale = _scale };
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        AddonEntitySceneObject? obj = null;
        Handle handle = Handle.Pan;
        if (!properties.IsMiddleButtonPressed)
        {
            AddonEntitySceneObject? selected = Selected();
            if (selected is { CanEdit: true })
            {
                Handle grip = HitHandle(selected, point);
                if (grip != Handle.None) { obj = selected; handle = grip; }
            }
            if (obj is null)
            {
                obj = HitObject(point);
                if (obj is not null)
                {
                    if (_selectedIndex != obj.Index)
                    {
                        _selectedIndex = obj.Index;
                        SelectionRequested?.Invoke(obj.Index);
                    }
                    handle = obj.CanEdit ? Handle.Center : Handle.None;
                }
            }
        }
        _drag = new Drag { Pointer = e.Pointer, Start = point, Handle = handle,
            Object = obj, Center = _center, Scale = _scale };
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    private Handle HitHandle(AddonEntitySceneObject obj, Point point)
    {
        Point center = Project(FootprintCenter(obj));
        if (obj.Box is { } box && (obj.Outlines is null || obj.Outlines.Count == 0))
        {
            Vector2 half = PlanePosition(box.Half);
            if (Near(point, center + new Vector(half.X * _scale, -half.Y * _scale), 11))
                return Handle.BoxCorner;
        }
        if (obj.Radius is { } radius && _plane == AddonEntityScenePlane.Top &&
            Near(point, center + new Vector(Math.Max(18, radius * _scale), 0), 11))
            return Handle.Radius;
        if (obj.YawDegrees is { } yaw && _plane == AddonEntityScenePlane.Top)
        {
            double angle = yaw * Math.PI / 180;
            if (Near(point, center + new Vector(30 * Math.Cos(angle), -30 * Math.Sin(angle)), 11))
                return Handle.Yaw;
        }
        if (Near(point, center + new Vector(43, 0), 11)) return Handle.X;
        if (Near(point, center + new Vector(0, -43), 11)) return Handle.Vertical;
        return Near(point, center, 12) ? Handle.Center : Handle.None;
    }

    private AddonEntitySceneObject? HitObject(Point point)
    {
        // Centers take precedence when trigger volumes overlap an object marker.
        foreach (AddonEntitySceneObject obj in _objects.Reverse())
            if (Near(point, Project(FootprintCenter(obj)), 12)) return obj;
        Vector2 world = Unproject(point);
        foreach (AddonEntitySceneObject obj in _objects.Reverse())
        {
            Vector2 local = world - PlanePosition(obj.Origin);
            if (obj.Box is { } box && (obj.Outlines is null || obj.Outlines.Count == 0))
            {
                Vector2 mid = PlanePosition(box.Mid), half = PlanePosition(box.Half);
                if (Math.Abs(local.X - mid.X) <= half.X && Math.Abs(local.Y - mid.Y) <= half.Y)
                    return obj;
            }
            foreach (AddonEntitySceneOutline outline in obj.Outlines ?? [])
                if (Contains(Points(outline), local)) return obj;
        }
        return null;
    }

    private static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        if (polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool Near(Point a, Point b, double radius) =>
        ((Vector)(a - b)).Length <= radius;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        _pointerPosition = point;
        if (_mode == AddonEntitySceneMode.Place) InvalidateVisual();
        if (_drag is not { } drag || drag.Pointer != e.Pointer)
        {
            return;
        }
        Vector delta = point - drag.Start;
        if (!drag.Moved && delta.Length < 3) return;
        drag.Moved = true;
        if (drag.Handle == Handle.Pan)
        {
            _center = drag.Center - new Vector2((float)(delta.X / drag.Scale),
                (float)(-delta.Y / drag.Scale));
            InvalidateVisual();
            return;
        }
        if (drag.Object is not { } obj) return;
        Vector3 origin = obj.Origin;
        float? radius = obj.Radius, yaw = obj.YawDegrees;
        AddonEntitySceneBox? box = obj.Box;
        float dx = (float)(delta.X / drag.Scale), dv = (float)(-delta.Y / drag.Scale);
        switch (drag.Handle)
        {
            case Handle.Center:
                origin.X = Round1(origin.X + dx);
                if (_plane == AddonEntityScenePlane.Top) origin.Y = Round1(origin.Y + dv);
                else origin.Z = Round1(origin.Z + dv);
                break;
            case Handle.X: origin.X = Round1(origin.X + dx); break;
            case Handle.Vertical:
                if (_plane == AddonEntityScenePlane.Top) origin.Y = Round1(origin.Y + dv);
                else origin.Z = Round1(origin.Z + dv);
                break;
            case Handle.Radius:
                radius = Round1((float)Math.Max(0,
                    ((Vector)(point - Project(FootprintCenter(obj)))).Length / drag.Scale));
                break;
            case Handle.Yaw:
                Point center = Project(FootprintCenter(obj));
                yaw = Round1((float)((Math.Atan2(center.Y - point.Y, point.X - center.X) * 180 / Math.PI + 360) % 360));
                break;
            case Handle.BoxCorner when box is not null:
                Point boxCenter = Project(FootprintCenter(obj));
                float halfX = Round1((float)Math.Max(1, Math.Abs(point.X - boxCenter.X) / drag.Scale));
                float halfV = Round1((float)Math.Max(1, Math.Abs(point.Y - boxCenter.Y) / drag.Scale));
                Vector3 half = box.Half with { X = halfX };
                if (_plane == AddonEntityScenePlane.Top) half.Y = halfV;
                else half.Z = halfV;
                box = box with { Half = half };
                break;
        }
        drag.Preview = obj with { Origin = origin, Radius = radius, YawDegrees = yaw, Box = box };
        InvalidateVisual();
        e.Handled = true;
    }

    private static float Round1(float value) => MathF.Round(value, 1, MidpointRounding.AwayFromZero);

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } drag || drag.Pointer != e.Pointer) return;
        if (_mode == AddonEntitySceneMode.Place)
        {
            Vector2 at = Unproject(e.GetPosition(this));
            Vector3 position = _ghost?.Origin ?? Vector3.Zero;
            position.X = at.X;
            if (_plane == AddonEntityScenePlane.Top) position.Y = at.Y;
            else position.Z = at.Y;
            PlacementRequested?.Invoke(position);
        }
        else if (_mode == AddonEntitySceneMode.Relink)
        {
            AddonEntitySceneObject? target = HitObject(e.GetPosition(this));
            if (target is not null && target.Index != _selectedIndex) RelinkRequested?.Invoke(target.Index);
        }
        else if (drag.Moved && drag.Preview is { } preview && drag.Object is { } original &&
                 (preview.Origin != original.Origin || preview.Radius != original.Radius ||
                  preview.YawDegrees != original.YawDegrees || preview.Box != original.Box))
            EditRequested?.Invoke(new AddonEntitySceneEdit(preview.Index, preview.Origin,
                preview.Radius, preview.YawDegrees, preview.Box?.Half));
        EndDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point point = e.GetPosition(this);
        Vector2 before = Unproject(point);
        Zoom(Math.Pow(1.2, e.Delta.Y));
        Vector2 after = Unproject(point);
        _center += before - after;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        if (_drag is not null) EndDrag();
        CancelRequested?.Invoke();
        e.Handled = true;
    }

    private void EndDrag()
    {
        IPointer? pointer = _drag?.Pointer;
        _drag = null;
        if (pointer?.Captured == this) pointer.Capture(null);
        InvalidateVisual();
    }
}
