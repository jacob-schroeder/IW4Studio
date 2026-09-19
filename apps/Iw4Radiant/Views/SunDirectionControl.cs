using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Iw4Radiant.Views;

public sealed class SunDirectionControl : Control
{
    private IPointer? _pointer;
    private Point _last;
    internal float Pitch { get; private set; } = -45;
    internal float Yaw { get; private set; }
    internal event Action? DirectionChanged;

    public SunDirectionControl()
    {
        Focusable = true;
        ClipToBounds = true;
        PointerPressed += (_, e) =>
        {
            if (_pointer is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            _pointer = e.Pointer;
            _last = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(_pointer, e.Pointer)) return;
            UpdateDrag(e.GetPosition(this));
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_pointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left) return;
            UpdateDrag(e.GetPosition(this));
            _pointer = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => _pointer = null;
        KeyDown += (_, e) =>
        {
            float step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 15 : 1;
            switch (e.Key)
            {
                case Key.Left: SetDirection(Pitch, Yaw - step); break;
                case Key.Right: SetDirection(Pitch, Yaw + step); break;
                case Key.Up: SetDirection(Math.Max(-90, Pitch - step), Yaw); break;
                case Key.Down: SetDirection(Math.Min(90, Pitch + step), Yaw); break;
                default: return;
            }
            DirectionChanged?.Invoke();
            e.Handled = true;
        };
    }

    private void UpdateDrag(Point point)
    {
        if (point == _last) return;
        var delta = point - _last;
        _last = point;
        SetDirection(Math.Clamp(Pitch + (float)delta.Y, -90, 90), Yaw + (float)delta.X);
        DirectionChanged?.Invoke();
    }

    internal void SetDirection(float pitch, float yaw)
    {
        Pitch = pitch;
        Yaw = yaw % 360;
        if (Yaw < 0) Yaw += 360;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var background = new SolidColorBrush(Color.Parse("#1B1D21"));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#454A52")));
        var sun = new SolidColorBrush(Color.Parse("#E5B477"));
        context.DrawRectangle(background, grid, new Rect(Bounds.Size), 2, 2);
        var center = new Point(44, Bounds.Height / 2);
        const double radius = 29;
        context.DrawEllipse(null, grid, center, radius, radius);
        context.DrawLine(grid, center - new Vector(radius, 0), center + new Vector(radius, 0));
        context.DrawLine(grid, center - new Vector(0, radius), center + new Vector(0, radius));
        double radians = Yaw * Math.PI / 180;
        var end = center + new Vector(Math.Cos(radians) * radius, -Math.Sin(radians) * radius);
        context.DrawLine(new Pen(sun, 2), center, end);
        context.DrawEllipse(sun, null, end, 4, 4);
        var text = new FormattedText(FormattableString.Invariant($"Yaw {Yaw:0.#}°\nElevation {-Pitch:0.#}°"),
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.LightGray);
        context.DrawText(text, new Point(87, Math.Max(4, (Bounds.Height - text.Height) / 2)));
    }
}
