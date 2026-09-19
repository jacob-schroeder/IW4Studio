using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class MapBrush
{
    private List<MapPolygon>? _polygons;
    public List<MapFace> Faces { get; } = [];
    public List<string> Directives { get; } = [];

    public IReadOnlyList<MapPolygon> GetPolygons() => _polygons ??= BrushGeometry.BuildPolygons(Faces);

    public IReadOnlyList<Vector3> GetVertices() => BrushGeometry.Vertices(GetPolygons());

    public (Vector3 Min, Vector3 Max) GetBounds()
    {
        Vector3[] vertices = GetPolygons().SelectMany(polygon => polygon.Vertices).ToArray();
        return vertices.Length == 0 ? (Vector3.Zero, Vector3.Zero) :
            (vertices.Aggregate(Vector3.Min), vertices.Aggregate(Vector3.Max));
    }

    public void Transform(Matrix4x4 matrix, bool textureLock)
    {
        if (matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 ||
            !Matrix4x4.Invert(matrix, out _) || !float.IsFinite(matrix.GetDeterminant()))
            throw new ArgumentException("Brush transforms must be finite, invertible affine transforms.");
        BrushGeometry.Validate(this);
        var candidate = Clone();
        bool mirrored = matrix.GetDeterminant() < 0;
        for (int index = 0; index < candidate.Faces.Count; index++)
        {
            var face = candidate.Faces[index];
            face.A = Vector3.Transform(face.A, matrix);
            face.B = Vector3.Transform(face.B, matrix);
            face.C = Vector3.Transform(face.C, matrix);
            if (mirrored) (face.B, face.C) = (face.C, face.B);
            if (textureLock)
                face.Projection = SurfaceProjection.Parse(face.Projection).Transform(Faces[index], face, matrix).Format();
        }
        BrushGeometry.Validate(candidate);
        // Keep face handles stable for ordinary transforms and publish only after every
        // face, projection and the resulting closed volume have passed validation.
        for (int index = 0; index < Faces.Count; index++)
        {
            Faces[index].A = candidate.Faces[index].A;
            Faces[index].B = candidate.Faces[index].B;
            Faces[index].C = candidate.Faces[index].C;
            Faces[index].Projection = candidate.Faces[index].Projection;
        }
        _polygons = null;
    }

    public void Translate(Vector3 offset, bool textureLock = false) =>
        Transform(Matrix4x4.CreateTranslation(offset), textureLock);

    public void Resize(Vector3 minimum, Vector3 maximum, bool textureLock = false)
    {
        var bounds = GetBounds();
        Vector3 size = bounds.Max - bounds.Min;
        Vector3 targetSize = maximum - minimum;
        if (!BrushGeometry.IsFinite(minimum) || !BrushGeometry.IsFinite(maximum) ||
            size.X <= 0 || size.Y <= 0 || size.Z <= 0 || targetSize.X <= 0 || targetSize.Y <= 0 || targetSize.Z <= 0)
            throw new ArgumentException("Brush bounds must have positive dimensions.");
        Vector3 scale = (maximum - minimum) / size;
        Transform(Matrix4x4.CreateTranslation(-bounds.Min) * Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateTranslation(minimum), textureLock);
    }

    internal void ReplaceFaces(MapBrush validated)
    {
        BrushGeometry.Validate(validated);
        Faces.Clear();
        Faces.AddRange(validated.Faces);
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
