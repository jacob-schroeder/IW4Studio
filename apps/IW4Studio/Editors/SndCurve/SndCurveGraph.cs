using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using IW4.Game.Assets.Sound;

namespace IW4.Studio.Desktop.Editors.SndCurve;

public enum SndCurveGraphEditPhase { Begin, Update, Commit, Cancel }

public readonly record struct SndCurveGraphEdit(
    int Index,
    SndCurveKnot Knot,
    SndCurveGraphEditPhase Phase);

/// <summary>Interactive normalized distance and volume plot for active sound curve knots.</summary>
public sealed class SndCurveGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<SndCurveKnot>?> PointsProperty =
        AvaloniaProperty.Register<SndCurveGraph, IReadOnlyList<SndCurveKnot>?>(nameof(Points));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SndCurveGraph, int>(nameof(SelectedIndex),
            defaultValue: -1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ListenerDistanceProperty =
        AvaloniaProperty.Register<SndCurveGraph, double>(nameof(ListenerDistance));

    public static readonly StyledProperty<double> ListenerVolumeProperty =
        AvaloniaProperty.Register<SndCurveGraph, double>(nameof(ListenerVolume));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<SndCurveGraph, bool>(nameof(IsReadOnly));

    private const double Left = 48;
    private const double Top = 30;
    private const double Right = 20;
    private const double Bottom = 50;
    private const double HitRadius = 17;

    private static readonly Palette Light = new(
        Color.FromRgb(243, 245, 241), Color.FromRgb(255, 255, 255),
        Color.FromRgb(217, 223, 216), Color.FromRgb(88, 101, 93),
        Color.FromRgb(8, 118, 102), Color.FromArgb(25, 8, 118, 102));
    private static readonly Palette Dark = new(
        Color.FromRgb(48, 54, 50), Color.FromRgb(40, 44, 41),
        Color.FromRgb(72, 81, 73), Color.FromRgb(185, 197, 187),
        Color.FromRgb(120, 214, 187), Color.FromArgb(30, 120, 214, 187));

    private IPointer? _dragPointer;
    private int _dragIndex = -1;
    private SndCurveKnot _startKnot;
    private SndCurveKnot _previewKnot;
    private Point _pressLocation;
    private int _hoverIndex = -1;

    static SndCurveGraph() =>
        AffectsRender<SndCurveGraph>(PointsProperty, SelectedIndexProperty,
            ListenerDistanceProperty, ListenerVolumeProperty, IsReadOnlyProperty);

    public SndCurveGraph()
    {
        ClipToBounds = true;
        Focusable = true;
        AutomationProperties.SetName(this, "Sound falloff curve");
        AutomationProperties.SetHelpText(this,
            "Tab to the graph. Use brackets to select a point, arrow keys to adjust it, or drag a point.");
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<SndCurveKnot>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double ListenerDistance
    {
        get => GetValue(ListenerDistanceProperty);
        set => SetValue(ListenerDistanceProperty, value);
    }

    public double ListenerVolume
    {
        get => GetValue(ListenerVolumeProperty);
        set => SetValue(ListenerVolumeProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public event EventHandler<SndCurveGraphEdit>? EditRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadOnlyProperty && IsReadOnly && _dragPointer is not null)
            EndDrag(true);
        if (change.Property != PointsProperty &&
            change.Property != SelectedIndexProperty &&
            change.Property != IsReadOnlyProperty)
            return;

        IReadOnlyList<SndCurveKnot> points = Points ?? [];
        string description = "Sound falloff curve";
        if (SelectedIndex >= 0 && SelectedIndex < points.Count)
        {
            SndCurveKnot selected = points[SelectedIndex];
            description += $", point {SelectedIndex + 1} of {points.Count}, " +
                $"{selected.X:P0} distance, {selected.Y:P0} volume";
        }
        if (IsReadOnly)
            description += ", read only";
        AutomationProperties.SetName(this, description);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? Math.Min(600, availableSize.Width) : 600,
        double.IsFinite(availableSize.Height) ? Math.Min(325, availableSize.Height) : 325);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        Palette colors = ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;
        context.DrawRectangle(colors.Stage, null, bounds);
        Rect plot = PlotRect(bounds);
        if (plot.Width < 24 || plot.Height < 24)
            return;

        DrawGrid(context, plot, colors);
        IReadOnlyList<SndCurveKnot> points = Points ?? [];
        if (points.Count == 0)
        {
            DrawLabel(context, "No active points", new Point(plot.Left + 10, plot.Center.Y - 8),
                plot.Width - 20, 12, colors.Label);
            return;
        }

        DrawCurve(context, plot, points, colors);
        DrawListener(context, plot, colors);
        DrawKnots(context, plot, points, colors);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_dragPointer is not null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        IReadOnlyList<SndCurveKnot> points = Points ?? [];
        int index = HitTest(e.GetPosition(this), points);
        if (index < 0)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        SetCurrentValue(SelectedIndexProperty, index);
        if (!IsReadOnly)
        {
            _dragIndex = index;
            _startKnot = points[index];
            _previewKnot = _startKnot;
            _pressLocation = e.GetPosition(this);
            _dragPointer = e.Pointer;
            EditRequested?.Invoke(this, new(index, _startKnot, SndCurveGraphEditPhase.Begin));
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragPointer == e.Pointer && _dragIndex >= 0)
        {
            UpdateDrag(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        int hover = HitTest(e.GetPosition(this), Points ?? []);
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Cursor = hover >= 0 ? new Cursor(StandardCursorType.Hand) : null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragPointer != e.Pointer)
            return;
        UpdateDrag(e.GetPosition(this));
        EndDrag(false);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragPointer is not null)
            EndDrag(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _dragPointer is not null)
        {
            EndDrag(true);
            e.Handled = true;
            return;
        }

        IReadOnlyList<SndCurveKnot> points = Points ?? [];
        if (points.Count == 0)
            return;

        int selected = Math.Clamp(SelectedIndex, 0, points.Count - 1);
        int nextSelection = e.Key switch
        {
            Key.OemOpenBrackets => Math.Max(0, selected - 1),
            Key.OemCloseBrackets => Math.Min(points.Count - 1, selected + 1),
            Key.Home => 0,
            Key.End => points.Count - 1,
            _ => -1
        };
        if (nextSelection >= 0)
        {
            SetCurrentValue(SelectedIndexProperty, nextSelection);
            e.Handled = true;
            return;
        }

        if (IsReadOnly || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;
        float step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.05f : 0.01f;
        SndCurveKnot before = points[selected];
        float x = before.X;
        float y = before.Y;
        switch (e.Key)
        {
            case Key.Left: x -= step; break;
            case Key.Right: x += step; break;
            case Key.Up: y += step; break;
            case Key.Down: y -= step; break;
        }
        SndCurveKnot next = Constrain(selected, x, y, points);
        if (next != before)
        {
            SetCurrentValue(SelectedIndexProperty, selected);
            EditRequested?.Invoke(this, new(selected, before, SndCurveGraphEditPhase.Begin));
            EditRequested?.Invoke(this, new(selected, next, SndCurveGraphEditPhase.Update));
            EditRequested?.Invoke(this, new(selected, next, SndCurveGraphEditPhase.Commit));
        }
        e.Handled = true;
    }

    private void EndDrag(bool cancel)
    {
        IPointer? pointer = _dragPointer;
        int index = _dragIndex;
        SndCurveKnot knot = cancel ? _startKnot : _previewKnot;
        _dragPointer = null;
        _dragIndex = -1;
        if (pointer?.Captured == this)
            pointer.Capture(null);
        EditRequested?.Invoke(this, new(index, knot,
            cancel || knot == _startKnot ? SndCurveGraphEditPhase.Cancel : SndCurveGraphEditPhase.Commit));
        InvalidateVisual();
    }

    private void UpdateDrag(Point position)
    {
        IReadOnlyList<SndCurveKnot> points = Points ?? [];
        Rect plot = PlotRect(new Rect(Bounds.Size));
        if (_dragIndex < 0 || _dragIndex >= points.Count || plot.Width <= 0 || plot.Height <= 0)
            return;
        Vector delta = position - _pressLocation;
        float x = _startKnot.X + (float)(delta.X / plot.Width);
        float y = _startKnot.Y - (float)(delta.Y / plot.Height);
        SndCurveKnot next = Constrain(_dragIndex, x, y, points);
        if (next == _previewKnot) return;
        _previewKnot = next;
        EditRequested?.Invoke(this, new(_dragIndex, next, SndCurveGraphEditPhase.Update));
        InvalidateVisual();
    }

    private static SndCurveKnot Constrain(int index, float x, float y,
        IReadOnlyList<SndCurveKnot> points)
    {
        SndCurveKnot source = points[index];
        x = float.IsFinite(x) ? Math.Clamp(x, 0, 1) : source.X;
        y = float.IsFinite(y) ? Math.Clamp(y, 0, 1) : source.Y;
        if (index == 0 || index == points.Count - 1)
            x = source.X;
        else
        {
            float minimum = MathF.BitIncrement(points[index - 1].X);
            float maximum = MathF.BitDecrement(points[index + 1].X);
            x = minimum <= maximum ? Math.Clamp(x, minimum, maximum) : source.X;
        }
        return new SndCurveKnot(x, y);
    }

    private static Rect PlotRect(Rect bounds)
    {
        bool compact = bounds.Width < 320 || bounds.Height < 190;
        double left = compact ? 38 : Left;
        double top = compact ? 23 : Top;
        double right = compact ? 14 : Right;
        double bottom = compact ? 42 : Bottom;
        return new Rect(left, top,
            Math.Max(0, bounds.Width - left - right),
            Math.Max(0, bounds.Height - top - bottom));
    }

    private static Point Project(SndCurveKnot knot, Rect plot) => new(
        plot.Left + Math.Clamp(knot.X, 0, 1) * plot.Width,
        plot.Bottom - Math.Clamp(knot.Y, 0, 1) * plot.Height);

    private int HitTest(Point position, IReadOnlyList<SndCurveKnot> points)
    {
        Rect plot = PlotRect(new Rect(Bounds.Size));
        int result = -1;
        double distance = HitRadius * HitRadius;
        for (int i = 0; i < points.Count; i++)
        {
            if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                continue;
            Point p = Project(i == _dragIndex ? _previewKnot : points[i], plot);
            double squared = Math.Pow(position.X - p.X, 2) + Math.Pow(position.Y - p.Y, 2);
            if (squared <= distance)
            {
                distance = squared;
                result = i;
            }
        }
        return result;
    }

    private static void DrawGrid(DrawingContext context, Rect plot, Palette colors)
    {
        DrawLabel(context, "Volume", new Point(plot.Left, plot.Top - 20), 90, 12, colors.Label);
        DrawLabel(context, "Distance", new Point(plot.Center.X - 50, plot.Bottom + 25),
            100, 12, colors.Label, TextAlignment.Center);
        int divisions = plot.Width < 270 ? 2 : 4;
        for (int i = 0; i <= divisions; i++)
        {
            double fraction = i / (double)divisions;
            double x = plot.Left + plot.Width * fraction;
            double y = plot.Bottom - plot.Height * fraction;
            context.DrawLine(i == 0 ? colors.AxisPen : colors.GridPen,
                new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(i == 0 ? colors.AxisPen : colors.GridPen,
                new Point(plot.Left, y), new Point(plot.Right, y));
            DrawLabel(context, $"{fraction:P0}", new Point(x - 21, plot.Bottom + 8),
                42, 11, colors.Label, TextAlignment.Center);
            if (i % (divisions / 2) == 0)
                DrawLabel(context, $"{fraction:P0}", new Point(2, y - 7),
                    plot.Left - 9, 11, colors.Label, TextAlignment.Right);
        }
    }

    private void DrawCurve(DrawingContext context, Rect plot,
        IReadOnlyList<SndCurveKnot> points, Palette colors)
    {
        var projected = new List<Point>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            SndCurveKnot knot = i == _dragIndex ? _previewKnot : points[i];
            if (!float.IsFinite(knot.X) || !float.IsFinite(knot.Y))
                return;
            projected.Add(Project(knot, plot));
        }
        using (context.PushClip(plot))
        {
            var fill = new StreamGeometry();
            using (StreamGeometryContext path = fill.Open())
            {
                path.BeginFigure(new Point(projected[0].X, plot.Bottom), true);
                foreach (Point point in projected)
                    path.LineTo(point);
                path.LineTo(new Point(projected[^1].X, plot.Bottom));
                path.EndFigure(true);
            }
            context.DrawGeometry(colors.Fill, null, fill);
            for (int i = 1; i < projected.Count; i++)
                context.DrawLine(colors.CurvePen, projected[i - 1], projected[i]);
        }
    }

    private void DrawListener(DrawingContext context, Rect plot, Palette colors)
    {
        if (!double.IsFinite(ListenerDistance) || !double.IsFinite(ListenerVolume))
            return;
        double x = plot.Left + Math.Clamp(ListenerDistance, 0, 1) * plot.Width;
        double y = plot.Bottom - Math.Clamp(ListenerVolume, 0, 1) * plot.Height;
        context.DrawLine(colors.ListenerPen, new Point(x, plot.Bottom), new Point(x, y));
        context.DrawEllipse(colors.Accent, new Pen(colors.Stage, 2.5), new Point(x, y), 6, 6);
    }

    private void DrawKnots(DrawingContext context, Rect plot,
        IReadOnlyList<SndCurveKnot> points, Palette colors)
    {
        for (int i = 0; i < points.Count; i++)
        {
            SndCurveKnot knot = i == _dragIndex ? _previewKnot : points[i];
            if (!float.IsFinite(knot.X) || !float.IsFinite(knot.Y))
                continue;
            Point point = Project(knot, plot);
            bool selected = i == SelectedIndex;
            double radius = selected ? 8.5 : 7;
            context.DrawEllipse(selected ? colors.Accent : colors.Panel,
                new Pen(colors.Accent, selected ? 3 : 2.5), point, radius, radius);
            if (selected)
            {
                context.DrawEllipse(null, new Pen(colors.Panel, 2.5), point, 11.5, 11.5);
                if (IsFocused)
                    context.DrawEllipse(null, new Pen(colors.Accent, 1.4), point, 14, 14);
            }
        }
    }

    private static void DrawLabel(DrawingContext context, string text, Point at,
        double width, double size, IBrush brush,
        TextAlignment alignment = TextAlignment.Left)
    {
        if (width <= 0)
            return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, brush)
        {
            MaxTextWidth = width,
            MaxTextHeight = size + 5,
            MaxLineCount = 1,
            TextAlignment = alignment
        };
        context.DrawText(formatted, at);
    }

    private sealed class Palette
    {
        public Palette(Color stage, Color panel, Color grid, Color label,
            Color accent, Color fill)
        {
            Stage = new SolidColorBrush(stage);
            Panel = new SolidColorBrush(panel);
            Label = new SolidColorBrush(label);
            Accent = new SolidColorBrush(accent);
            Fill = new SolidColorBrush(fill);
            GridPen = new Pen(new SolidColorBrush(grid), 1);
            AxisPen = new Pen(Label, 1);
            CurvePen = new Pen(Accent, 3);
            ListenerPen = new Pen(Accent, 1.5, DashStyle.Dash);
        }

        public IBrush Stage { get; }
        public IBrush Panel { get; }
        public IBrush Label { get; }
        public IBrush Accent { get; }
        public IBrush Fill { get; }
        public Pen GridPen { get; }
        public Pen AxisPen { get; }
        public Pen CurvePen { get; }
        public Pen ListenerPen { get; }
    }
}
