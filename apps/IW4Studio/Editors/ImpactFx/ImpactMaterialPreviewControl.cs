using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;

namespace IW4.Studio.Desktop.Editors.ImpactFx;

/// <summary>A shared material swatch and illustrative, interactive impact sample.</summary>
public sealed class ImpactMaterialPreviewControl : Control
{
    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<ImpactMaterialPreviewControl, int>(nameof(Slot));

    public static readonly StyledProperty<FxEffectDefAsset?> AssignedEffectProperty =
        AvaloniaProperty.Register<ImpactMaterialPreviewControl, FxEffectDefAsset?>(nameof(AssignedEffect));

    public static readonly StyledProperty<bool> IsSwatchProperty =
        AvaloniaProperty.Register<ImpactMaterialPreviewControl, bool>(nameof(IsSwatch));

    private DispatcherTimer? _clock;
    private readonly Stopwatch _elapsed = new();
    private Point _impact;
    private ImpactKind _impactKind;
    private static readonly IBrush[,] TextureBackgrounds = CreateTextureBackgrounds();

    static ImpactMaterialPreviewControl() =>
        AffectsRender<ImpactMaterialPreviewControl>(SlotProperty, AssignedEffectProperty, IsSwatchProperty);

    public ImpactMaterialPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        UpdateAccessibility();
    }

    /// <summary>Surface slots 0–30, followed by four separate character-hit variants.</summary>
    public int Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <summary>The resolved Fx assignment. Null also covers empty or unresolved references.</summary>
    public FxEffectDefAsset? AssignedEffect
    {
        get => GetValue(AssignedEffectProperty);
        set => SetValue(AssignedEffectProperty, value);
    }

    internal bool CanPreview => AssignedEffect?.ElemDefs.Any(element =>
        element.ElemType is >= FxElemType.SpriteBillboard and <= FxElemType.SpotLight or FxElemType.Decal) == true;

    /// <summary>Compact, decorative texture mode for use inside a material button.</summary>
    public bool IsSwatch
    {
        get => GetValue(IsSwatchProperty);
        set => SetValue(IsSwatchProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SlotProperty && change.Property != AssignedEffectProperty &&
            change.Property != IsSwatchProperty)
            return;

        _clock?.Stop();
        _elapsed.Reset();
        Focusable = !IsSwatch;
        IsHitTestVisible = !IsSwatch;
        UpdateAccessibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clock?.Stop();
        _elapsed.Reset();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsSwatch || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        Fire(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsSwatch || e.Key is not (Key.Enter or Key.Space))
            return;

        Fire(new Point(Bounds.Width / 2, Bounds.Height / 2));
        e.Handled = true;
    }

    private void Fire(Point at)
    {
        if (!CanPreview || AssignedEffect is not { } effect ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        _impactKind = Classify(effect);
        _impact = new Point(
            Math.Clamp(at.X, 12, Math.Max(12, Bounds.Width - 12)),
            Math.Clamp(at.Y, 12, Math.Max(12, Bounds.Height - 12)));
        _clock ??= CreateClock();
        _elapsed.Restart();
        _clock.Start();
        InvalidateVisual();
    }

    private DispatcherTimer CreateClock()
    {
        var clock = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000d / 30)
        };
        clock.Tick += (_, _) =>
        {
            if (_elapsed.ElapsedMilliseconds >= 680)
            {
                clock.Stop();
                _elapsed.Stop();
            }
            InvalidateVisual();
        };
        return clock;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect area = new(Bounds.Size);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        DrawTexture(context, area, SurfaceKind(Slot), dark);
        if (IsSwatch)
        {
            context.DrawRectangle(null, new Pen(Brush(dark ? 94 : 160, dark ? 180 : 80,
                dark ? 192 : 82, dark ? 182 : 77), 1), area.Deflate(.5));
            return;
        }

        Color guide = dark ? Color.FromArgb(145, 244, 249, 239) : Color.FromArgb(155, 28, 43, 35);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(guide), 1), area.Deflate(.5));
        if (area.Width > 40 && area.Height > 40)
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1),
                area.Deflate(11.5));

        Point center = _clock?.IsEnabled == true
            ? _impact : new Point(area.Width / 2, area.Height / 2);
        var target = new Pen(new SolidColorBrush(guide), 1.3);
        context.DrawEllipse(null, target, center, 22, 22);
        context.DrawLine(target, new Point(center.X - 8, center.Y), new Point(center.X + 8, center.Y));
        context.DrawLine(target, new Point(center.X, center.Y - 8), new Point(center.X, center.Y + 8));

        if (_clock?.IsEnabled == true)
            DrawImpact(context, _impact, _impactKind, _elapsed.Elapsed.TotalMilliseconds / 680d);
        if (IsFocused)
            context.DrawRectangle(null, new Pen(Brush(dark ? 120 : 8, dark ? 220 : 118,
                dark ? 188 : 102, 255), 2), area.Deflate(2));
    }

    private void UpdateAccessibility()
    {
        string name = Slot is >= 0 and < (int)MaterialSurfaceType.Count
            ? ((MaterialSurfaceType)Slot).ToString()
            : Slot is >= 31 and <= 34 ? $"Flesh variant {Slot - 31}" : $"Surface slot {Slot}";
        if (IsSwatch)
        {
            AutomationProperties.SetName(this, $"{name} material texture");
            return;
        }

        string assignment = CanPreview && AssignedEffect is { } fx
            ? $"assigned effect {fx.Name ?? "unnamed"}; press Enter or Space to preview an illustrative impact"
            : "no resolved effect to preview";
        AutomationProperties.SetName(this, $"{name} impact sample, {assignment}");
    }

    private static ImpactKind Classify(FxEffectDefAsset effect)
    {
        foreach (FxElemDef element in effect.ElemDefs)
        {
            if (element.ElemType is FxElemType.SparkCloud or FxElemType.SparkFountain)
                return ImpactKind.Spark;
            if (element.ElemType == FxElemType.Cloud)
                return ImpactKind.Cloud;
        }

        string name = effect.Name ?? "";
        if (name.Contains("spark", StringComparison.OrdinalIgnoreCase)) return ImpactKind.Spark;
        if (name.Contains("splinter", StringComparison.OrdinalIgnoreCase)) return ImpactKind.Splinter;
        if (name.Contains("dust", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("smoke", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mist", StringComparison.OrdinalIgnoreCase)) return ImpactKind.Cloud;
        return ImpactKind.Chip;
    }

    private static void DrawImpact(DrawingContext context, Point at, ImpactKind kind, double progress)
    {
        double fade = 1 - Math.Clamp(progress, 0, 1);
        Color color = kind switch
        {
            ImpactKind.Spark => Color.FromArgb((byte)(230 * fade), 255, 202, 111),
            ImpactKind.Splinter => Color.FromArgb((byte)(215 * fade), 220, 175, 117),
            ImpactKind.Cloud => Color.FromArgb((byte)(180 * fade), 217, 222, 205),
            _ => Color.FromArgb((byte)(210 * fade), 225, 221, 204)
        };
        var pen = new Pen(new SolidColorBrush(color), kind == ImpactKind.Spark ? 2.5 : 1.5);
        context.DrawEllipse(null, pen, at, 5 + 60 * progress, 5 + 60 * progress);

        int count = kind == ImpactKind.Cloud ? 10 : 8;
        for (int i = 0; i < count; i++)
        {
            double angle = Math.Tau * i / count + (i % 3) * .21;
            double radius = (kind == ImpactKind.Cloud ? 25 : 57) * progress * (0.7 + i % 3 * .15);
            Point end = new(at.X + Math.Cos(angle) * radius,
                at.Y + Math.Sin(angle) * radius);
            if (kind == ImpactKind.Cloud)
                context.DrawEllipse(new SolidColorBrush(color), null, end, 3 + 9 * progress, 3 + 9 * progress);
            else
            {
                double length = kind == ImpactKind.Spark ? 12 * fade : 5 * fade;
                Point start = new(end.X - Math.Cos(angle) * length,
                    end.Y - Math.Sin(angle) * length);
                context.DrawLine(pen, start, end);
            }
        }
    }

    private static SurfaceTexture SurfaceKind(int slot)
    {
        if (slot is >= 31 and <= 34)
            return SurfaceTexture.Flesh;
        return (MaterialSurfaceType)slot switch
        {
            MaterialSurfaceType.Bark => SurfaceTexture.Bark,
            MaterialSurfaceType.Wood => SurfaceTexture.Wood,
            MaterialSurfaceType.Brick => SurfaceTexture.Brick,
            MaterialSurfaceType.Carpet or MaterialSurfaceType.Cloth or MaterialSurfaceType.Cushion => SurfaceTexture.Fabric,
            MaterialSurfaceType.Concrete or MaterialSurfaceType.Plaster or MaterialSurfaceType.Rock or
                MaterialSurfaceType.Ceramic => SurfaceTexture.Stone,
            MaterialSurfaceType.Asphalt => SurfaceTexture.Asphalt,
            MaterialSurfaceType.Dirt or MaterialSurfaceType.Mud or MaterialSurfaceType.Sand => SurfaceTexture.Earth,
            MaterialSurfaceType.Gravel => SurfaceTexture.Gravel,
            MaterialSurfaceType.Slush => SurfaceTexture.Slush,
            MaterialSurfaceType.Flesh => SurfaceTexture.Flesh,
            MaterialSurfaceType.Foliage or MaterialSurfaceType.Grass or MaterialSurfaceType.Fruit => SurfaceTexture.Foliage,
            MaterialSurfaceType.Glass or MaterialSurfaceType.Ice => SurfaceTexture.Glass,
            MaterialSurfaceType.Metal => SurfaceTexture.Metal,
            MaterialSurfaceType.PaintedMetal => SurfaceTexture.PaintedMetal,
            MaterialSurfaceType.RiotShield => SurfaceTexture.Shield,
            MaterialSurfaceType.Paper => SurfaceTexture.Paper,
            MaterialSurfaceType.Snow => SurfaceTexture.Snow,
            MaterialSurfaceType.Water => SurfaceTexture.Water,
            MaterialSurfaceType.Plastic or MaterialSurfaceType.Rubber => SurfaceTexture.Polymer,
            _ => SurfaceTexture.Default
        };
    }

    private static void DrawTexture(DrawingContext context, Rect area, SurfaceTexture kind, bool dark)
    {
        context.DrawRectangle(TextureBackgrounds[dark ? 1 : 0, (int)kind], null, area);

        IBrush markingBrush = Brush(dark ? 230 : 31, dark ? 237 : 42,
            dark ? 229 : 36, kind == SurfaceTexture.Glass ? 75 : 46);
        var marking = new Pen(markingBrush, 1);
        if (kind == SurfaceTexture.Brick)
        {
            const double rowHeight = 27;
            const double brickWidth = 55;
            for (int row = 0; row * rowHeight < area.Height; row++)
            {
                double y = row * rowHeight;
                context.DrawLine(marking, new Point(0, y), new Point(area.Width, y));
                double offset = row % 2 == 0 ? 0 : brickWidth / 2;
                for (double x = offset; x < area.Width; x += brickWidth)
                    context.DrawLine(marking, new Point(x, y), new Point(x, Math.Min(y + rowHeight, area.Height)));
            }
        }
        else if (kind is SurfaceTexture.Wood or SurfaceTexture.Bark)
        {
            double spacing = kind == SurfaceTexture.Bark ? 14 : 18;
            double amplitude = kind == SurfaceTexture.Bark ? 3.5 : 2.5;
            for (double baseline = 7; baseline < area.Height + spacing; baseline += spacing)
            {
                double previousY = baseline;
                for (double x = 0; x < area.Width; x += 7)
                {
                    double nextX = Math.Min(x + 7, area.Width);
                    double nextY = baseline + Math.Sin(nextX / 22 + baseline * .18) * amplitude;
                    context.DrawLine(marking, new Point(x, previousY), new Point(nextX, nextY));
                    previousY = nextY;
                }
            }
            if (kind == SurfaceTexture.Wood && area.Width > 65 && area.Height > 35)
                context.DrawEllipse(null, marking, new Point(area.Width * .6, area.Height * .52), 17, 5);
        }
        else if (kind == SurfaceTexture.Fabric)
        {
            for (double x = 0; x < area.Width; x += 7)
                context.DrawLine(marking, new Point(x, 0), new Point(x, area.Height));
            for (double y = 0; y < area.Height; y += 7)
                context.DrawLine(marking, new Point(0, y), new Point(area.Width, y));
        }
        else if (kind is SurfaceTexture.Metal or SurfaceTexture.PaintedMetal or SurfaceTexture.Shield)
        {
            double spacing = kind == SurfaceTexture.Metal ? 6 : 11;
            for (double y = 3; y < area.Height; y += spacing)
                context.DrawLine(marking, new Point(0, y), new Point(area.Width, y - 2));
            if (kind == SurfaceTexture.Shield)
            {
                for (double x = 0; x < area.Width; x += 38)
                    context.DrawLine(marking, new Point(x, 0), new Point(x + 18, area.Height));
            }
            else if (kind == SurfaceTexture.PaintedMetal)
            {
                for (double x = 18; x < area.Width; x += 63)
                    context.DrawLine(marking, new Point(x, 0), new Point(x + 9, area.Height));
            }
        }
        else if (kind is SurfaceTexture.Stone or SurfaceTexture.Asphalt or SurfaceTexture.Earth or
            SurfaceTexture.Gravel or SurfaceTexture.Slush or SurfaceTexture.Snow or SurfaceTexture.Foliage)
        {
            double radius = kind switch
            {
                SurfaceTexture.Foliage => 6,
                SurfaceTexture.Gravel => 4,
                SurfaceTexture.Slush => 3,
                _ => 2
            };
            for (double y = 12; y < area.Height; y += 25)
                for (double x = 13 + ((int)y % 3) * 7; x < area.Width; x += 30)
                    context.DrawEllipse(markingBrush, null, new Point(x, y), radius,
                        kind == SurfaceTexture.Foliage ? 3.5 : radius);
        }
        else if (kind == SurfaceTexture.Water)
        {
            for (double y = 20; y < area.Height; y += 32)
                context.DrawEllipse(null, marking, new Point(area.Width * .42, y), 35, 10);
        }
        else if (kind == SurfaceTexture.Glass)
        {
            var glint = new Pen(Brush(255, 255, 255, 65), 5);
            for (double x = 0; x < area.Width; x += 55)
                context.DrawLine(glint, new Point(x, 0), new Point(x + 45, area.Height));
        }
    }

    private static IBrush[,] CreateTextureBackgrounds()
    {
        var backgrounds = new IBrush[2, Enum.GetValues<SurfaceTexture>().Length];
        foreach (SurfaceTexture kind in Enum.GetValues<SurfaceTexture>())
        {
            backgrounds[0, (int)kind] = CreateTextureBackground(kind, false);
            backgrounds[1, (int)kind] = CreateTextureBackground(kind, true);
        }
        return backgrounds;
    }

    private static IBrush CreateTextureBackground(SurfaceTexture kind, bool dark)
    {
        (Color light, Color shadow) = kind switch
        {
            SurfaceTexture.Bark => (Rgb(140, 106, 77), Rgb(78, 58, 44)),
            SurfaceTexture.Wood => (Rgb(187, 147, 111), Rgb(112, 79, 57)),
            SurfaceTexture.Brick => (Rgb(184, 117, 97), Rgb(111, 71, 62)),
            SurfaceTexture.Fabric => (Rgb(158, 171, 161), Rgb(89, 108, 96)),
            SurfaceTexture.Stone => (Rgb(176, 181, 171), Rgb(92, 102, 94)),
            SurfaceTexture.Asphalt => (Rgb(107, 113, 108), Rgb(55, 66, 61)),
            SurfaceTexture.Earth => (Rgb(152, 121, 98), Rgb(91, 77, 63)),
            SurfaceTexture.Gravel => (Rgb(168, 169, 157), Rgb(83, 98, 83)),
            SurfaceTexture.Slush => (Rgb(164, 178, 170), Rgb(84, 112, 106)),
            SurfaceTexture.Flesh => (Rgb(184, 141, 131), Rgb(118, 84, 81)),
            SurfaceTexture.Foliage => (Rgb(131, 160, 104), Rgb(58, 96, 63)),
            SurfaceTexture.Glass => (Rgb(157, 196, 197), Rgb(59, 111, 120)),
            SurfaceTexture.Metal => (Rgb(177, 194, 195), Rgb(69, 93, 101)),
            SurfaceTexture.PaintedMetal => (Rgb(141, 175, 170), Rgb(50, 83, 88)),
            SurfaceTexture.Shield => (Rgb(129, 145, 148), Rgb(39, 59, 66)),
            SurfaceTexture.Paper => (Rgb(226, 219, 199), Rgb(141, 135, 118)),
            SurfaceTexture.Snow => (Rgb(218, 229, 223), Rgb(131, 151, 151)),
            SurfaceTexture.Water => (Rgb(110, 171, 178), Rgb(48, 105, 120)),
            SurfaceTexture.Polymer => (Rgb(137, 149, 154), Rgb(71, 82, 86)),
            _ => (Rgb(209, 214, 204), Rgb(76, 90, 81))
        };
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(dark ? shadow : light, 0),
                new GradientStop(dark ? Dim(light, .72) : Dim(light, .77), 1)
            ]
        };
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Dim(Color color, double factor) =>
        Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
    private static IBrush Brush(int r, int g, int b, int alpha) =>
        new SolidColorBrush(Color.FromArgb((byte)alpha, (byte)r, (byte)g, (byte)b));

    private enum ImpactKind { Spark, Cloud, Splinter, Chip }
    private enum SurfaceTexture
    {
        Default, Bark, Wood, Brick, Fabric, Stone, Asphalt, Earth, Gravel, Slush,
        Flesh, Foliage, Glass, Metal, PaintedMetal, Shield, Paper, Snow, Water, Polymer
    }
}
