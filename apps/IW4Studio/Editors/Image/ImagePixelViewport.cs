using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed class ImagePixelViewport : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImagePixelViewport, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<double> MagnificationProperty =
        AvaloniaProperty.Register<ImagePixelViewport, double>(nameof(Magnification));
    public static readonly StyledProperty<IBrush?> TransparencyBrushProperty =
        AvaloniaProperty.Register<ImagePixelViewport, IBrush?>(nameof(TransparencyBrush));

    private Point? _lastPointer;
    private Vector _pan;

    static ImagePixelViewport() => AffectsRender<ImagePixelViewport>(
        SourceProperty, MagnificationProperty, TransparencyBrushProperty);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // A zero magnification fits both axes inside the viewport.
    public double Magnification
    {
        get => GetValue(MagnificationProperty);
        set => SetValue(MagnificationProperty, value);
    }
    public IBrush? TransparencyBrush
    {
        get => GetValue(TransparencyBrushProperty);
        set => SetValue(TransparencyBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        double scale = Magnification > 0 ? Magnification :
            Math.Min(Bounds.Width / source.PixelSize.Width, Bounds.Height / source.PixelSize.Height);
        var size = new Size(source.PixelSize.Width * scale, source.PixelSize.Height * scale);
        _pan = new Vector(ClampPan(_pan.X, size.Width, Bounds.Width), ClampPan(_pan.Y, size.Height, Bounds.Height));
        var rect = new Rect((Bounds.Width - size.Width) / 2 + _pan.X,
            (Bounds.Height - size.Height) / 2 + _pan.Y, size.Width, size.Height);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            context.DrawRectangle(TransparencyBrush, null, rect);
            context.DrawImage(source, rect);
        }
    }

    private static double ClampPan(double value, double extent, double viewport)
    {
        double limit = Math.Max(0, (extent - viewport) / 2);
        return Math.Clamp(value, -limit, limit);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Magnification > 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPointer is { } previous)
        {
            Point next = e.GetPosition(this);
            _pan += next - previous;
            _lastPointer = next;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_lastPointer.HasValue)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _lastPointer = null;
    }
}
