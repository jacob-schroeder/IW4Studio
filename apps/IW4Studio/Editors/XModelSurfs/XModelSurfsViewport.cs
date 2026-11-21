using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vector = Avalonia.Vector;

namespace IW4.Studio.Desktop.Editors.XModelSurfs;

public enum XModelSurfsViewportTool { Move, Size }
public enum XModelSurfsViewportPhase { Begin, Update, Commit, Cancel }

public sealed record XModelSurfsViewportSurface(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<ushort> TriangleIndices,
    Vector3 OriginalCenter,
    Vector3 OriginalSize,
    Vector3 Position,
    Vector3 Size,
    bool CanEdit);

public readonly record struct XModelSurfsViewportEdit(
    int Index, Vector3 Position, Vector3 Size, XModelSurfsViewportPhase Phase);

public sealed partial class XModelSurfsViewport : Control
{
    private static readonly IBrush Stage = new SolidColorBrush(Color.FromRgb(39, 47, 46));
    private static readonly Pen Grid = new(new SolidColorBrush(Color.FromArgb(35, 176, 195, 183)), 1);
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
    private IReadOnlyList<XModelSurfsViewportSurface> _surfaces = [];
    private int _selectedIndex = -1;
    private XModelSurfsViewportTool _tool;
    private bool _wireframe;
    private bool _canEdit = true;
    private bool _fitted;
    private Vector3 _viewCenter;
    private double _unitsPerPixel = 1;
    private double _orbitDegrees = 349;
    private int _hotAxis = -1;
    private Drag? _drag;

    public XModelSurfsViewport()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public event Action<int>? SelectionRequested;
    public event Action<XModelSurfsViewportEdit>? EditRequested;

    public XModelSurfsViewportTool Tool
    {
        get => _tool;
        set { _tool = value; InvalidateVisual(); }
    }

    public bool Wireframe
    {
        get => _wireframe;
        set { if (_wireframe == value) return; _wireframe = value; _redrawMesh = true; InvalidateVisual(); }
    }

    public bool CanEdit
    {
        get => _canEdit;
        set { _canEdit = value; InvalidateVisual(); }
    }

    public double OrbitDegrees
    {
        get => _orbitDegrees;
        set { _orbitDegrees = value % 360; _redrawMesh = true; InvalidateVisual(); }
    }

    public void RotateView(double deltaDegrees) => OrbitDegrees += deltaDegrees;

    public void SetScene(IReadOnlyList<XModelSurfsViewportSurface> surfaces, int selectedIndex)
    {
        if (ReferenceEquals(_surfaces, surfaces) && _selectedIndex == selectedIndex) return;
        bool replaced = _surfaces.Count != surfaces.Count;
        if (!replaced)
            for (int i = 0; i < surfaces.Count; i++)
                if (_surfaces[i].Positions.Count != surfaces[i].Positions.Count ||
                    _surfaces[i].TriangleIndices.Count != surfaces[i].TriangleIndices.Count ||
                    _surfaces[i].OriginalCenter != surfaces[i].OriginalCenter ||
                    _surfaces[i].OriginalSize != surfaces[i].OriginalSize)
                { replaced = true; break; }
        _surfaces = surfaces;
        _selectedIndex = selectedIndex;
        _redrawMesh = true;
        if (replaced && _drag is null) _fitted = false;
        InvalidateVisual();
    }

    public void FitView()
    {
        _fitted = false;
        _redrawMesh = true;
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
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.DrawRectangle(Stage, null, new Rect(Bounds.Size));
        if (!_fitted) FitScene();
        DrawGrid(context);
        DrawMesh(context);
        if (_canEdit && _selectedIndex >= 0 && _selectedIndex < _surfaces.Count &&
            _surfaces[_selectedIndex].CanEdit)
            DrawGizmo(context);
    }

    private Point Project(Vector3 point)
    {
        double yaw = _orbitDegrees * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        Vector3 p = point - _viewCenter;
        double rx = p.X * cy - p.Y * sy;
        double ry = p.X * sy + p.Y * cy;
        return new Point(Bounds.Width * .5 + (rx - ry) * .866 / _unitsPerPixel,
            Bounds.Height * .52 + ((rx + ry) * .5 - p.Z * .92) / _unitsPerPixel);
    }

