using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.Game.Assets.Menu;
using IW4.Render.Textures;
using IW4.Render.UI.ScreenPlacement;
using IW4.Render.UI.Text;
using IW4.Render.Techniques;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private void DrawStage(
        DrawingContext context,
        MenuPreviewScene scene,
        PreviewTransform transform)
    {
        Rect stage = StageBounds(scene.Settings, transform);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(8, 10, 13)),
            new Pen(new SolidColorBrush(Color.FromRgb(75, 82, 92)), 1),
            stage);

        if (!ShowPlacementGuides)
            return;

        UiScreenPlacement placement = scene.Settings.ScreenPlacement;
        double gridSizeX = 32 * placement.VirtualToRealX * transform.Scale;
        double gridSizeY = 32 * placement.VirtualToRealY * transform.Scale;
        if (Math.Min(gridSizeX, gridSizeY) >= 10)
        {
            var gridPen = new Pen(
                new SolidColorBrush(Color.FromArgb(35, 154, 164, 178)),
                1);
            double subScreenLeft = stage.Left +
                placement.SubScreenLeft * transform.Scale;
            double subScreenRight = subScreenLeft +
                placement.VirtualWidth * placement.VirtualToRealX *
                transform.Scale;
            for (double x = subScreenLeft + gridSizeX;
                 x < subScreenRight;
                 x += gridSizeX)
            {
                context.DrawLine(gridPen, new Point(x, stage.Top), new Point(x, stage.Bottom));
            }
            for (double y = stage.Top + gridSizeY;
                 y < stage.Bottom;
                 y += gridSizeY)
            {
                context.DrawLine(gridPen, new Point(stage.Left, y), new Point(stage.Right, y));
            }
        }

        MenuPreviewInsets safe = scene.Settings.SafeArea;
        if (safe.Left + safe.Top + safe.Right + safe.Bottom <= 0)
            return;

        var safeRect = new Rect(
            stage.Left + safe.Left * transform.Scale,
            stage.Top + safe.Top * transform.Scale,
            stage.Width - (safe.Left + safe.Right) * transform.Scale,
            stage.Height - (safe.Top + safe.Bottom) * transform.Scale);
        context.DrawRectangle(
            null,
            new Pen(
                new SolidColorBrush(Color.FromArgb(150, 103, 190, 255)),
                1,
                DashStyle.Dash),
            safeRect);
    }

    private void DrawPrimitive(
        DrawingContext context,
        MenuPreviewScene scene,
        MenuPreviewPrimitive primitive,
        PreviewTransform transform)
    {
        MenuPreviewPlacement placement = EffectivePlacement(
            primitive,
            scene.Settings);
        Rect bounds = transform.Map(placement.OutputBounds);
        bool isManipulated = _geometryManipulation is
            {
                IsActivated: true
            } state &&
            IsManipulatedPrimitive(primitive, state);
        switch (primitive)
        {
            case MenuPreviewFill fill:
                context.DrawRectangle(Brush(fill.Color), null, bounds);
                break;

            case MenuPreviewBorder border:
                DrawBorder(context, border, bounds, transform.Scale);
                break;

            case MenuPreviewMaterial material:
                using (material.HorizontalFraction < 1
                    ? context.PushClip(VisibleMaterialBounds(material, bounds))
                    : (DrawingContext.PushedState?)null)
                {
                    DrawMaterial(context, material, bounds, placement.OutputBounds);
                }
                break;

            case MenuPreviewText text:
                MenuPreviewText effectiveText = placement == text.Placement
                    ? text
                    : text with { Placement = placement };
                DrawText(
                    context,
                    effectiveText,
                    bounds,
                    transform,
                    scene,
                    useCachedLayout: !isManipulated);
                break;

            case MenuPreviewPlaceholder placeholder:
                DrawPlaceholder(context, placeholder.Label, bounds);
                break;
        }
    }

    private static Rect VisibleMaterialBounds(MenuPreviewMaterial material, Rect bounds)
    {
        if (material.HorizontalFraction == 1f)
            return bounds;
        double width = bounds.Width * material.HorizontalFraction;
        return new Rect(material.FlipHorizontal ? bounds.Right - width : bounds.Left,
            bounds.Top, width, bounds.Height);
    }

    private void DrawMaterial(
        DrawingContext context,
        MenuPreviewMaterial material,
        Rect bounds,
        MenuPreviewRect nativeBounds)
    {
        if (_materialSnapshots.TryGetValue(
                material.MaterialName,
                out MenuPreviewMaterialSnapshot? snapshot) &&
            snapshot.CpuPreviewCompositeState is { } compositeState &&
            IsMaterialCulled(compositeState.CullMode))
        {
            return;
        }

        if (snapshot is not null)
        {
            double width = Math.Abs(nativeBounds.Width);
            double height = Math.Abs(nativeBounds.Height);
            int mip = snapshot.SelectMipLevel(width, height);
            var key = MaterialBitmapKey.Create(material.MaterialName, material.Tint, mip);
            // Dragging a rectangle can cross a mip boundary between scene refreshes.
            Bitmap bitmap = GetOrCreateMaterialBitmap(snapshot, key);
            using var filter = context.PushRenderOptions(new RenderOptions
            {
                BitmapInterpolationMode = snapshot.SamplerState is { MipFilter: TextureFilter.Point, MinLodControl: 0 } sampler
                    ? ResolveFilter(sampler, width, height, snapshot) switch
                {
                    TextureFilter.Point => BitmapInterpolationMode.None,
                    TextureFilter.Linear => BitmapInterpolationMode.LowQuality,
                    _ => BitmapInterpolationMode.Unspecified
                } : BitmapInterpolationMode.Unspecified
            });
            if (!material.FlipHorizontal && !material.FlipVertical)
            {
                context.DrawImage(bitmap, bounds);
                return;
            }

            var transform = new Matrix(
                material.FlipHorizontal ? -1 : 1,
                0,
                0,
                material.FlipVertical ? -1 : 1,
                material.FlipHorizontal ? bounds.Left + bounds.Right : 0,
                material.FlipVertical ? bounds.Top + bounds.Bottom : 0);
            using (context.PushTransform(transform))
                context.DrawImage(bitmap, bounds);
            return;
        }

        DrawCheckerboard(context, bounds, Bounds);
        string label = _materialFailures.TryGetValue(
            material.MaterialName,
            out string? failure)
                ? failure
                : $"Loading: {material.MaterialName}";
        DrawLabel(context, label, bounds, 10, Color.FromRgb(224, 229, 236));
    }

    // PS3 0x0022a000 swaps the UV endpoints and makes each signed extent positive
    // before submitting the quad. Mirrored menu pictures keep the usual back-facing winding.
    private static bool IsMaterialCulled(CullMode cullMode) => cullMode == CullMode.Back;

    private void DrawText(
        DrawingContext context,
        MenuPreviewText text,
        Rect bounds,
        PreviewTransform transform,
        MenuPreviewScene scene,
        bool useCachedLayout)
    {
        MenuPreviewTextLayout? layout;
        if (useCachedLayout)
        {
            _textLayouts.TryGetValue(text, out layout);
        }
        else if (TextResourceResolver is { } resolver)
        {
            layout = MenuPreviewTextLayoutPlanner.Plan(
                text,
                resolver,
                MenuPreviewTextLayoutContext.FromScreenPlacement(
                    scene.Settings.ScreenPlacement));
        }
        else
        {
            layout = null;
        }

        if (layout is not null &&
            DrawGlyphRun(
                context,
                text,
                layout,
                transform,
                scene.Settings.ScreenPlacement))
        {
            return;
        }

        // Native glyph placement also supports point anchors (rect x y 0 0).
        // Only the bounded fallback below needs a nonempty layout box.
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        UiScreenAxisPlacement horizontal =
            scene.Settings.ScreenPlacement.Resolve(
                text.Placement.VirtualRectangle.HorizontalAlignment);
        UiScreenAxisPlacement vertical =
            scene.Settings.ScreenPlacement.Resolve(
                text.Placement.VirtualRectangle.VerticalAlignment);
        double scaledFontSize = 24 * text.Scale * vertical.Scale *
            transform.Scale;
        double fontSize = double.IsFinite(scaledFontSize)
            ? Math.Clamp(scaledFontSize, 5, 96)
            : 5;
        string displayText = layout?.DisplayText ?? TextResourceResolver?
            .ResolveText(text.Text).DisplayText ?? text.Text;
        double insetX = (text.BorderInset + text.OffsetX) *
            horizontal.Scale * transform.Scale;
        double insetY = (text.BorderInset + text.OffsetY) *
            vertical.Scale * transform.Scale;
        var textBounds = new Rect(
            bounds.Left + insetX,
            bounds.Top + insetY,
            Math.Max(0, bounds.Width - insetX),
            Math.Max(0, bounds.Height - insetY));
        var formatted = new FormattedText(
            displayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            Brush(text.Color))
        {
            MaxTextWidth = textBounds.Width,
            MaxTextHeight = textBounds.Height,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = (text.Alignment & 3) switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };
        double y = (text.Alignment & 0xC) switch
        {
            8 => textBounds.Top + Math.Max(
                0,
                (textBounds.Height - formatted.Height) * 0.5),
            12 => textBounds.Bottom - Math.Min(
                textBounds.Height,
                formatted.Height),
            _ => textBounds.Top
        };
        using (context.PushClip(textBounds))
            context.DrawText(formatted, new Point(textBounds.Left, y));
    }

    private bool DrawGlyphRun(
        DrawingContext context,
        MenuPreviewText text,
        MenuPreviewTextLayout layout,
        PreviewTransform transform,
        UiScreenPlacement placement)
    {
        if (layout.GlyphRun is not { CanRender: true } glyphRun ||
            string.IsNullOrWhiteSpace(glyphRun.MaterialName) ||
            !_materialSnapshots.TryGetValue(
                glyphRun.MaterialName,
                out MenuPreviewMaterialSnapshot? snapshot))
        {
            return false;
        }

        UiScreenAxisPlacement horizontal = placement.Resolve(
            text.Placement.VirtualRectangle.HorizontalAlignment);
        UiScreenAxisPlacement vertical = placement.Resolve(
            text.Placement.VirtualRectangle.VerticalAlignment);
        float appliedBaselineX = horizontal.ApplyPosition(layout.BaselineX);
        float appliedBaselineY = vertical.ApplyPosition(layout.BaselineY);
        float snapX = MathF.Floor(appliedBaselineX + 0.5f) -
            appliedBaselineX;
        float snapY = MathF.Floor(appliedBaselineY + 0.5f) -
            appliedBaselineY;

        foreach (UiGlyphQuad glyph in glyphRun.Quads)
        {
            UiGlyphColorRun colorRun = glyphRun.ColorRuns[glyph.ColorRunIndex];
            MaterialBitmapKey key = MaterialBitmapKey.Create(
                glyphRun.MaterialName,
                MenuPreviewTextLayoutPlanner.ResolveGlyphColor(
                    text.Color,
                    colorRun.CaretColorCode));
            if (!_materialBitmaps.ContainsKey(key))
                return false;
        }

        foreach (UiGlyphQuad glyph in glyphRun.Quads)
        {
            UiGlyphColorRun colorRun = glyphRun.ColorRuns[glyph.ColorRunIndex];
            MaterialBitmapKey key = MaterialBitmapKey.Create(
                glyphRun.MaterialName,
                MenuPreviewTextLayoutPlanner.ResolveGlyphColor(
                    text.Color,
                    colorRun.CaretColorCode));
            Bitmap bitmap = _materialBitmaps[key];
            UiGlyphTextureRect uv = glyph.TextureCoordinates;
            var source = new Rect(
                uv.S0 * snapshot.Width,
                uv.T0 * snapshot.Height,
                (uv.S1 - uv.S0) * snapshot.Width,
                (uv.T1 - uv.T0) * snapshot.Height);
            UiGlyphRect glyphBounds = glyph.Bounds;
            Rect destination = transform.Map(new MenuPreviewRect(
                horizontal.ApplyPosition(glyphBounds.X) + snapX,
                vertical.ApplyPosition(glyphBounds.Y) + snapY,
                horizontal.ApplyLength(glyphBounds.Width),
                vertical.ApplyLength(glyphBounds.Height)));
            if (source.Width > 0 &&
                source.Height > 0 &&
                destination.Width > 0 &&
                destination.Height > 0)
            {
                context.DrawImage(bitmap, source, destination);
            }
        }
        return true;
    }

    private static void DrawPlaceholder(
        DrawingContext context,
        string label,
        Rect bounds)
    {
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(90, 57, 65, 78)),
            new Pen(
                new SolidColorBrush(Color.FromArgb(210, 151, 161, 176)),
                1,
                DashStyle.Dash),
            bounds);
        DrawLabel(context, label, bounds, 11, Color.FromRgb(220, 225, 232));
    }

    private void DrawSelection(
        DrawingContext context,
        MenuPreviewScene scene,
        PreviewTransform transform)
    {
        if (SelectedNodeId is not { } selected)
            return;

        MenuPreviewHitRegion? region = scene.HitRegions
            .Where(value => value.NodeId == selected)
            .OrderByDescending(value => value.ZIndex)
            .FirstOrDefault();
        if (region is null)
            return;

        Rect selectionBounds = transform.Map(
            EffectiveSelectionBounds(region));
        context.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(74, 184, 255)), 2),
            selectionBounds);
        DrawResizeHandles(context, selectionBounds);
        if (!ShowTextBounds || _geometryManipulation is { IsActivated: true })
            return;

        foreach (var (text, layout) in _textLayouts)
        {
            if (text.NodeId != selected || layout.GlyphRun is not { CanRender: true } run)
                continue;
            var rect = text.Placement.VirtualRectangle;
            var placement = scene.Settings.ScreenPlacement;
            var horizontal = placement.Resolve(rect.HorizontalAlignment);
            var vertical = placement.Resolve(rect.VerticalAlignment);
            float x = MathF.Floor(horizontal.ApplyPosition(layout.BaselineX) + 0.5f);
            float y = MathF.Floor(vertical.ApplyPosition(layout.BaselineY) + 0.5f);
            var baseline = transform.Map(new MenuPreviewRect(x, y,
                horizontal.ApplyLength(run.MaxLineAdvance), 0));
            var pen = new Pen(Brushes.Gold, 1, DashStyle.Dash);
            context.DrawLine(pen, baseline.TopLeft, baseline.TopRight);
            if (run.Quads.Count == 0) continue;
            float left = run.Quads.Min(q => q.Bounds.X);
            float top = run.Quads.Min(q => q.Bounds.Y);
            float right = run.Quads.Max(q => q.Bounds.X + q.Bounds.Width);
            float bottom = run.Quads.Max(q => q.Bounds.Y + q.Bounds.Height);
            context.DrawRectangle(null, pen, transform.Map(new MenuPreviewRect(
                x + horizontal.ApplyLength(left - layout.BaselineX),
                y + vertical.ApplyLength(top - layout.BaselineY),
                horizontal.ApplyLength(right - left), vertical.ApplyLength(bottom - top))));
        }
    }

    private static void DrawBorder(
        DrawingContext context,
        MenuPreviewBorder border,
        Rect bounds,
        double scale)
    {
        double horizontalThickness = Math.Clamp(
            border.ThicknessX * scale,
            0,
            Math.Max(0, bounds.Width));
        double verticalThickness = Math.Clamp(
            border.ThicknessY * scale,
            0,
            Math.Max(0, bounds.Height));
        if (!double.IsFinite(horizontalThickness) ||
            !double.IsFinite(verticalThickness) ||
            horizontalThickness <= 0 ||
            verticalThickness <= 0)
        {
            return;
        }

        IBrush brush = Brush(border.Color);
        switch (border.Border)
        {
            case WindowBorder.WINDOW_BORDER_HORZ:
            case WindowBorder.WINDOW_BORDER_KCGRADIENT:
                DrawHorizontalBorder(
                    context,
                    brush,
                    bounds,
                    verticalThickness);
                break;
            case WindowBorder.WINDOW_BORDER_VERT:
                DrawVerticalBorder(
                    context,
                    brush,
                    bounds,
                    horizontalThickness,
                    insetY: 0);
                break;
            case WindowBorder.WINDOW_BORDER_RAISED:
            case WindowBorder.WINDOW_BORDER_SUNKEN:
                var color = border.Color;
                IBrush dimmed = Brush(new MenuColorValue(color.A, color.R * border.LowLightScale,
                    color.G * border.LowLightScale, color.B * border.LowLightScale));
                bool raised = border.Border == WindowBorder.WINDOW_BORDER_RAISED;
                IBrush topLeft = raised ? brush : dimmed;
                IBrush bottomRight = raised ? dimmed : brush;
                var inset = new Vector(horizontalThickness, verticalThickness);
                Point tl = bounds.TopLeft + inset;
                Point br = bounds.BottomRight - inset;
                var tr = new Point(br.X, tl.Y);
                var bl = new Point(tl.X, br.Y);
                DrawBorderFace(context, topLeft, bounds.TopLeft, bounds.TopRight, tr, tl);
                DrawBorderFace(context, topLeft, bounds.TopLeft, tl, bl, bounds.BottomLeft);
                DrawBorderFace(context, bottomRight, bounds.BottomLeft, bl, br, bounds.BottomRight);
                DrawBorderFace(context, bottomRight, bounds.TopRight, bounds.BottomRight, br, tr);
                break;
            default:
                DrawHorizontalBorder(
                    context,
                    brush,
                    bounds,
                    verticalThickness);
                DrawVerticalBorder(
                    context,
                    brush,
                    bounds,
                    horizontalThickness,
                    verticalThickness);
                break;
        }
    }

    private static void DrawBorderFace(DrawingContext context, IBrush brush, Point a, Point b, Point c, Point d)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(a, isFilled: true);
            path.LineTo(b);
            path.LineTo(c);
            path.LineTo(d);
            path.EndFigure(isClosed: true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawHorizontalBorder(
        DrawingContext context,
        IBrush brush,
        Rect bounds,
        double thickness)
    {
        context.DrawRectangle(
            brush,
            null,
            new Rect(bounds.Left, bounds.Top, bounds.Width, thickness));
        context.DrawRectangle(
            brush,
            null,
            new Rect(
                bounds.Left,
                bounds.Bottom - thickness,
                bounds.Width,
                thickness));
    }

    private static void DrawVerticalBorder(
        DrawingContext context,
        IBrush brush,
        Rect bounds,
        double thickness,
        double insetY)
    {
        double height = Math.Max(0, bounds.Height - insetY * 2);
        context.DrawRectangle(
            brush,
            null,
            new Rect(bounds.Left, bounds.Top + insetY, thickness, height));
        context.DrawRectangle(
            brush,
            null,
            new Rect(
                bounds.Right - thickness,
                bounds.Top + insetY,
                thickness,
                height));
    }

    private static void DrawCheckerboard(
        DrawingContext context,
        Rect bounds,
        Rect visibleBounds)
    {
        const double cell = 12;
        double left = Math.Max(bounds.Left, visibleBounds.Left);
        double top = Math.Max(bounds.Top, visibleBounds.Top);
        double right = Math.Min(bounds.Right, visibleBounds.Right);
        double bottom = Math.Min(bounds.Bottom, visibleBounds.Bottom);
        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            left >= right ||
            top >= bottom)
        {
            return;
        }

        int row = 0;
        for (double y = top; y < bottom; y += cell, row++)
        {
            int column = 0;
            for (double x = left; x < right; x += cell, column++)
            {
                bool alternate = (column + row) % 2 == 0;
                context.DrawRectangle(
                    new SolidColorBrush(alternate
                        ? Color.FromRgb(47, 52, 61)
                        : Color.FromRgb(34, 38, 45)),
                    null,
                    new Rect(
                        x,
                        y,
                        Math.Min(cell, right - x),
                        Math.Min(cell, bottom - y)));
            }
        }
    }

    private static void DrawLabel(
        DrawingContext context,
        string text,
        Rect bounds,
        double fontSize,
        Color color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            new SolidColorBrush(color))
        {
            MaxTextWidth = Math.Max(1, bounds.Width - 8),
            MaxTextHeight = bounds.Height,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };
        Point origin = new(
            bounds.Left + 4,
            bounds.Top + Math.Max(0, (bounds.Height - formatted.Height) * 0.5));
        using (context.PushClip(bounds))
            context.DrawText(formatted, origin);
    }

    private void DrawCenteredLabel(DrawingContext context, string text) =>
        DrawLabel(
            context,
            text,
            Bounds,
            13,
            Color.FromRgb(170, 178, 190));

    private PreviewTransform CreateTransform(MenuPreviewSettings settings)
    {
        double scale = Math.Min(
            Bounds.Width / settings.CanvasWidth,
            Bounds.Height / settings.CanvasHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;
        return new PreviewTransform(
            scale,
            new Point(
                (Bounds.Width - settings.CanvasWidth * scale) * 0.5,
                (Bounds.Height - settings.CanvasHeight * scale) * 0.5));
    }

    private static Rect StageBounds(
        MenuPreviewSettings settings,
        PreviewTransform transform) =>
        transform.Map(new MenuPreviewRect(
            0,
            0,
            settings.CanvasWidth,
            settings.CanvasHeight));

    private static IBrush Brush(MenuColorValue value) =>
        new SolidColorBrush(Color.FromArgb(
            Channel(value.A),
            Channel(value.R),
            Channel(value.G),
            Channel(value.B)));

    private static byte Channel(float value) =>
        (byte)Math.Round(Clamp01(value) * byte.MaxValue);

    private static double Clamp01(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static Bitmap CreateMaterialBitmap(
        MenuPreviewMaterialSnapshot snapshot,
        MaterialBitmapKey key)
    {
        byte[] rgba = snapshot.MipPixels(key.MipLevel).ToArray();
        int width = Math.Max(1, snapshot.Width >> key.MipLevel);
        int height = Math.Max(1, snapshot.Height >> key.MipLevel);
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = MultiplyChannel(rgba[offset], key.R);
            rgba[offset + 1] = MultiplyChannel(rgba[offset + 1], key.G);
            rgba[offset + 2] = MultiplyChannel(rgba[offset + 2], key.B);
            rgba[offset + 3] = MultiplyChannel(rgba[offset + 3], key.A);
            if (snapshot.CpuPreviewCompositeState is { } state &&
                !PassesAlphaTest(rgba[offset + 3] / 255f, state.AlphaTest))
            {
                rgba[offset] = 0;
                rgba[offset + 1] = 0;
                rgba[offset + 2] = 0;
                rgba[offset + 3] = 0;
            }
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormats.Rgba8888,
            AlphaFormat.Unpremul);
        try
        {
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            int sourceStride = checked(width * 4);
            if (framebuffer.RowBytes < sourceStride)
            {
                throw new InvalidDataException(
                    "The preview bitmap row stride is smaller than its RGBA payload.");
            }
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(
                    rgba,
                    checked(row * sourceStride),
                    IntPtr.Add(
                        framebuffer.Address,
                        checked(row * framebuffer.RowBytes)),
                    sourceStride);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static byte MultiplyChannel(byte source, byte tint) =>
        (byte)((source * tint + 127) / byte.MaxValue);

    private readonly record struct MaterialBitmapKey(
        string MaterialName,
        byte A,
        byte R,
        byte G,
        byte B,
        int MipLevel)
    {
        public static MaterialBitmapKey Create(
            string materialName,
            MenuColorValue tint,
            int mipLevel = 0) =>
            new(
                materialName,
                Channel(tint.A),
                Channel(tint.R),
                Channel(tint.G),
                Channel(tint.B), mipLevel);
    }

    private readonly record struct PreviewTransform(double Scale, Point Origin)
    {
        public bool TryUnmap(Point value, out Point result)
        {
            double x = (value.X - Origin.X) / Scale;
            double y = (value.Y - Origin.Y) / Scale;
            result = new Point(x, y);
            return double.IsFinite(x) &&
                double.IsFinite(y) &&
                x is >= float.MinValue and <= float.MaxValue &&
                y is >= float.MinValue and <= float.MaxValue;
        }

        public Rect Map(MenuPreviewRect value)
        {
            double firstX = Origin.X + value.X * Scale;
            double firstY = Origin.Y + value.Y * Scale;
            double secondX = firstX + value.Width * Scale;
            double secondY = firstY + value.Height * Scale;
            if (!double.IsFinite(firstX) ||
                !double.IsFinite(firstY) ||
                !double.IsFinite(secondX) ||
                !double.IsFinite(secondY))
            {
                return default;
            }
            return new Rect(
                Math.Min(firstX, secondX),
                Math.Min(firstY, secondY),
                Math.Abs(secondX - firstX),
                Math.Abs(secondY - firstY));
        }
    }
}
