using IW4.Formats.SourceFormat.Material;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

// Thumbnails need CPU images rather than one OpenGL surface per browser item.
// The same rasterizer supplies the orbitable enlarged browser preview.
internal sealed class XModelPreviewRenderer
{
    private readonly Func<string, MaterialSource?>? _resolveMaterial;
    private readonly Func<string, (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface)>? _resolveNativeTexture;
    private readonly Dictionary<string, (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface)> _textures = new(StringComparer.Ordinal);

    internal XModelPreviewRenderer(Func<string, MaterialSource?> resolveMaterial) =>
        _resolveMaterial = resolveMaterial;

    internal XModelPreviewRenderer(Func<string, (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface)> resolveNativeTexture) =>
        _resolveNativeTexture = resolveNativeTexture;

    internal unsafe Bitmap Render(XModelSource source, int size, float yaw = -45, float pitch = 25, float zoom = 1,
        Vector2 pan = default)
    {
        XModelExportDocument document = source.Document;
        var textures = document.Materials.Select(material => Texture(material.Name)).ToArray();
        float radians = MathF.PI / 180;
        Vector3 eye = new(MathF.Cos(pitch * radians) * MathF.Cos(yaw * radians),
            MathF.Cos(pitch * radians) * MathF.Sin(yaw * radians), MathF.Sin(pitch * radians));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, eye));
        Vector3 up = Vector3.Cross(eye, right);
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        Vector3[] vertices = document.Vertices.Select(vertex => new Vector3(Vector3.Dot(vertex.Position, right),
            Vector3.Dot(vertex.Position, up), Vector3.Dot(vertex.Position, eye))).ToArray();
        foreach (Vector3 vertex in vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
        Vector3 center = (min + max) * 0.5f;
        float scale = (size - 12) / Math.Max(0.001f, Math.Max(max.X - min.X, max.Y - min.Y)) * zoom;
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 point = vertices[index] - center;
            vertices[index] = new Vector3(size * (0.5f + pan.X) + point.X * scale,
                size * (0.5f + pan.Y) - point.Y * scale, point.Z);
        }
        byte[] pixels = new byte[checked(size * size * 4)];
        float[] depth = new float[size * size];
        Array.Fill(depth, float.NegativeInfinity);
        Vector3 light = Vector3.Normalize(eye + Vector3.UnitZ * 0.6f);
        foreach (XModelExportTriangle triangle in document.Triangles)
        {
            var texture = textures[triangle.MaterialIndex];
            XModelExportCorner ca = triangle.First, cb = triangle.Second, cc = triangle.Third;
            Vector3 a = vertices[ca.VertexIndex], b = vertices[cb.VertexIndex], c = vertices[cc.VertexIndex];
            float denominator = Edge(a, b, c.X, c.Y);
            if (MathF.Abs(denominator) < 0.00001f) continue;
            int x0 = Math.Clamp((int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, size - 1);
            int x1 = Math.Clamp((int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, size - 1);
            int y0 = Math.Clamp((int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, size - 1);
            int y1 = Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, size - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float wa = Edge(b, c, x + 0.5f, y + 0.5f) / denominator;
                float wb = Edge(c, a, x + 0.5f, y + 0.5f) / denominator;
                float wc = 1 - wa - wb;
                if (wa < -0.00001f || wb < -0.00001f || wc < -0.00001f) continue;
                float z = a.Z * wa + b.Z * wb + c.Z * wc;
                int destination = y * size + x;
                if (z <= depth[destination]) continue;
                Vector2 uv = ca.Uv0 * wa + cb.Uv0 * wb + cc.Uv0 * wc;
                int tx = Math.Min(texture.Width - 1, (int)((uv.X - MathF.Floor(uv.X)) * texture.Width));
                int ty = Math.Min(texture.Height - 1, (int)((uv.Y - MathF.Floor(uv.Y)) * texture.Height));
                int texel = (ty * texture.Width + tx) * 4;
                Vector4 color = ca.Color * wa + cb.Color * wb + cc.Color * wc;
                float alpha = texture.Pixels[texel + 3] * Math.Clamp(color.W, 0, 1);
                if (texture.Surface.AlphaTest switch
                    {
                        GfxAlphaTest.GreaterThanZero => alpha <= 0,
                        GfxAlphaTest.LessThan128 => alpha >= 128,
                        GfxAlphaTest.GreaterThanOrEqualTo128 => alpha < 128,
                        _ => false
                    }) continue;
                Vector3 normal = ca.Normal * wa + cb.Normal * wb + cc.Normal * wc;
                float shade = normal.LengthSquared() > 0.000001f
                    ? 0.35f + 0.65f * Math.Max(0, Vector3.Dot(Vector3.Normalize(normal), light)) : 1;
                int pixel = destination * 4;
                pixels[pixel] = Channel(texture.Pixels[texel] * color.X * shade);
                pixels[pixel + 1] = Channel(texture.Pixels[texel + 1] * color.Y * shade);
                pixels[pixel + 2] = Channel(texture.Pixels[texel + 2] * color.Z * shade);
                pixels[pixel + 3] = texture.Surface.SupportsAlpha ? Channel(alpha) : (byte)255;
                depth[destination] = z;
            }
        }
        fixed (byte* data = pixels)
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)data, new PixelSize(size, size),
                new Avalonia.Vector(96, 96), size * 4);
    }

    private (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface) Texture(string name)
    {
        if (_textures.TryGetValue(name, out var texture)) return texture;
        if (_resolveNativeTexture is not null)
        {
            texture = _resolveNativeTexture(name);
            _textures.Add(name, texture);
            return texture;
        }
        if (_resolveMaterial?.Invoke(name) is not { IsSky: false } material)
            throw new FileNotFoundException($"Model material '{name}' has no available color image. Extract its material and image into the raw asset folder.");
        using var image = MaterialImages.Load(material, 256);
        using var converted = new WriteableBitmap(image.PixelSize, new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock();
        image.CopyPixels(buffer);
        byte[] pixels = new byte[checked(image.PixelSize.Width * image.PixelSize.Height * 4)];
        for (int row = 0; row < image.PixelSize.Height; row++)
            Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * image.PixelSize.Width * 4, image.PixelSize.Width * 4);
        texture = (image.PixelSize.Width, image.PixelSize.Height, pixels, material.Surface);
        _textures.Add(name, texture);
        return texture;
    }

    private static byte Channel(float value) => (byte)Math.Clamp(value, 0, 255);
    private static float Edge(Vector3 a, Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