    private void FitScene()
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < _surfaces.Count; i++)
        {
            XModelSurfsViewportSurface surface = _surfaces[i];
            foreach (Vector3 point in surface.Positions)
            {
                Vector3 world = Transform(point, surface, i);
                if (!Finite(world)) continue;
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
        }
        if (!Finite(min)) { min = new Vector3(-1); max = new Vector3(1); }
        _viewCenter = (min + max) * .5f;
        double spanX = 0, spanY = 0;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
            Point projected = ProjectUnscaled(corner);
            spanX = Math.Max(spanX, Math.Abs(projected.X));
            spanY = Math.Max(spanY, Math.Abs(projected.Y));
        }
        _unitsPerPixel = Math.Max(.001, Math.Max(2 * spanX / Math.Max(1, Bounds.Width * .82),
            2 * spanY / Math.Max(1, Bounds.Height * .76)));
        _fitted = true;
        _redrawMesh = true;
    }

    private Point ProjectUnscaled(Vector3 point)
    {
        double yaw = _orbitDegrees * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        Vector3 p = point - _viewCenter;
        double rx = p.X * cy - p.Y * sy, ry = p.X * sy + p.Y * cy;
        return new Point((rx - ry) * .866, (rx + ry) * .5 - p.Z * .92);
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

    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    private Vector3 Transform(Vector3 point, XModelSurfsViewportSurface surface, int index)
    {
        Vector3 size = _drag is { Axis: >= 0 } drag && drag.Index == index ? drag.Size : surface.Size;
        Vector3 position = _drag is { Axis: >= 0 } active && active.Index == index ? active.Position : surface.Position;
        Vector3 delta = point - surface.OriginalCenter;
        return position + new Vector3(
            surface.OriginalSize.X > 0 ? delta.X * size.X / surface.OriginalSize.X : delta.X,
            surface.OriginalSize.Y > 0 ? delta.Y * size.Y / surface.OriginalSize.Y : delta.Y,
            surface.OriginalSize.Z > 0 ? delta.Z * size.Z / surface.OriginalSize.Z : delta.Z);
    }

    private readonly record struct Grip(int Axis, Point Start, Point End);

    private Grip[] GetGrips()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _surfaces.Count) return [];
        XModelSurfsViewportSurface surface = _surfaces[_selectedIndex];
        Vector3 origin = _drag is { Axis: >= 0 } drag && drag.Index == _selectedIndex ? drag.Position : surface.Position;
        var grips = new List<Grip>(3);
        for (int axis = 0; axis < 3; axis++)
        {
            if (_tool == XModelSurfsViewportTool.Size && Component(surface.OriginalSize, axis) <= 0) continue;
            Vector3 worldAxis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            Point center = Project(origin);
            Vector projected = Project(origin + worldAxis) - center;
            if (projected.Length < .001) continue;
            Vector direction = projected / projected.Length;
            grips.Add(new Grip(axis, center + direction * 12, center + direction * 68));
        }
        return [.. grips];
    }

    private void DrawGizmo(DrawingContext context)
    {
        foreach (Grip grip in GetGrips())
        {
            IBrush brush = AxisBrushes[grip.Axis];
            var pen = new Pen(brush, grip.Axis == _hotAxis ? 3.5 : 2.5);
            context.DrawLine(pen, grip.Start, grip.End);
            if (_tool == XModelSurfsViewportTool.Size)
                context.DrawRectangle(brush, new Pen(Stage, 1.5), new Rect(grip.End.X - 6, grip.End.Y - 6, 12, 12));
            else
            {
                Vector direction = grip.End - grip.Start;
                direction /= direction.Length;
                Vector side = new(-direction.Y, direction.X);
                var arrow = new StreamGeometry();
                using (StreamGeometryContext path = arrow.Open())
                {
                    path.BeginFigure(grip.End + direction * 3, true);
                    path.LineTo(grip.End - direction * 12 + side * 6);
                    path.LineTo(grip.End - direction * 12 - side * 6);
                    path.EndFigure(true);
                }
                context.DrawGeometry(brush, new Pen(Stage, 1.5), arrow);
            }
            context.DrawText(AxisLabels[grip.Axis], grip.End + new Vector(8, -9));
        }
    }

    private int HitGrip(Point point)
    {
        if (!_canEdit || _selectedIndex < 0 || _selectedIndex >= _surfaces.Count || !_surfaces[_selectedIndex].CanEdit)
            return -1;
        double best = 10;
        int hit = -1;
        foreach (Grip grip in GetGrips())
        {
            Vector line = grip.End - grip.Start;
            double length2 = line.X * line.X + line.Y * line.Y;
            double t = Math.Clamp(((point.X - grip.Start.X) * line.X + (point.Y - grip.Start.Y) * line.Y) / length2, 0, 1);
            double dx = point.X - grip.Start.X - t * line.X, dy = point.Y - grip.Start.Y - t * line.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < best) { best = distance; hit = grip.Axis; }
        }
        return hit;
    }

    private static float Component(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    private static Vector3 SetComponent(Vector3 value, int axis, float component) => axis == 0 ? value with { X = component }
        : axis == 1 ? value with { Y = component } : value with { Z = component };
}
