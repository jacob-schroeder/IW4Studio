using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class MapBrush
{
    private List<MapPolygon>? _polygons;
    public List<MapFace> Faces { get; } = [];
    public List<string> Directives { get; } = [];

    public IReadOnlyList<MapPolygon> GetPolygons()
    {
        if (_polygons is not null)
            return _polygons;
        _polygons = [];
        var normals = Faces.Select(face => face.Normal).ToArray();
        var distances = Faces.Select((face, index) => Vector3.Dot(normals[index], face.A)).ToArray();
        var points = Faces.Select(_ => new List<Vector3>()).ToArray();
        for (int a = 0; a < Faces.Count; a++)
        for (int b = a + 1; b < Faces.Count; b++)
        for (int c = b + 1; c < Faces.Count; c++)
        {
            Vector3 cross = Vector3.Cross(normals[b], normals[c]);
            float denominator = Vector3.Dot(normals[a], cross);
            if (MathF.Abs(denominator) < 0.00001f)
                continue;
            Vector3 point = (distances[a] * cross + distances[b] * Vector3.Cross(normals[c], normals[a]) +
                             distances[c] * Vector3.Cross(normals[a], normals[b])) / denominator;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                continue;
            bool inside = true;
            for (int face = 0; face < Faces.Count; face++)
                if (Vector3.Dot(normals[face], point) > distances[face] + 0.02f)
                {
                    inside = false;
                    break;
                }
            if (!inside)
                continue;
            foreach (int face in new[] { a, b, c })
                if (!points[face].Any(existing => Vector3.DistanceSquared(existing, point) < 0.0001f))
                    points[face].Add(point);
        }
        for (int face = 0; face < Faces.Count; face++)
        {
            if (points[face].Count < 3)
                continue;
            Vector3 center = points[face].Aggregate(Vector3.Zero, (sum, point) => sum + point) / points[face].Count;
            Vector3 u = Vector3.Normalize(points[face][0] - center);
            Vector3 v = Vector3.Cross(normals[face], u);
            _polygons.Add(new MapPolygon(Faces[face], points[face].OrderBy(point =>
                MathF.Atan2(Vector3.Dot(point - center, v), Vector3.Dot(point - center, u))).ToArray()));
        }
        return _polygons;
    }

    public (Vector3 Min, Vector3 Max) GetBounds()
    {
        Vector3[] vertices = GetPolygons().SelectMany(polygon => polygon.Vertices).ToArray();
        return vertices.Length == 0 ? (Vector3.Zero, Vector3.Zero) :
            (vertices.Aggregate(Vector3.Min), vertices.Aggregate(Vector3.Max));
    }

    public void Translate(Vector3 offset)
    {
        foreach (var face in Faces)
        {
            face.A += offset;
            face.B += offset;
            face.C += offset;
        }
        _polygons = null;
    }

    public void Resize(Vector3 minimum, Vector3 maximum)
    {
        var bounds = GetBounds();
        Vector3 size = bounds.Max - bounds.Min;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            return;
        Vector3 scale = (maximum - minimum) / size;
        foreach (var face in Faces)
        {
            face.A = (face.A - bounds.Min) * scale + minimum;
            face.B = (face.B - bounds.Min) * scale + minimum;
            face.C = (face.C - bounds.Min) * scale + minimum;
        }
        _polygons = null;
    }

    public MapBrush Clone()
    {
        var copy = new MapBrush();
        copy.Faces.AddRange(Faces.Select(face => face.Clone()));
        copy.Directives.AddRange(Directives);
        return copy;
    }

    public static MapBrush CreateBox(Vector3 a, Vector3 b, string material)
    {
        var brush = new MapBrush();
        void Add(Vector3 p0, Vector3 p1, Vector3 p2) => brush.Faces.Add(new MapFace
            { A = p0, B = p1, C = p2, Material = material });
        Add(new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z), new(a.X, a.Y, a.Z));
        Add(new(a.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z));
        Add(new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, a.Y, a.Z));
        Add(new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z));
        Add(new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z), new(a.X, b.Y, a.Z));
        Add(new(a.X, b.Y, b.Z), new(a.X, a.Y, b.Z), new(a.X, a.Y, a.Z));
        return brush;
    }
}
