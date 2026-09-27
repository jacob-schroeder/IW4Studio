using System.Numerics;
using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

internal static class BrushGlass
{
    private const double NormalTolerance = 1e-4;

    internal static (MapPolygon Face, float Thickness) ReadPane(MapBrush brush)
    {
        try { BrushGeometry.Validate(brush); }
        catch (ArgumentException exception)
        { throw new InvalidDataException("A glass pane must be a valid closed convex brush.", exception); }

        var polygons = brush.GetPolygons();
        if (polygons.Count != 6 || polygons.Any(polygon => polygon.Vertices.Length != 4) ||
            brush.GetVertices().Count != 8)
            throw new NotSupportedException("Breakable glass currently supports six-sided rectangular pane brushes.");
        if (brush.Faces.Select(face => face.Material).Distinct(StringComparer.Ordinal).Count() != 1 ||
            string.IsNullOrWhiteSpace(brush.Faces[0].Material))
            throw new NotSupportedException("All faces of a glass pane must use the same material.");

        var dimensions = new (MapPolygon Polygon, float EdgeA, float EdgeB, double Area)[polygons.Count];
        for (int index = 0; index < polygons.Count; index++)
        {
            MapPolygon polygon = polygons[index];
            Vector3[] points = polygon.Vertices;
            Vector3 a = points[1] - points[0], b = points[2] - points[1];
            Vector3 c = points[3] - points[2], d = points[0] - points[3];
            float edgeA = a.Length(), edgeB = b.Length();
            if (edgeA <= BrushGeometry.PointTolerance || edgeB <= BrushGeometry.PointTolerance ||
                Vector3.Distance(a, -c) > BrushGeometry.PlaneTolerance ||
                Vector3.Distance(b, -d) > BrushGeometry.PlaneTolerance ||
                Math.Abs(BrushGeometry.Dot(a, b)) / edgeA > BrushGeometry.PlaneTolerance ||
                Math.Abs(BrushGeometry.Dot(a, b)) / edgeB > BrushGeometry.PlaneTolerance)
                throw new NotSupportedException("Every glass pane face must be rectangular.");
            dimensions[index] = (polygon, edgeA, edgeB, (double)edgeA * edgeB);
        }

        foreach (MapPolygon polygon in polygons)
        {
            double[] alignment = polygons.Where(other => other != polygon)
                .Select(other => BrushGeometry.Dot(polygon.Face.Normal, other.Face.Normal)).ToArray();
            if (alignment.Count(dot => dot < -1 + NormalTolerance) != 1 ||
                alignment.Count(dot => Math.Abs(dot) <= NormalTolerance) != 4)
                throw new NotSupportedException("Glass pane faces must form three perpendicular pairs of opposing parallel faces.");
        }

        MapPolygon face = dimensions.MaxBy(item => item.Area).Polygon;
        Vector3 normal = face.Face.Normal;
        MapPolygon opposite = polygons.Single(polygon => polygon != face &&
            BrushGeometry.Dot(normal, polygon.Face.Normal) < -1 + NormalTolerance);

        float thickness = (float)BrushGeometry.Dot(normal, face.Vertices[0] - opposite.Vertices[0]);
        var chosen = dimensions.First(item => item.Polygon == face);
        if (thickness <= BrushGeometry.PointTolerance || thickness >= chosen.EdgeA || thickness >= chosen.EdgeB)
            throw new NotSupportedException("A glass pane must have one unique thin axis, smaller than both face edges.");
        return (face, thickness);
    }

    internal static (string ShatteredMaterial, string PhysPreset)? Read(MapBrush brush)
    {
        (string ShatteredMaterial, string PhysPreset)? result = null;
        foreach (string directive in brush.Directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count == 0 || tokens[0].Quoted || tokens[0].Value != "glass") continue;
            if (result is not null)
                throw new InvalidDataException("A brush can have only one glass directive.");
            if (tokens.Count != 4 || !tokens[1].Quoted || !tokens[2].Quoted ||
                tokens[3].Quoted || tokens[3].Value != ";")
                throw new InvalidDataException("A glass directive needs a quoted shattered material and physics preset.");
            ValidateNames(tokens[1].Value, tokens[2].Value);
            result = (tokens[1].Value, tokens[2].Value);
        }
        return result;
    }

    internal static void Set(MapBrush brush, string shatteredMaterial, string physPreset)
    {
        ValidateNames(shatteredMaterial, physPreset);
        Clear(brush);
        brush.Directives.Add($"glass {MapWriter.Quote(shatteredMaterial)} {MapWriter.Quote(physPreset)};");
    }

    internal static void Clear(MapBrush brush)
    {
        for (int index = brush.Directives.Count - 1; index >= 0; index--)
        {
            var tokens = MapTokenizer.Tokenize(brush.Directives[index]);
            if (tokens.Count > 0 && !tokens[0].Quoted && tokens[0].Value == "glass")
                brush.Directives.RemoveAt(index);
        }
    }

    internal static bool IsGlass(MapBrush brush) => BrushContents.Read(brush) == BrushKind.BreakableGlass;

    internal static void ValidateNames(string shatteredMaterial, string physPreset)
    {
        ValidateName(shatteredMaterial, "Shattered material");
        ValidateName(physPreset, "Shard physics preset");
    }

    private static void ValidateName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
            throw new FormatException($"{label} must have a name without line breaks or null characters.");
    }
}
