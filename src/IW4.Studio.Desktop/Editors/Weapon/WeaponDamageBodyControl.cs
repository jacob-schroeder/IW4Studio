using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Assets.Assets.Weapon;

namespace IW4.Studio.Desktop.Editors.Weapon;

public sealed class WeaponDamageBodyControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<float>?> MultipliersProperty =
        AvaloniaProperty.Register<WeaponDamageBodyControl, IReadOnlyList<float>?>(
            nameof(Multipliers));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WeaponDamageBodyControl, int>(
            nameof(SelectedIndex),
            defaultValue: (int)HitLocation.None,
            defaultBindingMode: BindingMode.TwoWay);

    private const double DesiredWidth = 280;
    private const double DesiredHeight = 400;
    private const double FigureWidth = 144;
    private const double FigureHeight = 312;

    private static readonly IBrush PanelBrush =
        new SolidColorBrush(Color.FromRgb(24, 27, 32));
    private static readonly IPen PanelBorderPen =
        new Pen(new SolidColorBrush(Color.FromRgb(55, 60, 68)), 1);
    private static readonly IBrush TitleBrush =
        new SolidColorBrush(Color.FromRgb(223, 228, 234));
    private static readonly IBrush DetailBrush =
        new SolidColorBrush(Color.FromRgb(157, 165, 176));
    private static readonly IBrush MissingValueBrush =
        new SolidColorBrush(Color.FromRgb(56, 61, 69));
    private static readonly IBrush BadgeIconBrush =
        new SolidColorBrush(Color.FromRgb(213, 220, 228));
    private static readonly IPen RegionBorderPen =
        new Pen(new SolidColorBrush(Color.FromRgb(94, 101, 111)), 1.15);
    private static readonly IPen HoverBorderPen =
        new Pen(new SolidColorBrush(Color.FromRgb(182, 198, 166)), 1.8);
    private static readonly IPen SelectedBorderPen =
        new Pen(new SolidColorBrush(Color.FromRgb(143, 211, 75)), 2.4);
    private static readonly Cursor HandCursor =
        new(StandardCursorType.Hand);

    private IReadOnlyList<HitRegion> _hitRegions = [];
    private int _hoveredIndex = -1;

    static WeaponDamageBodyControl() =>
        AffectsRender<WeaponDamageBodyControl>(
            MultipliersProperty,
            SelectedIndexProperty);

    public WeaponDamageBodyControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public IReadOnlyList<float>? Multipliers
    {
        get => GetValue(MultipliersProperty);
        set => SetValue(MultipliersProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? Math.Min(DesiredWidth, availableSize.Width)
            : DesiredWidth;
        double height = double.IsFinite(availableSize.Height)
            ? Math.Min(DesiredHeight, availableSize.Height)
            : DesiredHeight;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(PanelBrush, bounds, 12);
        context.DrawRectangle(PanelBorderPen, bounds, 12);

        if (bounds.Width < 80 || bounds.Height < 120)
        {
            _hitRegions = [];
            return;
        }

        double padding = Math.Clamp(bounds.Width * 0.055, 8, 16);
        double headerHeight = bounds.Height >= 250 ? 38 : 22;
        double footerHeight = bounds.Height >= 220 ? 44 : 34;
        DrawHeader(context, new Rect(
            padding,
            8,
            bounds.Width - padding * 2,
            headerHeight - 8));

        double availableFigureHeight =
            bounds.Height - headerHeight - footerHeight - 12;
        double scale = Math.Min(
            Math.Max(0, bounds.Width - padding * 2) / FigureWidth,
            Math.Max(0, availableFigureHeight) / FigureHeight);
        scale = Math.Max(0.1, scale);

        var origin = new Point(
            bounds.Center.X,
            headerHeight +
            Math.Max(0, (availableFigureHeight - FigureHeight * scale) * 0.5));
        var regions = new List<HitRegion>((int)HitLocation.Count);
        DrawFigure(context, regions, origin, scale);
        DrawAffordances(
            context,
            regions,
            new Rect(
                padding,
                bounds.Height - footerHeight,
                bounds.Width - padding * 2,
                footerHeight - 8));
        _hitRegions = regions;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        int index = HitTest(e.GetPosition(this));
        if (index < 0)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        SetCurrentValue(SelectedIndexProperty, index);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int index = HitTest(e.GetPosition(this));
        if (_hoveredIndex == index)
            return;

        _hoveredIndex = index;
        Cursor = index >= 0 ? HandCursor : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredIndex < 0)
            return;

        _hoveredIndex = -1;
        Cursor = null;
        InvalidateVisual();
    }

    private void DrawHeader(DrawingContext context, Rect bounds)
    {
        if (bounds.Height < 18)
            return;

        DrawText(
            context,
            "HIT LOCATION",
            new Rect(bounds.X, bounds.Y + 2, bounds.Width * 0.38, 16),
            9,
            DetailBrush,
            FontWeight.SemiBold);

        if (!IsLocationIndex(SelectedIndex))
            return;

        string value = FormatMultiplier(GetMultiplier(SelectedIndex));
        DrawText(
            context,
            $"{FormatLocation(SelectedIndex)}  {value}",
            new Rect(
                bounds.X + bounds.Width * 0.38,
                bounds.Y,
                bounds.Width * 0.62,
                19),
            10,
            TitleBrush,
            FontWeight.SemiBold);
    }

    private void DrawFigure(
        DrawingContext context,
        List<HitRegion> regions,
        Point origin,
        double scale)
    {
        // A front-facing figure presents the subject's right side on the viewer's left.
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightUpperArm,
            origin,
            scale,
            4,
            (-29, 74), (-39, 72), (-47, 79), (-51, 101),
            (-57, 132), (-45, 136), (-38, 110), (-29, 90));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftUpperArm,
            origin,
            scale,
            4,
            (29, 74), (39, 72), (47, 79), (51, 101),
            (57, 132), (45, 136), (38, 110), (29, 90));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightLowerArm,
            origin,
            scale,
            3.5,
            (-57, 130), (-45, 133), (-47, 153), (-53, 183),
            (-64, 181), (-61, 156));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftLowerArm,
            origin,
            scale,
            3.5,
            (57, 130), (45, 133), (47, 153), (53, 183),
            (64, 181), (61, 156));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightHand,
            origin,
            scale,
            3,
            (-64, 178), (-53, 180), (-51, 187), (-52, 197),
            (-56, 205), (-61, 205), (-66, 197), (-67, 188));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftHand,
            origin,
            scale,
            3,
            (64, 178), (53, 180), (51, 187), (52, 197),
            (56, 205), (61, 205), (66, 197), (67, 188));

        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightUpperLeg,
            origin,
            scale,
            4,
            (-22, 168), (-1, 168), (-3, 192), (-5, 215),
            (-7, 240), (-20, 240), (-24, 218), (-26, 190));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftUpperLeg,
            origin,
            scale,
            4,
            (22, 168), (1, 168), (3, 192), (5, 215),
            (7, 240), (20, 240), (24, 218), (26, 190));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightLowerLeg,
            origin,
            scale,
            3.5,
            (-20, 237), (-7, 237), (-6, 251), (-9, 272),
            (-10, 297), (-23, 297), (-24, 272), (-25, 253));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftLowerLeg,
            origin,
            scale,
            3.5,
            (20, 237), (7, 237), (6, 251), (9, 272),
            (10, 297), (23, 297), (24, 272), (25, 253));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.RightFoot,
            origin,
            scale,
            2.5,
            (-23, 295), (-10, 295), (-8, 302), (-2, 307),
            (-2, 312), (-29, 312), (-30, 308), (-27, 302));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LeftFoot,
            origin,
            scale,
            2.5,
            (23, 295), (10, 295), (8, 302), (2, 307),
            (2, 312), (29, 312), (30, 308), (27, 302));

        DrawRoundedRegion(context, regions, HitLocation.Neck, origin, scale,
            -7, 56, 14, 18, 5);
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.UpperTorso,
            origin,
            scale,
            5,
            (-12, 68), (-25, 69), (-36, 77), (-33, 95),
            (-29, 121), (-23, 130), (23, 130), (29, 121),
            (33, 95), (36, 77), (25, 69), (12, 68));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.LowerTorso,
            origin,
            scale,
            5,
            (-23, 128), (23, 128), (22, 148), (27, 165),
            (22, 176), (13, 180), (-13, 180), (-22, 176),
            (-27, 165), (-22, 148));
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.Head,
            origin,
            scale,
            4,
            (-10, 20), (-15, 25), (-17, 35), (-15, 47),
            (-10, 56), (-4, 61), (0, 62), (4, 61),
            (10, 56), (15, 47), (17, 35), (15, 25), (10, 20));
        DrawHelmetRegion(context, regions, origin, scale);

        if (scale >= 0.55)
        {
            DrawText(
                context,
                "R",
                new Rect(
                    origin.X - 72 * scale,
                    origin.Y + 83 * scale,
                    12,
                    14),
                Math.Clamp(8 * scale, 7, 10),
                DetailBrush,
                FontWeight.SemiBold);
            DrawText(
                context,
                "L",
                new Rect(
                    origin.X + 64 * scale,
                    origin.Y + 83 * scale,
                    12,
                    14),
                Math.Clamp(8 * scale, 7, 10),
                DetailBrush,
                FontWeight.SemiBold);
        }
    }

    private void DrawHelmetRegion(
        DrawingContext context,
        List<HitRegion> regions,
        Point origin,
        double scale)
    {
        DrawRoundedPolygonRegion(
            context,
            regions,
            HitLocation.Helmet,
            origin,
            scale,
            3,
            (-16, 29), (-17, 21), (-13, 12), (-7, 7),
            (0, 5), (7, 7), (13, 12), (17, 21),
            (16, 29), (9, 26), (0, 24), (-9, 26));
    }

    private void DrawAffordances(
        DrawingContext context,
        List<HitRegion> regions,
        Rect bounds)
    {
        const double gap = 6;
        double itemWidth = Math.Max(1, (bounds.Width - gap * 2) / 3);
        DrawAffordance(
            context,
            regions,
            HitLocation.None,
            "None",
            new Rect(bounds.X, bounds.Y, itemWidth, bounds.Height),
            AffordanceIcon.None);
        DrawAffordance(
            context,
            regions,
            HitLocation.Gun,
            "Gun",
            new Rect(
                bounds.X + itemWidth + gap,
                bounds.Y,
                itemWidth,
                bounds.Height),
            AffordanceIcon.Gun);
        DrawAffordance(
            context,
            regions,
            HitLocation.Shield,
            "Shield",
            new Rect(
                bounds.X + (itemWidth + gap) * 2,
                bounds.Y,
                itemWidth,
                bounds.Height),
            AffordanceIcon.Shield);
    }

    private void DrawAffordance(
        DrawingContext context,
        List<HitRegion> regions,
        HitLocation location,
        string label,
        Rect bounds,
        AffordanceIcon icon)
    {
        int index = (int)location;
        context.FillRectangle(FillFor(index), bounds, 7);
        context.DrawRectangle(BorderFor(index), bounds, 7);
        regions.Add(new HitRegion(index, HitShape.Rectangle, bounds, null));

        var iconBounds = new Rect(
            bounds.X + 8,
            bounds.Center.Y - 7,
            15,
            14);
        DrawAffordanceIcon(context, icon, iconBounds);
        DrawText(
            context,
            label,
            new Rect(
                bounds.X + 27,
                bounds.Y + 6,
                Math.Max(1, bounds.Width - 34),
                14),
            9,
            TitleBrush,
            FontWeight.SemiBold);
        DrawText(
            context,
            FormatMultiplier(GetMultiplier(index)),
            new Rect(
                bounds.X + 27,
                bounds.Y + 20,
                Math.Max(1, bounds.Width - 34),
                12),
            8,
            DetailBrush);
    }

    private static void DrawAffordanceIcon(
        DrawingContext context,
        AffordanceIcon icon,
        Rect bounds)
    {
        var pen = new Pen(BadgeIconBrush, 1.35);
        switch (icon)
        {
            case AffordanceIcon.None:
                context.DrawEllipse(
                    null,
                    pen,
                    new Rect(bounds.X + 2, bounds.Y + 2, 10, 10));
                context.DrawLine(
                    pen,
                    new Point(bounds.X + 4, bounds.Bottom - 4),
                    new Point(bounds.Right - 4, bounds.Y + 4));
                break;

            case AffordanceIcon.Gun:
                context.DrawLine(
                    pen,
                    new Point(bounds.X + 1, bounds.Y + 5),
                    new Point(bounds.Right - 1, bounds.Y + 5));
                context.DrawLine(
                    pen,
                    new Point(bounds.X + 7, bounds.Y + 6),
                    new Point(bounds.X + 10, bounds.Bottom - 2));
                context.DrawLine(
                    pen,
                    new Point(bounds.X + 2, bounds.Y + 7),
                    new Point(bounds.X + 6, bounds.Y + 7));
                break;

            case AffordanceIcon.Shield:
                Point[] points =
                [
                    new(bounds.X + 2, bounds.Y + 1),
                    new(bounds.Right - 2, bounds.Y + 1),
                    new(bounds.Right - 3, bounds.Y + 9),
                    new(bounds.Center.X, bounds.Bottom - 1),
                    new(bounds.X + 3, bounds.Y + 9)
                ];
                context.DrawGeometry(null, pen, CreatePolygon(points));
                break;
        }
    }

    private void DrawRoundedRegion(
        DrawingContext context,
        List<HitRegion> regions,
        HitLocation location,
        Point origin,
        double scale,
        double x,
        double y,
        double width,
        double height,
        double radius)
    {
        var bounds = TransformRect(origin, scale, x, y, width, height);
        int index = (int)location;
        double scaledRadius = radius * scale;
        var geometry = new RectangleGeometry(
            bounds,
            scaledRadius,
            scaledRadius);
        context.DrawGeometry(FillFor(index), BorderFor(index), geometry);
        regions.Add(new HitRegion(
            index,
            HitShape.Geometry,
            geometry.Bounds,
            geometry));
    }

    private void DrawRoundedPolygonRegion(
        DrawingContext context,
        List<HitRegion> regions,
        HitLocation location,
        Point origin,
        double scale,
        double radius,
        params (double X, double Y)[] points)
    {
        int index = (int)location;
        var geometry = CreateRoundedPolygon(
            TransformPoints(origin, scale, points),
            radius * scale);
        context.DrawGeometry(FillFor(index), BorderFor(index), geometry);
        regions.Add(new HitRegion(
            index,
            HitShape.Geometry,
            geometry.Bounds,
            geometry));
    }

    private IBrush FillFor(int index)
    {
        float? multiplier = GetMultiplier(index);
        if (multiplier is null)
            return MissingValueBrush;

        double intensity = Math.Clamp(multiplier.Value / 2.25, 0, 1);
        intensity = 0.18 + intensity * 0.82;
        Color color = Blend(
            Color.FromRgb(45, 57, 53),
            Color.FromRgb(121, 190, 65),
            intensity);
        if (index == SelectedIndex)
            color = Blend(color, Color.FromRgb(170, 225, 108), 0.16);
        else if (index == _hoveredIndex)
            color = Blend(color, Color.FromRgb(206, 220, 196), 0.1);
        return new SolidColorBrush(color);
    }

    private IPen BorderFor(int index)
    {
        if (index == SelectedIndex)
            return SelectedBorderPen;
        return index == _hoveredIndex ? HoverBorderPen : RegionBorderPen;
    }

    private float? GetMultiplier(int index)
    {
        IReadOnlyList<float>? multipliers = Multipliers;
        if (multipliers is null || index < 0 || index >= multipliers.Count)
            return null;

        float value;
        try
        {
            value = multipliers[index];
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        return float.IsFinite(value) ? value : null;
    }

    private int HitTest(Point point)
    {
        for (int index = _hitRegions.Count - 1; index >= 0; index--)
        {
            if (_hitRegions[index].Contains(point))
                return _hitRegions[index].Index;
        }
        return -1;
    }

    private static Point[] TransformPoints(
        Point origin,
        double scale,
        params (double X, double Y)[] points)
    {
        var transformed = new Point[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            transformed[index] = new Point(
                origin.X + points[index].X * scale,
                origin.Y + points[index].Y * scale);
        }
        return transformed;
    }

    private static Rect TransformRect(
        Point origin,
        double scale,
        double x,
        double y,
        double width,
        double height) =>
        new(
            origin.X + x * scale,
            origin.Y + y * scale,
            width * scale,
            height * scale);

    private static StreamGeometry CreateRoundedPolygon(
        IReadOnlyList<Point> points,
        double radius)
    {
        var geometry = new StreamGeometry();
        if (points.Count < 3)
            return geometry;

        var entries = new Point[points.Count];
        var exits = new Point[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            Point previous = points[(index + points.Count - 1) % points.Count];
            Point current = points[index];
            Point next = points[(index + 1) % points.Count];
            double incomingLength = Distance(current, previous);
            double outgoingLength = Distance(current, next);
            double trim = Math.Min(
                radius,
                Math.Min(incomingLength, outgoingLength) * 0.42);
            entries[index] = MoveToward(current, previous, trim, incomingLength);
            exits[index] = MoveToward(current, next, trim, outgoingLength);
        }

        using StreamGeometryContext path = geometry.Open();
        path.BeginFigure(entries[0], isFilled: true);
        path.QuadraticBezierTo(points[0], exits[0]);
        for (int index = 1; index < points.Count; index++)
        {
            path.LineTo(entries[index]);
            path.QuadraticBezierTo(points[index], exits[index]);
        }
        path.LineTo(entries[0]);
        path.EndFigure(isClosed: true);
        return geometry;
    }

    private static double Distance(Point first, Point second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static Point MoveToward(
        Point start,
        Point end,
        double distance,
        double totalDistance)
    {
        if (totalDistance <= double.Epsilon)
            return start;

        double amount = distance / totalDistance;
        return new Point(
            start.X + (end.X - start.X) * amount,
            start.Y + (end.Y - start.Y) * amount);
    }

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0)
            return geometry;

        using StreamGeometryContext path = geometry.Open();
        path.BeginFigure(points[0], isFilled: true);
        for (int index = 1; index < points.Count; index++)
            path.LineTo(points[index]);
        path.EndFigure(isClosed: true);
        return geometry;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static bool IsLocationIndex(int index) =>
        index >= (int)HitLocation.None && index < (int)HitLocation.Count;

    private static string FormatMultiplier(float? multiplier) =>
        multiplier is { } value
            ? $"{value.ToString("0.##", CultureInfo.CurrentCulture)}×"
            : "—";

    private static string FormatLocation(int index)
    {
        if (!IsLocationIndex(index))
            return "Unknown";

        string identifier = ((HitLocation)index).ToString();
        var formatted = new System.Text.StringBuilder(identifier.Length + 4);
        for (int characterIndex = 0;
             characterIndex < identifier.Length;
             characterIndex++)
        {
            char character = identifier[characterIndex];
            if (characterIndex > 0 && char.IsUpper(character))
                formatted.Append(' ');
            formatted.Append(
                characterIndex > 0
                    ? char.ToLower(character, CultureInfo.CurrentCulture)
                    : character);
        }
        return formatted.ToString();
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Rect bounds,
        double fontSize,
        IBrush brush,
        FontWeight fontWeight = default)
    {
        var formatted = new FormattedText(
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
            MaxTextWidth = Math.Max(1, bounds.Width),
            MaxTextHeight = Math.Max(1, bounds.Height),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        using (context.PushClip(bounds))
            context.DrawText(formatted, bounds.Position);
    }

    private enum AffordanceIcon
    {
        None,
        Gun,
        Shield
    }

    private enum HitShape
    {
        Rectangle,
        Geometry
    }

    private sealed record HitRegion(
        int Index,
        HitShape Shape,
        Rect Bounds,
        Geometry? Geometry)
    {
        public bool Contains(Point point)
        {
            if (!Bounds.Contains(point))
                return false;

            return Shape switch
            {
                HitShape.Geometry => Geometry?.FillContains(point) == true,
                _ => true
            };
        }
    }
}
