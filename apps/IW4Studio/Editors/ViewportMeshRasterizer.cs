using System.Numerics;

namespace IW4.Studio.Desktop.Editors;

internal static class ViewportMeshRasterizer
{
    internal static void Triangle(
        Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC,
        Vector3 light, int baseColor, int owner, int width, int height,
        float[] depth, int[] pixels, int[]? owners = null, bool wireframe = false,
        int redOffset = 0, int greenOffset = 0, int blueOffset = 0)
    {
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (!float.IsFinite(area) || MathF.Abs(area) < .001f) return;
        float minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        float maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        float maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        if (maxX < 0 || minX >= width || maxY < 0 || minY >= height) return;
        int left = (int)MathF.Max(0, MathF.Floor(minX));
        int right = (int)MathF.Min(width - 1, MathF.Ceiling(maxX));
        int top = (int)MathF.Max(0, MathF.Floor(minY));
        int bottom = (int)MathF.Min(height - 1, MathF.Ceiling(maxY));
        float lightA = Shade(normalA), lightB = Shade(normalB), lightC = Shade(normalC);
        float du = (c.Y - a.Y) / area, dv = (a.Y - b.Y) / area;
        for (int y = top; y <= bottom; y++)
        {
            float u = ((left + .5f - a.X) * (c.Y - a.Y) - (y + .5f - a.Y) * (c.X - a.X)) / area;
            float v = ((b.X - a.X) * (y + .5f - a.Y) - (b.Y - a.Y) * (left + .5f - a.X)) / area;
            for (int x = left; x <= right; x++, u += du, v += dv)
            {
                float w = 1 - u - v;
                if (u < 0 || v < 0 || w < 0) continue;
                float z = w * a.Z + u * b.Z + v * c.Z;
                int index = y * width + x;
                if (z <= depth[index]) continue;
                depth[index] = z;
                int tone = (int)(w * lightA + u * lightB + v * lightC);
                int red = Math.Clamp(((baseColor >> 16) & 255) * tone / 189 + redOffset, 0, 255);
                int green = Math.Clamp(((baseColor >> 8) & 255) * tone / 189 + greenOffset, 0, 255);
                int blue = Math.Clamp((baseColor & 255) * tone / 189 + blueOffset, 0, 255);
                pixels[index] = unchecked((int)0xff000000) | red << 16 | green << 8 | blue;
                if (owners is not null) owners[index] = owner;
            }
        }
        if (wireframe)
        {
            Edge(a, b); Edge(b, c); Edge(c, a);
        }

        float Shade(Vector3 normal) => 82 + 107 * Math.Clamp(Vector3.Dot(normal, light), 0, 1);
        void Edge(Vector3 first, Vector3 second)
        {
            float dx = second.X - first.X, dy = second.Y - first.Y;
            float start = 0, end = 1;
            if (!Clip(-dx, first.X, ref start, ref end) ||
                !Clip(dx, width - 1 - first.X, ref start, ref end) ||
                !Clip(-dy, first.Y, ref start, ref end) ||
                !Clip(dy, height - 1 - first.Y, ref start, ref end)) return;
            int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) * (end - start));
            if (steps <= 0) return;
            // The depth buffer keeps hidden edges from showing through the mesh.
            for (int i = 0; i <= steps; i++)
            {
                float t = start + (end - start) * i / steps;
                int x = (int)MathF.Round(first.X + dx * t), y = (int)MathF.Round(first.Y + dy * t);
                if ((uint)x >= (uint)width || (uint)y >= (uint)height) continue;
                int index = y * width + x;
                float z = first.Z + (second.Z - first.Z) * t;
                if (z + .01f < depth[index]) continue;
                pixels[index] = unchecked((int)0xff243432);
            }
        }
        static bool Clip(float p, float q, ref float start, ref float end)
        {
            if (p == 0) return q >= 0;
            float t = q / p;
            if (p < 0) start = MathF.Max(start, t);
            else end = MathF.Min(end, t);
            return start <= end;
        }
    }
}
