using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;
using Vector = Avalonia.Vector;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicDrawing
{
    private static readonly IBrush BackgroundBrush = Brush("#1B1D21");
    private static readonly IBrush MutedBrush = Brush("#91959E");
    private static readonly IBrush SelectionBrush = Brush("#F2B65B");
    private static readonly IBrush SelectionFill = Brush("#19F2B65B");
    private static readonly IBrush MissingModelFill = Brush("#FF0000");
    private static readonly IBrush VehiclePathBrush = Brush("#72C5B1");
    private static readonly IBrush VehicleIssueBrush = Brush("#EA7774");
    private static readonly IBrush VehicleLookaheadBrush = Brush("#B6A0EA");
    private static readonly Pen MinorGrid = new(Brush("#26292E"));
    private static readonly Pen MajorGrid = new(Brush("#33373E"));
    private static readonly Pen BrushPen = new(Brush("#9AA0AA"));
    private static readonly Pen ClipBrushPen = new(Brush("#D959D9"));
    private static readonly Pen TerrainPen = new(Brush("#84988B"));
    private static readonly Pen EntityPen = new(Brush("#A29AAE"), 1.5);
    private static readonly Pen MissingModelPen = new(Brush("#7A0000"), 1.5);
    private static readonly Pen TargetPen = new(Brush("#6DB8C4"), 1.2);
    private static readonly Pen VehiclePathPen = new(VehiclePathBrush, 2);
    private static readonly Pen VehicleCyclePen = new(Brush("#E7A85D"), 2);
    private static readonly Pen VehicleIssuePen = new(VehicleIssueBrush, 2);
    private static readonly Pen VehicleHeadingPen = new(VehicleLookaheadBrush, 1.8);
    private static readonly Pen VehicleLookaheadPen = new(VehicleLookaheadBrush, 1.5, DashStyle.Dash);
    private static readonly Pen SelectedPen = new(SelectionBrush, 1.8);
    private static readonly Pen LeakPathPen = new(Brush("#FF6666"), 2.5);
    private static readonly IBrush LeakPointBrush = Brush("#FFB5B5");
    private static readonly Pen XAxisPen = new(Brush("#BD6165"), 1.5);
    private static readonly Pen YAxisPen = new(Brush("#74AD82"), 1.5);
    private static readonly Pen ZAxisPen = new(Brush("#6B9AC8"), 1.5);

    private readonly OrthographicProjection _projection;

    internal OrthographicDrawing(OrthographicProjection projection) => _projection = projection;
    internal LeakPath? LeakPath { get; set; }
    internal int LeakPointIndex { get; set; }

    internal void Draw(DrawingContext context, EditorSession? session, OrthographicGestures gestures, bool focused)
    {
        context.DrawRectangle(BackgroundBrush, null, new Rect(_projection.Size));
        DrawGrid(context, session?.GridSize ?? 16);
        if (session is not null)
        {
            EditorScene scene = session.Scene;
            foreach (var brush in scene.Document.Brushes)
            {
                bool selected = IsWholeSelected(session, brush);
                IReadOnlyList<MapPolygon> polygons = brush.GetPolygons();
                if (selected)
                    DrawPolygon(context, OrthographicGeometry.ConvexHull(polygons.SelectMany(p => p.Vertices).Select(_projection.ToScreen)),
                        SelectionFill, null);
                foreach (var polygon in polygons)
                {
                    bool faceSelected = scene.Selection.Contains(new BrushFaceSelection(brush, polygon.Face));
                    DrawPolygon(context, polygon.Vertices.Select(_projection.ToScreen).ToArray(), faceSelected ? SelectionFill : null,
                        selected || faceSelected ? SelectedPen : ClipBrushMaterial.IsPlayerClip(polygon.Face.Material) ? ClipBrushPen : BrushPen);
                }
            }
            foreach (var terrain in scene.Document.Terrains)
                DrawTerrain(context, terrain.GetSurface(), IsWholeSelected(session, terrain), !terrain.IsCurve);
            foreach (var entity in scene.Document.Entities.Where(PointEntityGeometry.IsPointEntity))
            {
                Vector3 origin = EditorSession.EntityOrigin(entity);
                bool selected = scene.Selection.Contains(entity);
                bool missingModel = false;
                if (XModelGeometry.IsModel(entity))
                {
                    if (scene.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
                    {
                        Pen pen = selected ? SelectedPen : EntityPen;
                        foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                        {
                            Point a = _projection.ToScreen(triangle.A.Position), b = _projection.ToScreen(triangle.B.Position), c = _projection.ToScreen(triangle.C.Position);
                            context.DrawLine(pen, a, b); context.DrawLine(pen, b, c); context.DrawLine(pen, c, a);
                        }
                        if (selected) DrawText(context, model.Name, _projection.ToScreen(origin) + new Vector(7, 7), SelectionBrush);
                        continue;
                    }
                    missingModel = true;
                }
                if (scene.Bounds(entity) is not { } entityBounds) continue;
                Rect rect = _projection.ScreenBounds(entityBounds.Min, entityBounds.Max);
                if (!missingModel && entity.ClassName == "trigger_radius")
                    foreach (var line in PointEntityGeometry.GetRadiusLines(entity))
                        context.DrawLine(selected ? SelectedPen : EntityPen, _projection.ToScreen(line.A), _projection.ToScreen(line.B));
                else
                    context.DrawRectangle(missingModel ? MissingModelFill : selected ? SelectionFill : null,
                        selected ? SelectedPen : missingModel ? MissingModelPen : EntityPen, rect);
                Point center = _projection.ToScreen(origin);
                context.DrawLine(EntityPen, center - new Vector(4, 0), center + new Vector(4, 0));
                context.DrawLine(EntityPen, center - new Vector(0, 4), center + new Vector(0, 4));
                DrawText(context, entity.ClassName, new Point(rect.Right + 5, rect.Top - 2), MutedBrush);
                if (!VehiclePathPreview.IsNode(entity) &&
                    (entity.Properties.ContainsKey("angles") || entity.Properties.ContainsKey("angle")))
                    try
                    {
                        Point forward = _projection.ToScreen(origin + EntityOrientation.Forward(EntityOrientation.Read(entity)) * 32);
                        DrawArrow(context, center, forward, selected ? SelectedPen : EntityPen);
                    }
                    catch (ArgumentException) { }
            }
            MapEntity? selectedVehicle = scene.Selection.Items.OfType<MapEntity>().FirstOrDefault(VehiclePathPreview.IsNode);
            DrawTargets(context, scene, selectedVehicle is not null);
            if (selectedVehicle is not null) DrawVehiclePath(context, scene.Document, selectedVehicle);
            if (session.SelectionBounds is { } bounds)
                DrawSelection(context, session, bounds.Min, bounds.Max);
            if (session.Tool == EditorTool.Vertex) DrawVertices(context, session);
            if (gestures.MarqueeBounds is { } marquee)
                context.DrawRectangle(SelectionFill, new Pen(SelectionBrush, 1, DashStyle.Dash), marquee);
            if (gestures.HasClipPreview) DrawClip(context, session, gestures);
            if (gestures.IsCreating)
                DrawCreation(context, session, gestures);
            if (session.Tool == EditorTool.Sculpt && _projection.Plane == OrthoPlane.Top && gestures.PointerInside)
            {
                double radius = session.SculptRadius * _projection.Zoom;
                context.DrawEllipse(null, SelectedPen, gestures.CursorScreen, radius, radius);
                context.DrawLine(SelectedPen, gestures.CursorScreen - new Vector(4, 0), gestures.CursorScreen + new Vector(4, 0));
                context.DrawLine(SelectedPen, gestures.CursorScreen - new Vector(0, 4), gestures.CursorScreen + new Vector(0, 4));
            }
        }
        if (LeakPath is { } path)
        {
            for (int index = 1; index < path.Points.Count; index++)
                context.DrawLine(LeakPathPen, _projection.ToScreen(path.Points[index - 1]),
                    _projection.ToScreen(path.Points[index]));
            Point current = _projection.ToScreen(path.Points[LeakPointIndex]);
            context.DrawEllipse(LeakPointBrush, LeakPathPen, current, 5, 5);
        }
        if (focused)
            context.DrawRectangle(null, new Pen(Brush("#6F7787")), new Rect(_projection.Size).Deflate(0.5));
    }

    private void DrawGrid(DrawingContext context, float gridSize)
    {
        double step = Math.Max(0.25, gridSize);
        while (step * _projection.Zoom < 12) step *= 2;
        Vector2 minimum = _projection.ToWorld(new Point(0, _projection.Size.Height));
        Vector2 maximum = _projection.ToWorld(new Point(_projection.Size.Width, 0));
        double firstX = Math.Ceiling(minimum.X / step), lastX = Math.Floor(maximum.X / step);
        double firstY = Math.Ceiling(minimum.Y / step), lastY = Math.Floor(maximum.Y / step);
        for (double i = firstX; i <= lastX; i++)
        {
            double x = _projection.ToScreen(new Vector2((float)(i * step), 0)).X;
            bool major = i % 8 == 0;
            context.DrawLine(major ? MajorGrid : MinorGrid, new Point(x, 0), new Point(x, _projection.Size.Height));
            if (major) DrawText(context, (i * step).ToString("G", CultureInfo.InvariantCulture), new Point(x + 3, 4), MutedBrush, 10);
        }
        for (double i = firstY; i <= lastY; i++)
        {
            double y = _projection.ToScreen(new Vector2(0, (float)(i * step))).Y;
            bool major = i % 8 == 0;
            context.DrawLine(major ? MajorGrid : MinorGrid, new Point(0, y), new Point(_projection.Size.Width, y));
            if (major && y > 16) DrawText(context, (i * step).ToString("G", CultureInfo.InvariantCulture), new Point(4, y + 2), MutedBrush, 10);
        }
        Point origin = _projection.ToScreen(Vector2.Zero);
        context.DrawLine(HorizontalAxisPen, new Point(0, origin.Y), new Point(_projection.Size.Width, origin.Y));
        context.DrawLine(VerticalAxisPen, new Point(origin.X, 0), new Point(origin.X, _projection.Size.Height));
    }

    private void DrawTerrain(DrawingContext context, MapTerrain terrain, bool selected, bool showPoints)
    {
        Pen pen = selected ? SelectedPen : TerrainPen;
        for (int x = 0; x < terrain.Width; x++)
        for (int y = 0; y < terrain.Height; y++)
        {
            int index = x * terrain.Height + y;
            Point p = _projection.ToScreen(terrain.Vertices[index]);
            if (x + 1 < terrain.Width)
                context.DrawLine(pen, p, _projection.ToScreen(terrain.Vertices[index + terrain.Height]));
            if (y + 1 < terrain.Height)
                context.DrawLine(pen, p, _projection.ToScreen(terrain.Vertices[index + 1]));
            if (selected && showPoints)
                context.DrawRectangle(SelectionBrush, null, new Rect(p.X - 2, p.Y - 2, 4, 4));
        }
    }

    private void DrawSelection(DrawingContext context, EditorSession session, Vector3 min, Vector3 max)
    {
        Rect rect = _projection.ScreenBounds(min, max);
        context.DrawRectangle(null, new Pen(SelectionBrush, 1, DashStyle.Dash), rect);
        if (session.Tool is not (EditorTool.Select or EditorTool.Vertex) || !session.CanTransformSelection) return;
        if (session.Tool == EditorTool.Select && session.TransformMode == TransformMode.Move && session.Selection.Count == 1 && session.Selection.Active is MapBrush)
            foreach (Point point in OrthographicGeometry.Corners(rect))
                context.DrawRectangle(BackgroundBrush, SelectedPen, new Rect(point.X - 3.5, point.Y - 3.5, 7, 7));
        Point center = rect.Center;
        if (session.TransformMode == TransformMode.Rotate)
            context.DrawEllipse(null, SelectedPen, center, OrthographicTransform.RotationRadius, OrthographicTransform.RotationRadius);
        else
        {
            Point right = center + new Vector(OrthographicTransform.AxisLength, 0);
            Point up = center - new Vector(0, OrthographicTransform.AxisLength);
            context.DrawLine(HorizontalAxisPen, center, right);
            context.DrawLine(VerticalAxisPen, center, up);
            if (session.TransformMode == TransformMode.Scale)
            {
                context.DrawRectangle(HorizontalAxisPen.Brush, null, new Rect(right.X - 4, right.Y - 4, 8, 8));
                context.DrawRectangle(VerticalAxisPen.Brush, null, new Rect(up.X - 4, up.Y - 4, 8, 8));
            }
            else
            {
                DrawPolygon(context, [right, right + new Vector(-8, -4), right + new Vector(-8, 4)], HorizontalAxisPen.Brush, null);
                DrawPolygon(context, [up, up + new Vector(-4, 8), up + new Vector(4, 8)], VerticalAxisPen.Brush, null);
            }
            context.DrawRectangle(SelectionBrush, null, new Rect(center.X - 3, center.Y - 3, 6, 6));
        }
        Vector2 size = _projection.Project(max - min);
        DrawText(context, FormattableString.Invariant($"{size.X:G5} × {size.Y:G5}"),
            new Point(rect.Left, rect.Bottom + 7), SelectionBrush);
    }

    private void DrawVertices(DrawingContext context, EditorSession session)
    {
        foreach (OrthographicSelection.VertexHandle handle in OrthographicSelection.GetVertexHandles(session, _projection))
        {
            Point point = _projection.ToScreen(handle.Position);
            IBrush fill = handle.Vertices.All(session.Selection.Contains) ? SelectionBrush : BackgroundBrush;
            if (handle.IsEdge) context.DrawEllipse(fill, SelectedPen, point, 3, 3);
            else context.DrawRectangle(fill, SelectedPen, new Rect(point.X - 3.5, point.Y - 3.5, 7, 7));
        }
    }

    private void DrawClip(DrawingContext context, EditorSession session, OrthographicGestures gestures)
    {
        Point start = _projection.ToScreen(gestures.ClipStart), end = _projection.ToScreen(gestures.ClipEnd);
        context.DrawLine(SelectedPen, start, end);
        context.DrawEllipse(SelectionBrush, null, start, 3, 3);
        context.DrawEllipse(SelectionBrush, null, end, 3, 3);
        Vector2 edge = gestures.ClipEnd - gestures.ClipStart;
        if (edge.LengthSquared() > 0.0001f)
        {
            Vector2 left = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            Point center = _projection.ToScreen((gestures.ClipStart + gestures.ClipEnd) / 2);
            Vector direction = new(left.X * 18, -left.Y * 18);
            if (session.ClipMode != ClipMode.KeepBack) context.DrawLine(SelectedPen, center, center + direction);
            if (session.ClipMode != ClipMode.KeepFront) context.DrawLine(SelectedPen, center, center - direction);
            DrawText(context, "Preview · Enter: apply · Esc: cancel", end + new Vector(8, 8), SelectionBrush);
        }
    }

    private static bool IsWholeSelected(EditorSession session, object item) => session.Scene.Selection.Contains(item) ||
        session.Scene.Selection.Items.OfType<MapEntity>().Any(entity => item is MapBrush brush && entity.Brushes.Contains(brush) ||
            item is MapTerrain terrain && entity.Terrains.Contains(terrain));

    private void DrawTargets(DrawingContext context, EditorScene scene, bool vehiclePreview)
    {
        foreach (MapEntity source in scene.Document.Entities)
        {
            if (vehiclePreview && VehiclePathPreview.IsNode(source)) continue;
            foreach (MapEntity target in scene.ResolveTargets(source))
                DrawArrow(context, _projection.ToScreen(EditorSession.EntityOrigin(source)),
                    _projection.ToScreen(EditorSession.EntityOrigin(target)), TargetPen);
        }
    }

    private void DrawVehiclePath(DrawingContext context, MapDocument document, MapEntity selectedEntity)
    {
        var preview = new VehiclePathPreview(document);
        if (preview.Find(selectedEntity) is not { } selected) return;
        HashSet<VehiclePathPreview.Node> connected = preview.ConnectedTo(selected);
        foreach (VehiclePathPreview.Node node in connected)
        {
            if (!node.HasPosition) continue;
            Point position = _projection.ToScreen(node.Position);
            if (node.Next is { HasPosition: true } next && connected.Contains(next))
                DrawArrow(context, position, _projection.ToScreen(next.Position),
                    node.Cycle && next.Cycle ? VehicleCyclePen : VehiclePathPen);
            if (node.Cycle)
                context.DrawEllipse(null, VehicleCyclePen, position, 8, 8);
            if (node.Warnings.Count > 0)
            {
                context.DrawEllipse(null, VehicleIssuePen, position, 11, 11);
                DrawText(context, "!", position + new Vector(9, -18), VehicleIssueBrush);
            }
        }

        if (!selected.HasPosition) return;
        Point origin = _projection.ToScreen(selected.Position);
        if (selected.Heading is { } heading)
            DrawArrow(context, origin, _projection.ToScreen(selected.Position + heading * 40), VehicleHeadingPen);
        IReadOnlyList<(Vector3 Start, Vector3 End)> lookahead = preview.LookaheadSegments(selected);
        foreach (var segment in lookahead)
            context.DrawLine(VehicleLookaheadPen, _projection.ToScreen(segment.Start), _projection.ToScreen(segment.End));
        if (lookahead.Count > 0)
        {
            Point end = _projection.ToScreen(lookahead[^1].End);
            context.DrawEllipse(null, VehicleLookaheadPen, end, 5, 5);
            DrawText(context, "lookahead", end + new Vector(7, 4), VehicleLookaheadBrush);
        }
        if (selected.SpeedMph is { } speed)
            DrawText(context, FormattableString.Invariant($"{speed:G4} mph"), origin + new Vector(13, -5), VehiclePathBrush);
    }

    private static void DrawArrow(DrawingContext context, Point start, Point end, Pen pen)
    {
        Vector delta = end - start;
        if (delta.Length < 0.001) return;
        context.DrawLine(pen, start, end);
        Vector direction = delta / delta.Length;
        Vector side = new(-direction.Y, direction.X);
        Point back = end - direction * Math.Min(8, delta.Length * 0.3);
        context.DrawLine(pen, end, back + side * 3);
        context.DrawLine(pen, end, back - side * 3);
    }

    private void DrawCreation(DrawingContext context, EditorSession session, OrthographicGestures gestures)
    {
        Rect rect = OrthographicGeometry.Rectangle(_projection.ToScreen(gestures.StartWorld), _projection.ToScreen(gestures.CurrentWorld));
        context.DrawRectangle(SelectionFill, SelectedPen, rect);
        if (gestures.IsCreatingTerrain)
        {
            int count = Math.Clamp(session.TerrainVertices, 2, 16);
            for (int i = 1; i < count - 1; i++)
            {
                double x = rect.Left + rect.Width * i / (count - 1);
                double y = rect.Top + rect.Height * i / (count - 1);
                context.DrawLine(TerrainPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
                context.DrawLine(TerrainPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
        Vector2 size = Vector2.Abs(gestures.CurrentWorld - gestures.StartWorld);
        DrawText(context, FormattableString.Invariant($"{size.X:G5} × {size.Y:G5}"),
            new Point(rect.Left, rect.Bottom + 7), SelectionBrush);
    }

    private Pen HorizontalAxisPen => _projection.Plane == OrthoPlane.Side ? YAxisPen : XAxisPen;
    private Pen VerticalAxisPen => _projection.Plane == OrthoPlane.Top ? YAxisPen : ZAxisPen;
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static void DrawText(DrawingContext context, string text, Point position, IBrush brush, double size = 11) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush), position);

    private static void DrawPolygon(DrawingContext context, Point[] points, IBrush? fill, Pen? pen)
    {
        if (points.Length < 2) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], fill is not null);
            for (int i = 1; i < points.Length; i++) path.LineTo(points[i]);
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }
}
