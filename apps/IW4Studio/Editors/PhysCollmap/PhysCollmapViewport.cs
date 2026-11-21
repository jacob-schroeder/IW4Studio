using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Formats.XModel;
using IW4.Game.Assets.Physics;
using Vector = Avalonia.Vector;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

public enum PhysCollmapViewportTool { Move, Resize, Rotate }
public enum PhysCollmapViewportLayer { Overlay, Model, Shape }
public enum PhysCollmapViewportPhase { Begin, Update, Commit, Cancel }

public sealed record PhysCollmapViewportShape(
    PhysGeomShape Shape, Vector3 Position, Vector3 Size, Vector3 RotationDegrees,
    BrushWrapper? Brush = null, bool CanTransform = true, bool CanRotate = true);

public readonly record struct PhysCollmapViewportEdit(
    int Index, Vector3 Position, Vector3 Size, Vector3 RotationDegrees,
    PhysCollmapViewportPhase Phase);

public sealed partial class PhysCollmapViewport : Control
{
    private static readonly IBrush Stage = new SolidColorBrush(Color.FromRgb(39, 47, 46));
    private static readonly Pen Grid = new(new SolidColorBrush(Color.FromArgb(32, 176, 195, 183)), 1);
    private static readonly IBrush ShapeFill = new SolidColorBrush(Color.FromArgb(17, 122, 220, 185));
    private static readonly Pen ShapePen = new(new SolidColorBrush(Color.FromArgb(153, 122, 220, 185)), 1.25);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.FromRgb(144, 239, 207)), 2);
    private static readonly Pen SelectedBackPen = new(new SolidColorBrush(Color.FromArgb(110, 144, 239, 207)), 1, DashStyle.Dash);
    private static readonly IBrush BalanceBrush = new SolidColorBrush(Color.FromRgb(244, 206, 111));
    private static readonly Pen BalancePen = new(BalanceBrush, 1.4);
    private static readonly IBrush[] AxisBrushes =
    [
        new SolidColorBrush(Color.FromRgb(250, 141, 124)),
        new SolidColorBrush(Color.FromRgb(130, 214, 142)),
        new SolidColorBrush(Color.FromRgb(138, 182, 255))
    ];
    private static readonly FormattedText[] AxisLabels =
    [
        new("X", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, AxisBrushes[0]),
        new("Y", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, AxisBrushes[1]),
        new("Z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, AxisBrushes[2])
    ];
    private static readonly PhysCollmapViewportGeometry.Outline Box = PhysCollmapViewportGeometry.Box;
    private static readonly PhysCollmapViewportGeometry.Outline Cylinder = PhysCollmapViewportGeometry.Cylinder;

    private IReadOnlyList<PhysCollmapViewportShape> _shapes = [];
    private IReadOnlyList<Vector3> _modelVertices = [];
    private IReadOnlyList<XModelExportTriangle> _modelTriangles = [];
    private readonly Dictionary<BrushWrapper, PhysCollmapViewportGeometry.Outline> _brushOutlines = new();
    private int _selectedIndex = -1;
    private Vector3 _balancePoint;
    private bool _fitted;
    private Vector3 _viewCenter;
    private double _unitsPerPixel = 1;
    private double _orbitDegrees = 349;
    private PhysCollmapViewportTool _tool;
    private PhysCollmapViewportLayer _layer;
    private bool _editBalancePoint;
    private bool _canEdit = true;
    private Drag? _drag;
    private int _hotAxis = -1;


    public PhysCollmapViewport()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public event Action<int>? SelectionRequested;
    public event Action<PhysCollmapViewportEdit>? EditRequested;

    public PhysCollmapViewportTool Tool
    {
        get => _tool;
        set { _tool = value; InvalidateVisual(); }
    }

    public PhysCollmapViewportLayer Layer
    {
        get => _layer;
        set { _layer = value; InvalidateVisual(); }
    }

    public bool EditBalancePoint
    {
        get => _editBalancePoint;
        set { _editBalancePoint = value; InvalidateVisual(); }
    }

    public bool CanEdit
    {
        get => _canEdit;
        set { _canEdit = value; InvalidateVisual(); }
    }

    public double OrbitDegrees
    {
        get => _orbitDegrees;
        set { _orbitDegrees = value % 360; _redrawModel = true; InvalidateVisual(); }
    }

    public void RotateView(double deltaDegrees) => OrbitDegrees += deltaDegrees;

    public void SetScene(
        IReadOnlyList<PhysCollmapViewportShape> shapes, int selectedIndex, Vector3 balancePoint,
        IReadOnlyList<Vector3> modelVertices, IReadOnlyList<XModelExportTriangle> modelTriangles)
    {
        _shapes = shapes;
        if (_brushOutlines.Count > 0)
        {
            var activeBrushes = new HashSet<BrushWrapper>(shapes.Where(s => s.Brush is not null).Select(s => s.Brush!));
            foreach (BrushWrapper brush in _brushOutlines.Keys.ToArray())
                if (!activeBrushes.Contains(brush)) _brushOutlines.Remove(brush);
        }
        _selectedIndex = selectedIndex;
        _balancePoint = balancePoint;
        if (!ReferenceEquals(_modelVertices, modelVertices) || !ReferenceEquals(_modelTriangles, modelTriangles))
        {
            _modelVertices = modelVertices;
            _modelTriangles = modelTriangles;
            _redrawModel = true;
            if (_drag is null) _fitted = false;
        }
        InvalidateVisual();
    }

    public void FitView()
    {
        _fitted = false;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_drag is null) FitView();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        context.DrawRectangle(Stage, null, new Rect(Bounds.Size));
        if (!_fitted)
            FitScene();
        DrawGrid(context);
        if (_layer != PhysCollmapViewportLayer.Shape)
            DrawModel(context);
        if (_layer != PhysCollmapViewportLayer.Model)
        {
            for (int i = 0; i < _shapes.Count; i++)
                if (i != _selectedIndex) DrawShape(context, i, false);
            if (_selectedIndex >= 0 && _selectedIndex < _shapes.Count)
                DrawShape(context, _selectedIndex, true);
            DrawBalancePoint(context);
            if (_canEdit && (_editBalancePoint || _selectedIndex >= 0 && _selectedIndex < _shapes.Count &&
                _shapes[_selectedIndex].CanTransform && (_tool != PhysCollmapViewportTool.Rotate || _shapes[_selectedIndex].CanRotate)))
                DrawGizmo(context);
        }
    }

    private Point Project(Vector3 point)
    {
        double yaw = _orbitDegrees * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        Vector3 p = point - _viewCenter;
        double rx = p.X * cy - p.Y * sy;
        double ry = p.X * sy + p.Y * cy;
        return new Point(Bounds.Width * 0.5 + (rx - ry) * 0.866 / _unitsPerPixel,
            Bounds.Height * 0.52 + ((rx + ry) * 0.5 - p.Z * 0.92) / _unitsPerPixel);
    }

    private void FitScene()
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        void Include(Vector3 p)
        {
            if (!PhysCollmapViewportGeometry.Finite(p)) return;
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        foreach (Vector3 p in _modelVertices) Include(p);
        foreach (PhysCollmapViewportShape shape in _shapes)
        {
            var basis = PhysCollmapViewportGeometry.Basis(shape.RotationDegrees);
            foreach (Vector3 local in OutlineFor(shape).Vertices)
                Include(PhysCollmapViewportGeometry.World(local, shape.Position, shape.Size, basis));
        }
        Include(_balancePoint);
        if (!PhysCollmapViewportGeometry.Finite(min))
        {
            min = new Vector3(-1); max = new Vector3(1);
        }
        _viewCenter = min * 0.5f + max * 0.5f;
        var corners = new List<Point>(8);
        double yaw = _orbitDegrees * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 p = new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
            p -= _viewCenter;
            double rx = p.X * cy - p.Y * sy, ry = p.X * sy + p.Y * cy;
            corners.Add(new Point((rx - ry) * 0.866, (rx + ry) * 0.5 - p.Z * 0.92));
        }
        double spanX = corners.Max(p => p.X) - corners.Min(p => p.X);
        double spanY = corners.Max(p => p.Y) - corners.Min(p => p.Y);
        _unitsPerPixel = Math.Max(0.001, Math.Max(spanX / Math.Max(1, Bounds.Width * 0.82),
            spanY / Math.Max(1, Bounds.Height * 0.76)));
        _redrawModel = true;
        _fitted = true;
    }

    private void DrawGrid(DrawingContext context)
    {
        double extent = Math.Max(8, _unitsPerPixel * Math.Max(Bounds.Width, Bounds.Height));
        double spacing = Math.Pow(2, Math.Ceiling(Math.Log2(extent / 12)));
        float originX = (float)(Math.Floor(_viewCenter.X / spacing) * spacing);
        float originY = (float)(Math.Floor(_viewCenter.Y / spacing) * spacing);
        float reach = (float)(spacing * 14);
        for (int i = -14; i <= 14; i++)
        {
            float x = originX + (float)(i * spacing), y = originY + (float)(i * spacing);
            context.DrawLine(Grid, Project(new Vector3(x, originY - reach, 0)), Project(new Vector3(x, originY + reach, 0)));
            context.DrawLine(Grid, Project(new Vector3(originX - reach, y, 0)), Project(new Vector3(originX + reach, y, 0)));
        }
    }

    private PhysCollmapViewportGeometry.Outline OutlineFor(PhysCollmapViewportShape shape)
    {
        if (shape.Shape == PhysGeomShape.Cylinder) return Cylinder;
        if (shape.Shape != PhysGeomShape.Brush || shape.Brush is null) return Box;
        if (!_brushOutlines.TryGetValue(shape.Brush, out var outline))
            _brushOutlines[shape.Brush] = outline = PhysCollmapViewportGeometry.Brush(shape.Brush);
        return outline;
    }

    private (Point[] Points, (int A, int B)[] Edges) ScreenOutline(PhysCollmapViewportShape shape)
    {
        var outline = OutlineFor(shape);
        var basis = PhysCollmapViewportGeometry.Basis(shape.RotationDegrees);
        var points = new Point[outline.Vertices.Length];
        for (int i = 0; i < points.Length; i++)
            points[i] = Project(PhysCollmapViewportGeometry.World(outline.Vertices[i], shape.Position, shape.Size, basis));
        return (points, outline.Edges);
    }

    private void DrawShape(DrawingContext context, int index, bool selected)
    {
        var (points, edges) = ScreenOutline(_shapes[index]);
        Point[] hull = ConvexHull(points);
        if (hull.Length >= 3)
        {
            var fill = new StreamGeometry();
            using (StreamGeometryContext path = fill.Open())
            {
                path.BeginFigure(hull[0], isFilled: true);
                for (int i = 1; i < hull.Length; i++) path.LineTo(hull[i]);
                path.EndFigure(true);
            }
            context.DrawGeometry(ShapeFill, selected ? SelectedPen : ShapePen, fill);
        }
        Pen pen = selected ? SelectedPen : ShapePen;
        foreach (var (a, b) in edges)
            context.DrawLine(pen, points[a], points[b]);
        if (selected && points.Length > 0)
            context.DrawEllipse(ShapeFill, SelectedBackPen, Project(_shapes[index].Position), 4, 4);
    }

    private void DrawBalancePoint(DrawingContext context)
    {
        Point p = Project(_balancePoint);
        context.DrawEllipse(BalanceBrush, null, p, 4, 4);
        context.DrawLine(BalancePen, p + new Vector(-10, 0), p + new Vector(10, 0));
        context.DrawLine(BalancePen, p + new Vector(0, -10), p + new Vector(0, 10));
    }

    private readonly record struct Grip(int Axis, Point[] Points, Vector3 WorldAxis);

    private Grip[] GetGrips()
    {
        bool balance = _editBalancePoint;
        var shape = balance ? null : _selectedIndex >= 0 && _selectedIndex < _shapes.Count ? _shapes[_selectedIndex] : null;
        if (!balance && shape is null) return [];
        Vector3 origin = balance ? _balancePoint : shape!.Position;
        Vector3[] basis = shape is null ? [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ]
            : PhysCollmapViewportGeometry.Basis(shape.RotationDegrees);
        int axisCount = !balance && _tool == PhysCollmapViewportTool.Rotate && shape?.Shape == PhysGeomShape.Cylinder ? 2 : 3;
        var grips = new Grip[axisCount];
        for (int axis = 0; axis < axisCount; axis++)
        {
            Vector3 worldAxis = _tool == PhysCollmapViewportTool.Resize && !balance ? basis[axis]
                : axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            Point center = Project(origin);
            Point unit = Project(origin + worldAxis);
            Vector direction = unit - center;
            double pixels = Math.Max(1, direction.Length);
            Vector normal = direction.Length > 1 ? direction / direction.Length : axis == 0 ? new Vector(1, 0)
                : axis == 1 ? new Vector(-0.7, 0.7) : new Vector(0, -1);
            if (_tool == PhysCollmapViewportTool.Rotate && !balance)
            {
                Vector3 a = basis[(axis + 1) % 3], b = basis[(axis + 2) % 3];
                Vector av = Project(origin + a) - center, bv = Project(origin + b) - center;
                double max = Math.Max(av.Length, bv.Length);
                double radius = (52 + axis * 9) / Math.Max(1, max);
                var ring = new Point[65];
                for (int i = 0; i < ring.Length; i++)
                {
                    double angle = i * Math.PI * 2 / (ring.Length - 1);
                    ring[i] = center + av * (Math.Cos(angle) * radius) + bv * (Math.Sin(angle) * radius);
                }
                grips[axis] = new Grip(axis, ring, worldAxis);
            }
            else if (_tool == PhysCollmapViewportTool.Resize && !balance)
            {
                Vector3 size = shape!.Size;
                float half = axis == 0 ? size.X * 0.5f : axis == 1 ? size.Y * 0.5f : size.Z * 0.5f;
                Point face = Project(origin + worldAxis * half);
                grips[axis] = new Grip(axis, [face, face + normal * 18], worldAxis);
            }
            else
                grips[axis] = new Grip(axis, [center + normal * 12, center + normal * 70], worldAxis);
        }
        return grips;
    }

    private void DrawGizmo(DrawingContext context)
    {
        foreach (Grip grip in GetGrips())
        {
            var pen = new Pen(AxisBrushes[grip.Axis], grip.Axis == _hotAxis ? 3.5 : 2.5);
            if (_tool == PhysCollmapViewportTool.Rotate && !_editBalancePoint)
            {
                for (int i = 1; i < grip.Points.Length; i++)
                    context.DrawLine(pen, grip.Points[i - 1], grip.Points[i]);
            }
            else
            {
                context.DrawLine(pen, grip.Points[0], grip.Points[1]);
                Point tip = grip.Points[1];
                if (_tool == PhysCollmapViewportTool.Resize && !_editBalancePoint)
                    context.DrawRectangle(AxisBrushes[grip.Axis], new Pen(Stage, 2), new Rect(tip.X - 6, tip.Y - 6, 12, 12));
                else
                {
                    Vector direction = tip - grip.Points[0];
                    direction = direction.Length > 0 ? direction / direction.Length : new Vector(0, -1);
                    Vector side = new(-direction.Y, direction.X);
                    var arrow = new StreamGeometry();
                    using (StreamGeometryContext path = arrow.Open())
                    {
                        path.BeginFigure(tip + direction * 3, isFilled: true);
                        path.LineTo(tip - direction * 12 + side * 6);
                        path.LineTo(tip - direction * 12 - side * 6);
                        path.EndFigure(true);
                    }
                    context.DrawGeometry(AxisBrushes[grip.Axis], new Pen(Stage, 1.5), arrow);
                }
            }
            Point labelAt = _tool == PhysCollmapViewportTool.Rotate && !_editBalancePoint
                ? grip.Points[(8 + grip.Axis * 13) % 64] : grip.Points[^1];
            context.DrawText(AxisLabels[grip.Axis], labelAt + new Vector(8, -9));
        }
    }

    private int HitGrip(Point point)
    {
        if (!_canEdit || _layer == PhysCollmapViewportLayer.Model) return -1;
        if (!_editBalancePoint && (_selectedIndex < 0 || _selectedIndex >= _shapes.Count ||
            !_shapes[_selectedIndex].CanTransform ||
            _tool == PhysCollmapViewportTool.Rotate && !_shapes[_selectedIndex].CanRotate)) return -1;
        double best = 10;
        int hit = -1;
        foreach (Grip grip in GetGrips())
        for (int i = 1; i < grip.Points.Length; i++)
        {
            double distance = SegmentDistance(point, grip.Points[i - 1], grip.Points[i]);
            if (distance < best) { best = distance; hit = grip.Axis; }
        }
        return hit;
    }

    private int HitShape(Point point)
    {
        if (_layer == PhysCollmapViewportLayer.Model) return -1;
        double nearest = 7;
        int hit = -1;
        double containedArea = double.PositiveInfinity;
        int contained = -1;
        for (int i = 0; i < _shapes.Count; i++)
        {
            var (points, edges) = ScreenOutline(_shapes[i]);
            foreach (var (a, b) in edges)
            {
                double distance = SegmentDistance(point, points[a], points[b]);
                if (distance < nearest) { nearest = distance; hit = i; }
            }
            Point[] hull = ConvexHull(points);
            if (hull.Length >= 3 && Contains(hull, point))
            {
                double area = PolygonArea(hull);
                if (area < containedArea) { containedArea = area; contained = i; }
            }
        }
        return hit >= 0 ? hit : contained;
    }

    private static Point[] ConvexHull(Point[] points)
    {
        if (points.Length < 3) return points;
        Point[] sorted = [.. points.OrderBy(p => p.X).ThenBy(p => p.Y)];
        var lower = new List<Point>();
        var upper = new List<Point>();
        foreach (Point p in sorted)
        {
            while (lower.Count > 1 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        for (int i = sorted.Length - 1; i >= 0; i--)
        {
            Point p = sorted[i];
            while (upper.Count > 1 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
        return [.. lower, .. upper];
    }

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool Contains(Point[] polygon, Point point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Point a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double PolygonArea(Point[] polygon)
    {
        double twice = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Point next = polygon[(i + 1) % polygon.Length];
            twice += polygon[i].X * next.Y - next.X * polygon[i].Y;
        }
        return Math.Abs(twice) * 0.5;
    }

    private static double SegmentDistance(Point p, Point a, Point b)
    {
        Vector line = b - a;
        double denominator = line.X * line.X + line.Y * line.Y;
        double t = denominator > 0 ? Math.Clamp(((p.X - a.X) * line.X + (p.Y - a.Y) * line.Y) / denominator, 0, 1) : 0;
        double dx = p.X - a.X - line.X * t, dy = p.Y - a.Y - line.Y * t;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
