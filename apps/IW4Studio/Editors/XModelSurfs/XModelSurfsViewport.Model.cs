using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IW4.Studio.Desktop.Editors.XModelSurfs;

public sealed partial class XModelSurfsViewport
{
    private WriteableBitmap? _meshImage;
    private float[] _meshDepth = [];
    private int[] _meshPixels = [];
    private int[] _meshOwners = [];
    private bool _redrawMesh = true;
    private double _meshScale = 1;

    private void DrawMesh(DrawingContext context)
    {
        if (_surfaces.Count == 0) return;
        double maxPixelsScale = Math.Sqrt(4_000_000 / Math.Max(1, Bounds.Width * Bounds.Height));
        double scale = Math.Min(maxPixelsScale, _drag?.Orbit == true ? 1 :
            Math.Min(2, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        PixelSize size = PixelSize.FromSize(Bounds.Size, scale);
        if (size.Width < 1 || size.Height < 1) return;
        if (_meshImage?.PixelSize != size)
        {
            _meshImage?.Dispose();
            _meshImage = new WriteableBitmap(size, new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            int length = checked(size.Width * size.Height);
            _meshDepth = new float[length];
            _meshPixels = new int[length];
            _meshOwners = new int[length];
            _redrawMesh = true;
        }
        if (_redrawMesh)
        {
            RasterizeMesh(scale);
            _meshScale = scale;
            _redrawMesh = false;
        }
        context.DrawImage(_meshImage!, new Rect(Bounds.Size));
    }

    private void RasterizeMesh(double scale)
    {
        float yaw = (float)(_orbitDegrees * Math.PI / 180);
        var (sin, cos) = MathF.SinCos(yaw);
        Vector3 eye = Vector3.Normalize(new Vector3(cos + sin, cos - sin, 1 / .92f));
        Vector3 right = Vector3.Normalize(new Vector3(cos - sin, -sin - cos, 0));
        Vector3 light = Vector3.Normalize(eye * .45f + Vector3.UnitZ - right * .6f);
        using var target = _meshImage!.Lock();
        int width = target.Size.Width, height = target.Size.Height;
        Array.Fill(_meshDepth, float.NegativeInfinity);
        Array.Clear(_meshPixels);
        Array.Fill(_meshOwners, -1);
        for (int surfaceIndex = 0; surfaceIndex < _surfaces.Count; surfaceIndex++)
        {
            XModelSurfsViewportSurface surface = _surfaces[surfaceIndex];
            if (surface.Positions.Count == 0 || surface.TriangleIndices.Count < 3) continue;
            var worldPositions = new Vector3[surface.Positions.Count];
            var projected = new Vector3[surface.Positions.Count];
            var normals = new Vector3[surface.Positions.Count];
            Vector3 size = _drag is { Axis: >= 0 } active && active.Index == surfaceIndex ? active.Size : surface.Size;
            for (int vertex = 0; vertex < projected.Length; vertex++)
            {
                Vector3 world = Transform(surface.Positions[vertex], surface, surfaceIndex);
                worldPositions[vertex] = world;
                Point point = Project(world);
                projected[vertex] = new Vector3((float)(point.X * scale), (float)(point.Y * scale),
                    Vector3.Dot(world - _viewCenter, eye));
                if (vertex < surface.Normals.Count)
                {
                    Vector3 n = surface.Normals[vertex];
                    if (surface.OriginalSize.X > 0 && size.X > 0)
                        n.X *= surface.OriginalSize.X / size.X;
                    if (surface.OriginalSize.Y > 0 && size.Y > 0)
                        n.Y *= surface.OriginalSize.Y / size.Y;
                    if (surface.OriginalSize.Z > 0 && size.Z > 0)
                        n.Z *= surface.OriginalSize.Z / size.Z;
                    if (Finite(n) && n.LengthSquared() > 1e-12f)
                        normals[vertex] = Vector3.Normalize(n);
                }
            }
            int color = surfaceIndex == _selectedIndex ? 0xe5c783 : 0xb7c6c2;
            for (int index = 0; index + 2 < surface.TriangleIndices.Count; index += 3)
            {
                int ia = surface.TriangleIndices[index], ib = surface.TriangleIndices[index + 1], ic = surface.TriangleIndices[index + 2];
                if ((uint)ia >= (uint)projected.Length || (uint)ib >= (uint)projected.Length ||
                    (uint)ic >= (uint)projected.Length) continue;
                Vector3 a = projected[ia], b = projected[ib], c = projected[ic];
                if (!Finite(a) || !Finite(b) || !Finite(c)) continue;
                Vector3 fallback = Vector3.UnitZ;
                if (normals[ia] == Vector3.Zero || normals[ib] == Vector3.Zero || normals[ic] == Vector3.Zero)
                {
                    fallback = Vector3.Cross(worldPositions[ib] - worldPositions[ia],
                        worldPositions[ic] - worldPositions[ia]);
                    fallback = fallback.LengthSquared() > 1e-12f ? Vector3.Normalize(fallback) : Vector3.UnitZ;
                }
                Vector3 na = normals[ia] == Vector3.Zero ? fallback : normals[ia];
                Vector3 nb = normals[ib] == Vector3.Zero ? fallback : normals[ib];
                Vector3 nc = normals[ic] == Vector3.Zero ? fallback : normals[ic];
                ViewportMeshRasterizer.Triangle(a, b, c, na, nb, nc, light, color, surfaceIndex,
                    width, height, _meshDepth, _meshPixels, _meshOwners, _wireframe);
            }
        }
        if (target.RowBytes == width * 4)
            Marshal.Copy(_meshPixels, 0, target.Address, _meshPixels.Length);
        else
            for (int y = 0; y < height; y++)
                Marshal.Copy(_meshPixels, y * width, IntPtr.Add(target.Address, y * target.RowBytes), width);
    }

    private int HitSurface(Point point)
    {
        if (_meshImage is null) return -1;
        int x = (int)(point.X * _meshScale), y = (int)(point.Y * _meshScale);
        int width = _meshImage.PixelSize.Width, height = _meshImage.PixelSize.Height;
        return (uint)x < (uint)width && (uint)y < (uint)height ? _meshOwners[y * width + x] : -1;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _meshImage?.Dispose();
        _meshImage = null;
        _meshDepth = [];
        _meshPixels = [];
        _meshOwners = [];
    }
}
