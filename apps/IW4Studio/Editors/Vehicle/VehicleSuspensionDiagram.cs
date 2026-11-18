using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Vehicle;

public sealed class VehicleSuspensionDiagram : Control
{
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<VehicleSuspensionDiagram, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<IBrush?> GuideProperty =
        AvaloniaProperty.Register<VehicleSuspensionDiagram, IBrush?>(nameof(Guide));

    public static readonly StyledProperty<IBrush?> LabelProperty =
        AvaloniaProperty.Register<VehicleSuspensionDiagram, IBrush?>(nameof(Label));

    private (double? Front, double? Rear) _draft;
    private (double? Front, double? Rear) _saved;
    private bool _compareSaved;

    static VehicleSuspensionDiagram() =>
        AffectsRender<VehicleSuspensionDiagram>(AccentProperty, GuideProperty, LabelProperty);

    public VehicleSuspensionDiagram() => ClipToBounds = true;

    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public IBrush? Guide { get => GetValue(GuideProperty); set => SetValue(GuideProperty, value); }

    public IBrush? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    internal void ShowTravel(double? front, double? rear, double? savedFront, double? savedRear, bool compareSaved)
    {
        _draft = (front, rear);
        _saved = (savedFront, savedRear);
        _compareSaved = compareSaved;
        string summary = $"Suspension travel. Draft front {Format(front)}, rear {Format(rear)}.";
        if (compareSaved)
            summary += $" Saved front {Format(savedFront)}, rear {Format(savedRear)}.";
        AutomationProperties.SetName(this, summary);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 100 || height < 160)
            return;

        IBrush accent = Accent ?? Brushes.YellowGreen;
        IBrush guides = Guide ?? Brushes.DimGray;
        IBrush labels = Label ?? Brushes.LightGray;
        var guidePen = new Pen(guides, 1);
        for (double x = width / 2 % 24; x < width; x += 24)
            context.DrawLine(guidePen, new Point(x, 0), new Point(x, height));
        for (double y = height / 2 % 24; y < height; y += 24)
            context.DrawLine(guidePen, new Point(0, y), new Point(width, y));

        double left = width * 0.26;
        double right = width * 0.74;
        double zero = height * 0.58;
        double axleRadius = Math.Min(width * 0.09, height * 0.13);
        var axle = new Pen(labels, 1.2);
        context.DrawLine(axle, new Point(left - axleRadius, zero), new Point(right + axleRadius, zero));
        DrawWheel(context, axle, left, zero, axleRadius);
        DrawWheel(context, axle, right, zero, axleRadius);

        double largest = Math.Max(Magnitude(_draft), _compareSaved ? Magnitude(_saved) : 0);
        double span = Math.Min(zero - 22, height - zero - 55) * 0.83;
        var current = new Pen(accent, 2);
        var saved = new Pen(labels, 1.5, DashStyle.Dash);
        DrawPair(left, "Front", _draft.Front, _saved.Front);
        DrawPair(right, "Rear", _draft.Rear, _saved.Rear);

        void DrawPair(double x, string name, double? value, double? previous)
        {
            if (_compareSaved && previous is { } savedValue)
                DrawTravel(context, saved, savedValue, x + 11, zero, largest, span);
            if (value is { } currentValue)
                DrawTravel(context, current, currentValue, x, zero, largest, span);

            double captionY = height - (_compareSaved ? 51 : 36);
            DrawCaption(context, labels, name, x, captionY, 12);
            DrawCaption(context, accent, Format(value), x, captionY + 15, 11);
            if (_compareSaved)
                DrawCaption(context, labels, Format(previous), x, captionY + 30, 10);
        }
    }

    private static double Magnitude((double? Front, double? Rear) values) =>
        Math.Max(Math.Abs(values.Front ?? 0), Math.Abs(values.Rear ?? 0));

    private static string Format(double? value) => value?.ToString("G6", CultureInfo.InvariantCulture) ?? "—";

    private static void DrawWheel(DrawingContext context, Pen pen, double x, double y, double radius)
    {
        context.DrawEllipse(null, pen, new Point(x, y), radius, radius);
        context.DrawEllipse(null, pen, new Point(x, y), radius * 0.63, radius * 0.63);
    }

    private static void DrawTravel(
        DrawingContext context, Pen pen, double value, double x, double zero, double largest, double span)
    {
        double end = largest > 0 ? zero - value / largest * span : zero;
        context.DrawLine(pen, new Point(x, zero), new Point(x, end));
        context.DrawLine(pen, new Point(x - 4, zero), new Point(x + 4, zero));
        context.DrawLine(pen, new Point(x - 4, end), new Point(x + 4, end));
    }

    private static void DrawCaption(DrawingContext context, IBrush brush, string value, double x, double y, int size)
    {
        var text = new FormattedText(
            value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(text, new Point(x - text.Width / 2, y));
    }
}
