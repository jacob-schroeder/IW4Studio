using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Codecs.GfxMap;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightmapCompiler
{
    // One secondary luxel covers eight map units; primary sun visibility has twice the resolution.
    private const float LuxelSize = 8;
    private const int Border = 2;

    internal static (IReadOnlyList<GfxLightmapArray> Lightmaps, Vector2[][] FaceUvs, byte[] FaceLightmapIndices)
        BakeLightmaps(BrushLightingScene scene)
    {
        var lightmaps = new List<GfxLightmapArray>();
        var faceUvs = new Vector2[scene.Polygons.Count][];
        var faceIndices = new byte[scene.Polygons.Count];
        byte[] primary = new byte[GfxLightmapCodec.PrimaryWidth * GfxLightmapCodec.PrimaryHeight];
        byte[] secondary = new byte[GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryHeight * 4];
        int cursorX = 0, cursorY = 0, rowHeight = 0;
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = scene.Polygons[faceIndex];
            faceUvs[faceIndex] = new Vector2[polygon.Vertices.Length];
            if (scene.IsSky(faceIndex))
            {
                faceIndices[faceIndex] = 31;
                continue;
            }
            Vector3 normal = polygon.Normal, anchor = polygon.Vertices[0];
            Vector3 uAxis = Vector3.Normalize(polygon.Vertices[1] - anchor);
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));
            Vector2[] coordinates = polygon.Vertices.Select(point => new Vector2(
                Vector3.Dot(point - anchor, uAxis), Vector3.Dot(point - anchor, vAxis))).ToArray();
            Vector2 minimum = coordinates.Aggregate(Vector2.Min), maximum = coordinates.Aggregate(Vector2.Max);
            int width = checked((int)MathF.Ceiling((maximum.X - minimum.X) / LuxelSize) + 1 + Border * 2);
            int height = checked((int)MathF.Ceiling((maximum.Y - minimum.Y) / LuxelSize) + 1 + Border * 2);
            if (width > GfxLightmapCodec.SecondaryWidth || height > GfxLightmapCodec.SecondaryPlaneHeight)
                throw new NotSupportedException($"Surface {faceIndex} exceeds one baked lightmap tile. Split the surface into smaller brushes or mesh cells.");
            if (cursorX + width > GfxLightmapCodec.SecondaryWidth)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }
            if (cursorY + height > GfxLightmapCodec.SecondaryPlaneHeight)
            {
                Flush();
                primary = new byte[primary.Length];
                secondary = new byte[secondary.Length];
                cursorX = cursorY = rowHeight = 0;
            }
            faceIndices[faceIndex] = checked((byte)lightmaps.Count);
            for (int vertex = 0; vertex < coordinates.Length; vertex++)
                faceUvs[faceIndex][vertex] = new Vector2(
                    (cursorX + Border + (coordinates[vertex].X - minimum.X) / LuxelSize + 0.5f) / GfxLightmapCodec.SecondaryWidth,
                    (cursorY + Border + (coordinates[vertex].Y - minimum.Y) / LuxelSize + 0.5f) / GfxLightmapCodec.SecondaryPlaneHeight);

            for (int y = 0; y < height; y++)
            {
                scene.CancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    Vector3 point = Position(x, y);
                    Vector3 ambient = scene.Ambient(point, polygon.Sample(point).Normal);
                    int upper = ((cursorY + y) * GfxLightmapCodec.SecondaryWidth + cursorX + x) * 4;
                    int lower = upper + GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryPlaneHeight * 4;
                    // Native lm_* reconstructs upper.rgb * normal.z plus lower.rgb *
                    // directional weight, then squares the result. Sun is added separately
                    // using the primary visibility image. This bake stores diffuse sky in
                    // the upper term and leaves the additional directional term empty.
                    secondary[upper] = EncodeAmbient(ambient.X);
                    secondary[upper + 1] = EncodeAmbient(ambient.Y);
                    secondary[upper + 2] = EncodeAmbient(ambient.Z);
                    secondary[upper + 3] = secondary[lower + 3] = 128;
                    for (int py = 0; py < 2; py++)
                    for (int px = 0; px < 2; px++)
                    {
                        Vector3 sample = Position(x + (px - 0.5f) * 0.5f, y + (py - 0.5f) * 0.5f);
                        int offset = ((cursorY + y) * 2 + py) * GfxLightmapCodec.PrimaryWidth + (cursorX + x) * 2 + px;
                        primary[offset] = (byte)Math.Clamp(MathF.Round(scene.SunVisibility(sample, normal) * 255), 0, 255);
                    }
                }
            }
            cursorX += width;
            rowHeight = Math.Max(rowHeight, height);

            Vector3 Position(float x, float y)
            {
                Vector3 point = anchor + uAxis * (minimum.X + (x - Border) * LuxelSize) +
                    vAxis * (minimum.Y + (y - Border) * LuxelSize);
                bool inside = true;
                Vector3 closest = point;
                float closestDistance = float.PositiveInfinity;
                for (int edge = 0; edge < polygon.Vertices.Length; edge++)
                {
                    Vector3 a = polygon.Vertices[edge], b = polygon.Vertices[(edge + 1) % polygon.Vertices.Length];
                    Vector3 segment = b - a;
                    if (Vector3.Dot(Vector3.Cross(segment, point - a), normal) < 0) inside = false;
                    Vector3 candidate = a + segment * Math.Clamp(Vector3.Dot(point - a, segment) / segment.LengthSquared(), 0, 1);
                    float distance = Vector3.DistanceSquared(candidate, point);
                    if (distance < closestDistance) { closestDistance = distance; closest = candidate; }
                }
                return inside ? point : closest;
            }
        }
        Flush();
        return (lightmaps, faceUvs, faceIndices);

        void Flush()
        {
            if (lightmaps.Count >= 31)
                throw new NotSupportedException("The baked world exceeds the 31-lightmap v22 limit.");
            lightmaps.Add(GfxLightmapCodec.Create(lightmaps.Count, primary, secondary));
        }
    }

    private static byte EncodeAmbient(float value)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new NotSupportedException("Calculated sky lighting exceeds the native lightmap's encoded range.");
        return (byte)MathF.Round(255 * MathF.Sqrt(Math.Clamp(value, 0, 1)));
    }
}
