using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Iw4Radiant.Viewports.Camera;

public sealed class CameraFoliageOverlay : Control
{
    private static readonly Pen Outline = new(new SolidColorBrush(Color.Parse("#D6E887")), 1.5);
    private Point[] _points = [];

    internal void SetBrush(IReadOnlyList<Point>? points)
    {
        _points = points?.ToArray() ?? [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_points.Length < 3) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(_points[0], false);
            for (int index = 1; index < _points.Length; index++) path.LineTo(_points[index]);
            path.EndFigure(true);
        }
        context.DrawGeometry(null, Outline, geometry);
        Point center = new(_points.Average(point => point.X), _points.Average(point => point.Y));
        context.DrawLine(Outline, center - new Vector(5, 0), center + new Vector(5, 0));
        context.DrawLine(Outline, center - new Vector(0, 5), center + new Vector(0, 5));
    }
}
