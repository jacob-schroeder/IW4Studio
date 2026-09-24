using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class RopeGeometry
{
    internal static MapTerrain[] Create(Vector3 start, Vector3 end, float thickness, float slackPercent,
        int segments, string material)
    {
        if (!Finite(start) || !Finite(end) || !Finite(end - start))
            throw new ArgumentException("Both rope endpoints need finite map coordinates.");
        Vector3 chord = end - start;
        float length = chord.Length();
        if (!float.IsFinite(length) || length < 1)
            throw new ArgumentException("Move the rope endpoints at least one map unit apart.");
        if (!float.IsFinite(thickness) || thickness < 2 || thickness > 256)
            throw new ArgumentException("Choose a rope thickness from 2 to 256 map units.");
        if (!float.IsFinite(slackPercent) || slackPercent < 0 || slackPercent > 100)
            throw new ArgumentException("Choose rope slack from 0 to 100%.");
        if (segments is < 2 or > 64)
            throw new ArgumentException("Choose 2 to 64 rope segments.");
        if (string.IsNullOrWhiteSpace(material))
            throw new ArgumentException("Choose a rope material in the browser first.");

        Vector3 direction = chord / length;
        Vector3 gravity = -Vector3.UnitZ;
        Vector3 sagDirection = gravity - direction * Vector3.Dot(gravity, direction);
        if (sagDirection.LengthSquared() < 0.0001f)
            sagDirection = Vector3.UnitX - direction * Vector3.Dot(Vector3.UnitX, direction);
        sagDirection = Vector3.Normalize(sagDirection);
        Vector3 side = Vector3.Normalize(Vector3.Cross(direction, sagDirection));
        float sag = length * slackPercent / 100;
        int width = segments + 1, height = 9, count = width * height;
        var rope = new MapTerrain
        {
            Material = material, Smoothing = "smoothing_smooth", Width = width, Height = height,
            Vertices = new Vector3[count], TextureCoordinates = new Vector2[count],
            LightmapCoordinates = new Vector2[count], Colors = new Vector4[count], EdgeFlags = new int[count]
        };
        float traveled = 0;
        Vector3 previous = start;
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / segments;
            Vector3 center = Vector3.Lerp(start, end, t) + sagDirection * (4 * t * (1 - t) * sag);
            Vector3 tangent = Vector3.Normalize(chord + sagDirection * (4 * (1 - 2 * t) * sag));
            Vector3 ringUp = Vector3.Normalize(Vector3.Cross(side, tangent));
            if (x > 0) traveled += Vector3.Distance(previous, center);
            previous = center;
            for (int y = 0; y < height; y++)
            {
                float around = (float)y / (height - 1);
                float angle = around * 2 * MathF.PI;
                int index = x * height + y;
                Vector3 radial = side * MathF.Cos(angle) + ringUp * MathF.Sin(angle);
                rope.Vertices[index] = center + radial * (thickness / 2);
                rope.TextureCoordinates[index] = new Vector2(traveled, thickness * MathF.PI * around) / 128;
                rope.LightmapCoordinates[index] = new Vector2(t, around);
                rope.Colors[index] = Vector4.One;
                rope.EdgeFlags[index] = 1;
                if (!Finite(rope.Vertices[index]) || !float.IsFinite(rope.TextureCoordinates[index].X))
                    throw new ArgumentException("The rope dimensions produce coordinates outside the supported map range.");
            }
        }
        TerrainContents.SetNonColliding(rope, true);
        return TerrainConversion.ToNativeTiles(rope);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
