using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Vec2 = IW4.Assets.Math.Vec2;

namespace IW4.Studio.Desktop.Editors.Weapon;

/// <summary>
/// Read-only plot of IW4 weapon accuracy knots. The horizontal coordinate is
/// the engine's normalized-distance domain; the vertical coordinate is the
/// authored accuracy value.
/// </summary>
public sealed class WeaponAccuracyGraphControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<Vec2>?> PointsProperty =
        AvaloniaProperty.Register<
            WeaponAccuracyGraphControl,
            IReadOnlyList<Vec2>?>(nameof(Points));

    private const double LeftMargin = 46;
    private const double TopMargin = 24;
    private const double RightMargin = 14;
    private const double BottomMargin = 42;
    private const double CompactLeftMargin = 36;
    private const double CompactTopMargin = 20;
    private const double CompactRightMargin = 8;
    private const double CompactBottomMargin = 32;

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(19, 23, 29));
    private static readonly IBrush PlotBrush =
        new SolidColorBrush(Color.FromRgb(15, 19, 24));
    private static readonly Pen BorderPen =
        new(new SolidColorBrush(Color.FromRgb(59, 67, 78)), 1);
    private static readonly Pen GridPen =
        new(new SolidColorBrush(Color.FromArgb(42, 151, 164, 179)), 1);
    private static readonly Pen AxisPen =
        new(new SolidColorBrush(Color.FromRgb(92, 103, 117)), 1);
    private static readonly IBrush LabelBrush =
        new SolidColorBrush(Color.FromRgb(166, 176, 188));
    private static readonly IBrush EmptyBrush =
        new SolidColorBrush(Color.FromRgb(132, 143, 157));
    private static readonly IBrush CurveBrush =
        new SolidColorBrush(Color.FromRgb(82, 211, 132));
    private static readonly Pen CurvePen = new(CurveBrush, 2);
    private static readonly Pen MarkerOutlinePen =
        new(new SolidColorBrush(Color.FromRgb(24, 68, 43)), 1);

    static WeaponAccuracyGraphControl() =>
        AffectsRender<WeaponAccuracyGraphControl>(PointsProperty);

    public WeaponAccuracyGraphControl() => ClipToBounds = true;

    public IReadOnlyList<Vec2>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 1 ||
            height <= 1)
        {
            return;
        }

        var frame = new Rect(
            0.5,
            0.5,
            Math.Max(0, width - 1),
            Math.Max(0, height - 1));
        context.FillRectangle(BackgroundBrush, frame, 6);
        context.DrawRectangle(BorderPen, frame, 6);

        bool compact = width < 260 || height < 150;
        double left = compact ? CompactLeftMargin : LeftMargin;
        double top = compact ? CompactTopMargin : TopMargin;
        double right = compact ? CompactRightMargin : RightMargin;
        double bottom = compact ? CompactBottomMargin : BottomMargin;
        if (width <= left + right + 12 || height <= top + bottom + 12)
            return;

        var plot = new Rect(
            left,
            top,
            width - left - right,
            height - top - bottom);
        context.FillRectangle(PlotBrush, plot, 3);

        IReadOnlyList<Vec2> points = Points ?? [];
        IReadOnlyList<GraphPoint> plottable = CollectPlottablePoints(points);
        AxisRange xRange = CreateDistanceRange(plottable);
        AxisRange yRange = CreateAccuracyRange(plottable);
        DrawAxes(context, plot, xRange, yRange, compact);

        if (plottable.Count == 0)
        {
            DrawEmptyState(
                context,
                plot,
                points.Count > 0
                    ? "No finite accuracy points"
                    : "No accuracy graph data",
                compact);
            return;
        }

        DrawCurve(context, plot, xRange, yRange, points);
    }

    private static IReadOnlyList<GraphPoint> CollectPlottablePoints(
        IReadOnlyList<Vec2> points)
    {
        if (points.Count == 0)
            return [];

        var result = new List<GraphPoint>(points.Count);
        foreach (Vec2 point in points)
        {
            if (TryReadPoint(point, out GraphPoint graphPoint))
                result.Add(graphPoint);
        }
        return result;
    }

    private static bool TryReadPoint(Vec2 source, out GraphPoint point)
    {
        double distance = source.a;
        double accuracy = source.b;
        if (!double.IsFinite(distance) ||
            !double.IsFinite(accuracy))
        {
            point = default;
            return false;
        }

        point = new GraphPoint(distance, accuracy);
        return true;
    }

    private static AxisRange CreateDistanceRange(
        IReadOnlyList<GraphPoint> points)
    {
        double minimum = 0;
        double maximum = 1;
        foreach (GraphPoint point in points)
        {
            minimum = Math.Min(minimum, point.Distance);
            maximum = Math.Max(maximum, point.Distance);
        }
        return CreateAxisRange(minimum, maximum);
    }

    private static AxisRange CreateAccuracyRange(
        IReadOnlyList<GraphPoint> points)
    {
        double minimum = 0;
        double maximum = 1;
        foreach (GraphPoint point in points)
        {
            minimum = Math.Min(minimum, point.Accuracy);
            maximum = Math.Max(maximum, point.Accuracy);
        }
        return CreateAxisRange(minimum, maximum);
    }

    private static AxisRange CreateAxisRange(double minimum, double maximum)
    {
        double step = NiceStep((maximum - minimum) / 4);
        double lower = Math.Floor(minimum / step) * step;
        double upper = Math.Ceiling(maximum / step) * step;
        if (!double.IsFinite(lower) ||
            !double.IsFinite(upper) ||
            upper <= lower)
        {
            return new AxisRange(0, 1, 4);
        }

        int tickCount = Math.Clamp(
            (int)Math.Round((upper - lower) / step),
            2,
            8);
        return new AxisRange(lower, upper, tickCount);
    }

    private static double NiceStep(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            return 0.25;

        double exponent = Math.Floor(Math.Log10(value));
        double magnitude = Math.Pow(10, exponent);
        double fraction = value / magnitude;
        double niceFraction = fraction switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10
        };
        double result = niceFraction * magnitude;
        return double.IsFinite(result) && result > 0 ? result : 0.25;
    }

    private static void DrawAxes(
        DrawingContext context,
        Rect plot,
        AxisRange xRange,
        AxisRange yRange,
        bool compact)
    {
        int xDivisions = compact
            ? 2
            : xRange.TickCount;
        for (int index = 0; index <= xDivisions; index++)
        {
            double position = index / (double)xDivisions;
            double x = plot.Left + plot.Width * position;
            if (index > 0 && index < xDivisions)
            {
                context.DrawLine(
                    GridPen,
                    new Point(x, plot.Top),
                    new Point(x, plot.Bottom));
            }

            double value = xRange.Minimum +
                (xRange.Maximum - xRange.Minimum) * position;
            DrawLabel(
                context,
                FormatAxisValue(value),
                new Rect(x - 22, plot.Bottom + 3, 44, compact ? 12 : 14),
                compact ? 8 : 9,
                LabelBrush,
                TextAlignment.Center);
        }

        int yDivisions = compact
            ? 2
            : yRange.TickCount;
        for (int index = 0; index <= yDivisions; index++)
        {
            double normalized = index / (double)yDivisions;
            double y = plot.Bottom - plot.Height * normalized;
            if (index > 0 && index < yDivisions)
            {
                context.DrawLine(
                    GridPen,
                    new Point(plot.Left, y),
                    new Point(plot.Right, y));
            }

            double value = yRange.Minimum +
                (yRange.Maximum - yRange.Minimum) * normalized;
            DrawLabel(
                context,
                FormatAxisValue(value),
                new Rect(2, y - 7, Math.Max(1, plot.Left - 8), 14),
                compact ? 8 : 9,
                LabelBrush,
                TextAlignment.Right);
        }

        context.DrawLine(
            AxisPen,
            new Point(plot.Left, plot.Top),
            new Point(plot.Left, plot.Bottom));
        context.DrawLine(
            AxisPen,
            new Point(plot.Left, plot.Bottom),
            new Point(plot.Right, plot.Bottom));

        DrawLabel(
            context,
            "Accuracy",
            new Rect(plot.Left, 3, plot.Width, Math.Max(12, plot.Top - 5)),
            compact ? 8 : 9,
            LabelBrush,
            TextAlignment.Left,
            FontWeight.SemiBold);
        DrawLabel(
            context,
            "Normalized distance",
            new Rect(
                plot.Left,
                plot.Bottom + (compact ? 16 : 20),
                plot.Width,
                compact ? 13 : 16),
            compact ? 8 : 9,
            LabelBrush,
            TextAlignment.Center,
            FontWeight.SemiBold);
    }

    private static void DrawCurve(
        DrawingContext context,
        Rect plot,
        AxisRange xRange,
        AxisRange yRange,
        IReadOnlyList<Vec2> points)
    {
        var markers = new List<Point>(points.Count);
        Point? previous = null;
        using (context.PushClip(plot))
        {
            foreach (Vec2 source in points)
            {
                if (!TryReadPoint(source, out GraphPoint point))
                {
                    previous = null;
                    continue;
                }

                Point projected = Project(point, plot, xRange, yRange);
                if (previous is { } start)
                    context.DrawLine(CurvePen, start, projected);
                markers.Add(projected);
                previous = projected;
            }

            foreach (Point marker in markers)
            {
                context.DrawEllipse(
                    CurveBrush,
                    MarkerOutlinePen,
                    new Rect(marker.X - 3.5, marker.Y - 3.5, 7, 7));
            }
        }
    }

    private static Point Project(
        GraphPoint point,
        Rect plot,
        AxisRange xRange,
        AxisRange yRange)
    {
        double x = (point.Distance - xRange.Minimum) /
            (xRange.Maximum - xRange.Minimum);
        double y = (point.Accuracy - yRange.Minimum) /
            (yRange.Maximum - yRange.Minimum);
        return new Point(
            plot.Left + x * plot.Width,
            plot.Bottom - y * plot.Height);
    }

    private static void DrawEmptyState(
        DrawingContext context,
        Rect plot,
        string text,
        bool compact)
    {
        var formatted = CreateText(
            text,
            compact ? 9 : 10,
            EmptyBrush,
            FontWeight.Normal,
            TextAlignment.Center,
            Math.Max(1, plot.Width - 20),
            24);
        context.DrawText(
            formatted,
            new Point(
                plot.Left + 10,
                Math.Max(plot.Top, plot.Center.Y - formatted.Height * 0.5)));
    }

    private static void DrawLabel(
        DrawingContext context,
        string text,
        Rect bounds,
        double fontSize,
        IBrush brush,
        TextAlignment alignment,
        FontWeight fontWeight = default)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        FormattedText formatted = CreateText(
            text,
            fontSize,
            brush,
            fontWeight,
            alignment,
            bounds.Width,
            bounds.Height);
        using (context.PushClip(bounds))
            context.DrawText(formatted, bounds.Position);
    }

    private static FormattedText CreateText(
        string text,
        double fontSize,
        IBrush brush,
        FontWeight fontWeight,
        TextAlignment alignment,
        double maximumWidth,
        double maximumHeight) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                Typeface.Default.FontFamily,
                FontStyle.Normal,
                fontWeight == default ? FontWeight.Normal : fontWeight),
            fontSize,
            brush)
        {
            MaxTextWidth = Math.Max(1, maximumWidth),
            MaxTextHeight = Math.Max(1, maximumHeight),
            MaxLineCount = 1,
            TextAlignment = alignment,
            Trimming = TextTrimming.CharacterEllipsis
        };

    private static string FormatAxisValue(double value)
    {
        double magnitude = Math.Abs(value);
        string format = magnitude >= 10_000 || magnitude is > 0 and < 0.01
            ? "0.##E+0"
            : "0.##";
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    private readonly record struct GraphPoint(
        double Distance,
        double Accuracy);

    private readonly record struct AxisRange(
        double Minimum,
        double Maximum,
        int TickCount);
}
