using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public sealed class PrefabPreview : Control
{
    private (Vector2 A, Vector2 B)[] _lines = [];
    private Vector2 _minimum, _maximum;

    internal void Show(MapDocument? document)
    {
        var lines = new List<(Vector2 A, Vector2 B)>();
        if (document is not null)
        {
            foreach (MapBrush brush in document.Brushes)
            foreach (MapPolygon polygon in brush.GetPolygons())
            for (int index = 0; index < polygon.Vertices.Length; index++)
                Add(polygon.Vertices[index], polygon.Vertices[(index + 1) % polygon.Vertices.Length]);
            foreach (MapTerrain terrain in document.Terrains)
            foreach (var (a, b, c) in terrain.GetTriangles())
            { Add(terrain.Vertices[a], terrain.Vertices[b]); Add(terrain.Vertices[b], terrain.Vertices[c]); Add(terrain.Vertices[c], terrain.Vertices[a]); }
            foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName != "worldspawn" && entity.Brushes.Count == 0 && entity.Terrains.Count == 0))
            {
                Vector3 origin = EditorSession.EntityOrigin(entity);
                Add(origin - Vector3.UnitX * 8, origin + Vector3.UnitX * 8);
                Add(origin - Vector3.UnitY * 8, origin + Vector3.UnitY * 8);
                Add(origin - Vector3.UnitZ * 8, origin + Vector3.UnitZ * 8);
            }
        }
        _lines = lines.ToArray();
        var points = _lines.SelectMany(line => new[] { line.A, line.B }).ToArray();
        _minimum = points.Length == 0 ? Vector2.Zero : points.Aggregate(Vector2.Min);
        _maximum = points.Length == 0 ? Vector2.One : points.Aggregate(Vector2.Max);
        InvalidateVisual();
        void Add(Vector3 a, Vector3 b) => lines.Add((Project(a), Project(b)));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#1B1D21")), null, new Rect(Bounds.Size));
        Vector2 dimensions = _maximum - _minimum;
        double scale = Math.Min(Math.Max(1, Bounds.Width - 20) / Math.Max(1, dimensions.X),
            Math.Max(1, Bounds.Height - 20) / Math.Max(1, dimensions.Y));
        var pen = new Pen(new SolidColorBrush(Color.Parse("#A0B8C9")), 1);
        foreach (var line in _lines) context.DrawLine(pen, Screen(line.A), Screen(line.B));
        Point Screen(Vector2 value) => new(Bounds.Width / 2 + (value.X - (_minimum.X + _maximum.X) / 2) * scale,
            Bounds.Height / 2 + (value.Y - (_minimum.Y + _maximum.Y) / 2) * scale);
    }

    private static Vector2 Project(Vector3 position) => new((position.X - position.Y) * 0.70710678f,
        (position.X + position.Y) * 0.35f - position.Z);
}
