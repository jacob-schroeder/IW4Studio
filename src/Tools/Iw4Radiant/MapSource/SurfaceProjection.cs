using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

internal readonly record struct SurfaceProjection(
    float Width, float Height, float ShiftX, float ShiftY, float Rotation, float Skew, string Suffix)
{
    internal static SurfaceProjection Parse(string source)
    {
        var tokens = MapTokenizer.Tokenize(source);
        if (tokens.Count < 6)
            throw new FormatException("A surface projection requires six numbers.");
        Span<float> values = stackalloc float[6];
        for (int index = 0; index < values.Length; index++)
            if (tokens[index].Quoted || !float.TryParse(tokens[index].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[index]) || !float.IsFinite(values[index]))
                throw new FormatException("Surface projection values must be finite numbers.");
        return new(values[0], values[1], values[2], values[3], values[4], values[5], source[tokens[5].End..]);
    }

    internal string Format()
    {
        Validate();
        return FormattableString.Invariant($"{Width:R} {Height:R} {ShiftX:R} {ShiftY:R} {Rotation:R} {Skew:R}") + Suffix;
    }

    internal (Vector3 U, Vector3 V, Vector2 Offset) GetMapping(Vector3 normal)
    {
        Validate();
        var (s, t) = Axes(normal);
        float width = Width == 0 ? 128 : Width, height = Height == 0 ? 128 : Height;
        float angle = Rotation % 360;
        if (angle < 0) angle += 360;
        var (sin, cos) = angle switch
        {
            0 => (0f, 1f), 90 => (1f, 0f), 180 => (0f, -1f), 270 => (-1f, 0f),
            _ => MathF.SinCos(angle * (MathF.PI / 180))
        };
        // Radiant texturevecs.cpp, Face_MoveTexture: size is world units per repeat;
        // zero size means 128. The crossterm adds T to S, and shifts are -shift/size.
        Vector3 v = Axis(s) * (sin / height) - Axis(t) * (cos / height);
        Vector3 u = Axis(s) * (cos / width) + Axis(t) * (sin / width) + Skew * v;
        Vector2 offset = new(-ShiftX / width, -ShiftY / height);
        if (!BrushGeometry.IsFinite(u) || !BrushGeometry.IsFinite(v) ||
            !float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            throw new ArgumentException("The surface projection exceeds the supported numeric range.");
        return (u, v, offset);
    }

    internal SurfaceProjection Fit(MapPolygon polygon, float repeatsX = 1, float repeatsY = 1)
    {
        if (!float.IsFinite(repeatsX) || !float.IsFinite(repeatsY) || repeatsX <= 0 || repeatsY <= 0 ||
            polygon.Vertices.Length < 3)
            throw new ArgumentException("Texture fit requires a face and positive repeat counts.");
        var mapping = GetMapping(polygon.Face.Normal);
        Vector2[] coordinates = polygon.Vertices.Select(point => Coordinates(mapping, point)).ToArray();
        Vector2 minimum = coordinates.Aggregate(Vector2.Min), maximum = coordinates.Aggregate(Vector2.Max);
        Vector2 extent = maximum - minimum;
        if (extent.X <= 0 || extent.Y <= 0)
            throw new ArgumentException("Cannot fit a texture to a degenerate face.");
        Vector2 scale = new(repeatsX / extent.X, repeatsY / extent.Y);
        var fitted = FromMapping(polygon.Face.Normal, mapping.U * scale.X, mapping.V * scale.Y,
            (mapping.Offset - minimum) * scale);
        var fittedMapping = fitted.GetMapping(polygon.Face.Normal);
        for (int index = 0; index < polygon.Vertices.Length; index++)
            RequireCoordinates(fittedMapping, polygon.Vertices[index],
                (coordinates[index] - minimum) * scale);
        return fitted;
    }

    internal SurfaceProjection Transform(MapFace source, MapFace transformed, Matrix4x4 matrix)
    {
        Vector3[] before = [source.A, source.B, source.C];
        Vector3[] after = before.Select(point => Vector3.Transform(point, matrix)).ToArray();
        return Reproject(source.Normal, transformed.Normal, before, after);
    }

    internal SurfaceProjection Reproject(Vector3 oldNormal, Vector3 newNormal,
        IReadOnlyList<Vector3> before, IReadOnlyList<Vector3> after)
    {
        if (before.Count != after.Count || before.Count < 3)
            throw new ArgumentException("Texture locking requires corresponding face vertices.");
        var mapping = GetMapping(oldNormal);
        Vector2[] coordinates = before.Select(point => Coordinates(mapping, point)).ToArray();
        var (s, t) = Axes(newNormal);
        int second = -1, third = -1;
        double determinant = 0;
        for (int b = 1; b < after.Count; b++)
        for (int c = b + 1; c < after.Count; c++)
        {
            double candidate = ((double)after[b][s] - after[0][s]) * ((double)after[c][t] - after[0][t]) -
                               ((double)after[b][t] - after[0][t]) * ((double)after[c][s] - after[0][s]);
            if (Math.Abs(candidate) <= Math.Abs(determinant)) continue;
            determinant = candidate;
            second = b;
            third = c;
        }
        if (second < 0 || !double.IsFinite(determinant))
            throw new ArgumentException("Texture locking requires a nondegenerate face.");
        double ds1 = (double)after[second][s] - after[0][s], dt1 = (double)after[second][t] - after[0][t];
        double ds2 = (double)after[third][s] - after[0][s], dt2 = (double)after[third][t] - after[0][t];
        (Vector3 Row, float Offset) Solve(int coordinate)
        {
            double delta1 = (double)coordinates[second][coordinate] - coordinates[0][coordinate];
            double delta2 = (double)coordinates[third][coordinate] - coordinates[0][coordinate];
            double alongS = (delta1 * dt2 - delta2 * dt1) / determinant;
            double alongT = (ds1 * delta2 - ds2 * delta1) / determinant;
            return (Axis(s) * (float)alongS + Axis(t) * (float)alongT,
                (float)(coordinates[0][coordinate] - alongS * after[0][s] - alongT * after[0][t]));
        }
        var u = Solve(0);
        var v = Solve(1);
        var result = FromMapping(newNormal, u.Row, v.Row, new(u.Offset, v.Offset));
        var resultMapping = result.GetMapping(newNormal);
        for (int index = 0; index < after.Count; index++)
            RequireCoordinates(resultMapping, after[index], coordinates[index]);
        return result;
    }

    private SurfaceProjection FromMapping(Vector3 normal, Vector3 u, Vector3 v, Vector2 offset)
    {
        // Radiant texturevecs_02 decomposes the two projected rows into size,
        // rotation and crossterm. Keep full float precision when locking textures.
        var (s, t) = Axes(normal);
        double vLengthSquared = (double)v[s] * v[s] + (double)v[t] * v[t];
        double skew = ((double)u[s] * v[s] + (double)u[t] * v[t]) / vLengthSquared;
        double us = u[s] - skew * v[s], ut = u[t] - skew * v[t];
        double width = 1 / Math.Sqrt(us * us + ut * ut), height = 1 / Math.Sqrt(vLengthSquared);
        if (v[s] * ut - v[t] * us < 0) height = -height;
        var result = new SurfaceProjection((float)width, (float)height, (float)(-offset.X * width),
            (float)(-offset.Y * height), (float)(Math.Atan2(ut, us) * (180 / Math.PI)), (float)skew, Suffix);
        result.Validate();
        if (result.Width == 0 || result.Height == 0)
            throw new ArgumentException("The locked texture projection cannot be represented on this face. Turn off texture lock to change its shape without preserving the UVs.");
        return result;
    }

    private void Validate()
    {
        if (!float.IsFinite(Width) || !float.IsFinite(Height) || !float.IsFinite(ShiftX) ||
            !float.IsFinite(ShiftY) || !float.IsFinite(Rotation) || !float.IsFinite(Skew))
            throw new ArgumentException("Surface projection values must be finite numbers.");
    }

    private static (int S, int T) Axes(Vector3 normal)
    {
        if (!BrushGeometry.IsFinite(normal) || normal.LengthSquared() == 0)
            throw new ArgumentException("A surface projection requires a valid face normal.");
        Vector3 absolute = Vector3.Abs(normal);
        return absolute.Z >= absolute.X && absolute.Z >= absolute.Y ? (0, 1) :
            absolute.X >= absolute.Y ? (1, 2) : (0, 2);
    }

    private static Vector3 Axis(int index) => index == 0 ? Vector3.UnitX : index == 1 ? Vector3.UnitY : Vector3.UnitZ;

    private static Vector2 Coordinates((Vector3 U, Vector3 V, Vector2 Offset) mapping, Vector3 point) =>
        new((float)(BrushGeometry.Dot(mapping.U, point) + mapping.Offset.X),
            (float)(BrushGeometry.Dot(mapping.V, point) + mapping.Offset.Y));

    private static void RequireCoordinates((Vector3 U, Vector3 V, Vector2 Offset) mapping, Vector3 point, Vector2 expected)
    {
        Vector2 actual = Coordinates(mapping, point);
        if (!float.IsFinite(actual.X) || !float.IsFinite(actual.Y) ||
            Math.Abs(actual.X - expected.X) > 0.00002 + 0.000002 * Math.Abs(expected.X) ||
            Math.Abs(actual.Y - expected.Y) > 0.00002 + 0.000002 * Math.Abs(expected.Y))
            throw new ArgumentException("Texture locking cannot preserve this face's projection after the edit. Turn off texture lock to change its shape without preserving the UVs.");
    }
}
