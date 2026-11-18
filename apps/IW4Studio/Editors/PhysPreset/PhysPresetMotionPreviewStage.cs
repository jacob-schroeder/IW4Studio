using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.PhysPreset;

internal sealed class PhysPresetMotionPreviewStage : Control
{
    private static readonly IBrush StageBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromRgb(22, 34, 46), 0),
            new GradientStop(Color.FromRgb(42, 60, 76), 1)
        ]
    };

    private static readonly IBrush FloorBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromArgb(0, 149, 181, 205), 0),
            new GradientStop(Color.FromArgb(46, 149, 181, 205), 1)
        ]
    };

    private static readonly IBrush BallBrush = new RadialGradientBrush
    {
        Center = new RelativePoint(0.38, 0.34, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.29, 0.24, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.74, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.74, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromRgb(247, 250, 250), 0),
            new GradientStop(Color.FromRgb(186, 202, 213), 0.52),
            new GradientStop(Color.FromRgb(73, 94, 113), 0.86),
            new GradientStop(Color.FromRgb(47, 66, 85), 1)
        ]
    };

    private static readonly IBrush ShadowBrush = new RadialGradientBrush
    {
        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromArgb(119, 7, 14, 22), 0),
            new GradientStop(Color.FromArgb(0, 7, 14, 22), 1)
        ]
    };

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(56, 151, 183, 208)), 1);
    private static readonly Pen ImpactPen = new(new SolidColorBrush(Color.FromArgb(160, 150, 208, 251)), 1);
    private static readonly Pen PathPen = new(new SolidColorBrush(Color.FromRgb(145, 203, 248)), 1.4, DashStyle.Dash);
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.FromArgb(103, 201, 226, 244)), 1, DashStyle.Dash);

    private Point[] _samples = [];
    private double _frame;
    private bool _showTrajectory = true;
    private Geometry? _trajectory;
    private Size _trajectorySize;

    public PhysPresetMotionPreviewStage() => ClipToBounds = true;

    public void SetSamples(Point[] samples)
    {
        _samples = samples;
        _trajectory = null;
        InvalidateVisual();
    }

    public void SetFrame(double frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    public void SetShowTrajectory(bool show)
    {
        _showTrajectory = show;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        context.DrawRectangle(StageBrush, null, new Rect(Bounds.Size));
        DrawFloor(context, width, height);
        if (_samples.Length == 0)
            return;

        double floor = height * 0.76;
        double radius = Math.Min(38, Math.Min(height * 0.105, width * 0.065));
        int index = (int)_frame;
        Point start = _samples[index];
        Point end = _samples[Math.Min(index + 1, _samples.Length - 1)];
        Point sample = start + ((end - start) * (_frame - index));
        Point center = Project(sample, radius);

        if (_showTrajectory)
        {
            DrawTrajectory(context, radius, floor);
            if (_frame == 0 && sample.Y > 0)
                context.DrawLine(GuidePen, new Point(center.X, center.Y + radius + 5), new Point(center.X, floor));
        }

        using (context.PushOpacity(1 / (1 + sample.Y)))
        {
            context.DrawEllipse(ShadowBrush, null, new Point(center.X, floor),
                radius * (1 + sample.Y * 0.5), radius * 0.26);
        }

        context.DrawEllipse(BallBrush, null, center, radius, radius);
    }

    private static void DrawFloor(DrawingContext context, double width, double height)
    {
        double horizon = height * 0.44;
        double depth = height - horizon;
        double vanishingX = width * 0.53;
        context.DrawRectangle(FloorBrush, null, new Rect(0, horizon, width, depth));

        for (int column = -10; column <= 10; column++)
            context.DrawLine(GridPen, new Point(vanishingX, horizon), new Point(vanishingX + column * width * 0.12, height));

        for (int row = 1; row <= 11; row++)
        {
            double y = horizon + depth * Math.Pow(0.75, row);
            context.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }
    }

    private void DrawTrajectory(DrawingContext context, double radius, double floor)
    {
        if (_trajectory is null || _trajectorySize != Bounds.Size)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(Project(_samples[0], radius), isFilled: false);
                for (int index = 2; index < _samples.Length; index += 2)
                    path.LineTo(Project(_samples[index], radius));
                path.EndFigure(isClosed: false);
            }

            _trajectory = geometry;
            _trajectorySize = Bounds.Size;
        }

        context.DrawGeometry(null, PathPen, _trajectory);
        double lastImpact = double.NegativeInfinity;
        for (int index = 1; index < _samples.Length; index++)
        {
            if (_samples[index].Y != 0 || _samples[index - 1].Y <= 0)
                continue;

            double x = Project(_samples[index], radius).X;
            if (x - lastImpact < 15)
                continue;

            context.DrawEllipse(null, ImpactPen, new Point(x, floor), Math.Min(10, radius * 0.47), 3);
            lastImpact = x;
        }
    }

    private Point Project(Point sample, double radius)
    {
        double x = Bounds.Width * (0.16 + 0.13 * sample.X / (1 + 0.11 * sample.X));
        double y = Bounds.Height * (0.76 - 0.325 * sample.Y) - radius;
        return new Point(x, y);
    }
}
