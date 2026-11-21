using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

public sealed partial class PhysCollmapViewport
{
    private WriteableBitmap? _modelImage;
    private float[] _modelDepth = [];
    private int[] _modelPixels = [];
    private bool _redrawModel = true;

    private void DrawModel(DrawingContext context)
    {
        if (_modelTriangles.Count == 0) return;
        double scale = _drag?.Orbit == true ? 1 : Math.Min(2, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var pixels = PixelSize.FromSize(Bounds.Size, scale);
        if (_modelImage?.PixelSize != pixels)
        {
            _modelImage?.Dispose();
            _modelImage = new WriteableBitmap(pixels, new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _modelDepth = new float[checked(pixels.Width * pixels.Height)];
            _modelPixels = new int[_modelDepth.Length];
            _redrawModel = true;
        }
        if (_redrawModel)
        {
            DrawModelTriangles(scale);
            _redrawModel = false;
        }
        context.DrawImage(_modelImage, new Rect(Bounds.Size));
    }

    private void DrawModelTriangles(double scale)
    {
        float yaw = (float)(_orbitDegrees * Math.PI / 180);
        var (sin, cos) = MathF.SinCos(yaw);
        var eye = Vector3.Normalize(new Vector3(cos + sin, cos - sin, 1 / 0.92f));
        var right = Vector3.Normalize(new Vector3(cos - sin, -sin - cos, 0));
        var light = Vector3.Normalize(eye * .45f + Vector3.UnitZ - right * .6f);
        var projected = new Vector3[_modelVertices.Count];
        for (int i = 0; i < projected.Length; i++)
        {
            Vector3 vertex = _modelVertices[i];
            Point point = Project(vertex);
            projected[i] = new Vector3((float)(point.X * scale), (float)(point.Y * scale), Vector3.Dot(vertex - _viewCenter, eye));
        }

        using var target = _modelImage!.Lock();
        int width = target.Size.Width, height = target.Size.Height;
        Array.Fill(_modelDepth, float.NegativeInfinity);
        Array.Clear(_modelPixels);
        foreach (var triangle in _modelTriangles)
        {
            var a = projected[triangle.First.VertexIndex];
            var b = projected[triangle.Second.VertexIndex];
            var c = projected[triangle.Third.VertexIndex];
            ViewportMeshRasterizer.Triangle(a, b, c, triangle.First.Normal, triangle.Second.Normal,
                triangle.Third.Normal, light, 0xbdbdbd, -1, width, height, _modelDepth, _modelPixels,
                redOffset: 2, greenOffset: 7, blueOffset: 5);
        }
        if (target.RowBytes == width * 4)
            Marshal.Copy(_modelPixels, 0, target.Address, _modelPixels.Length);
        else
            for (int y = 0; y < height; y++)
                Marshal.Copy(_modelPixels, y * width, IntPtr.Add(target.Address, y * target.RowBytes), width);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _modelImage?.Dispose();
        _modelImage = null;
        _modelDepth = [];
        _modelPixels = [];
    }
}
