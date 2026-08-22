using IW4.Assets.Assets.Font;
using IW4.Studio.Documents;
using SkiaSharp;

namespace IW4.Studio.Desktop.Editors.Font;

internal static class OpenTypeFontRasterizer
{
    private const int GlyphGutter = 1;
    private const int MinimumAtlasSize = 128;
    private const int MaximumAtlasSize = 4096;

    public static OpenTypeFontRasterization Rasterize(
        ReadOnlyMemory<byte> sourceBytes,
        FontAsset template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (sourceBytes.IsEmpty)
            throw new InvalidDataException("The selected OpenType font is empty.");
        if (template.PixelHeight is <= 0 or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"IW4 raster-font replacement requires a pixel height between 1 and {byte.MaxValue}; " +
                $"this Font declares {template.PixelHeight}.");
        }

        using SKData data = SKData.CreateCopy(sourceBytes.Span);
        using SKTypeface typeface = SKTypeface.FromData(data, 0) ??
            throw new InvalidDataException(
                "Skia could not open the selected file as a TrueType or OpenType font.");
        using SKFont font = CreateFont(typeface, template.PixelHeight);

        ushort[] letters = template.Glyphs
            .Select(glyph => glyph.Letter)
            .ToArray();
        ushort fallbackGlyph = typeface.GetGlyph('.');
        var metrics = new List<GlyphRasterMetric>(letters.Length);
        int substitutedGlyphCount = 0;
        foreach (ushort letter in letters)
        {
            ushort glyphId = typeface.GetGlyph(letter);
            if (glyphId == 0 && letter != 0)
            {
                glyphId = fallbackGlyph;
                substitutedGlyphCount++;
            }

            metrics.Add(MeasureGlyph(font, letter, glyphId));
        }

        int atlasSize = PackGlyphs(metrics);
        byte[] alphaBytes = RasterizeAtlas(font, metrics, atlasSize);
        FontRasterizedGlyph[] glyphs = metrics
            .Select(metric => new FontRasterizedGlyph(
                metric.Letter,
                metric.X0,
                metric.Y0,
                metric.Dx,
                metric.PixelWidth,
                metric.PixelHeight,
                metric.AtlasX,
                metric.AtlasY))
            .ToArray();

        return new OpenTypeFontRasterization(
            new FontRasterization(
                template.PixelHeight,
                atlasSize,
                atlasSize,
                alphaBytes,
                glyphs),
            string.IsNullOrWhiteSpace(typeface.FamilyName)
                ? "Unnamed OpenType face"
                : typeface.FamilyName,
            substitutedGlyphCount);
    }

    private static SKFont CreateFont(SKTypeface typeface, int pixelHeight)
    {
        using var probe = new SKFont(typeface, pixelHeight)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Normal,
            Subpixel = false,
            EmbeddedBitmaps = false
        };
        SKFontMetrics probeMetrics = probe.Metrics;
        float metricHeight = probeMetrics.Descent - probeMetrics.Ascent;
        if (!float.IsFinite(metricHeight) || metricHeight <= 0)
            throw new InvalidDataException("The selected OpenType font exposes invalid vertical metrics.");

        float fontSize = pixelHeight * (pixelHeight / metricHeight);
        if (!float.IsFinite(fontSize) || fontSize <= 0)
            throw new InvalidDataException("The selected OpenType font cannot be scaled to the IW4 pixel height.");

        return new SKFont(typeface, fontSize)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Normal,
            Subpixel = false,
            EmbeddedBitmaps = false
        };
    }

    private static GlyphRasterMetric MeasureGlyph(
        SKFont font,
        ushort letter,
        ushort glyphId)
    {
        Span<ushort> glyphs = stackalloc ushort[1] { glyphId };
        Span<float> widths = stackalloc float[1];
        Span<SKRect> bounds = stackalloc SKRect[1];
        font.GetGlyphWidths(glyphs, widths, bounds, paint: null);

        float advance = widths[0];
        SKRect boundsValue = bounds[0];
        if (!float.IsFinite(advance) ||
            !float.IsFinite(boundsValue.Left) ||
            !float.IsFinite(boundsValue.Top) ||
            !float.IsFinite(boundsValue.Right) ||
            !float.IsFinite(boundsValue.Bottom))
        {
            throw new InvalidDataException(
                $"OpenType glyph U+{letter:X4} exposes non-finite metrics.");
        }

        int x0 = checked((int)MathF.Floor(boundsValue.Left));
        int y0 = checked((int)MathF.Floor(boundsValue.Top));
        int right = checked((int)MathF.Ceiling(boundsValue.Right));
        int bottom = checked((int)MathF.Ceiling(boundsValue.Bottom));
        int pixelWidth = Math.Max(0, right - x0);
        int pixelHeight = Math.Max(0, bottom - y0);
        int dx = checked((int)MathF.Round(advance, MidpointRounding.AwayFromZero));
        if (x0 is < sbyte.MinValue or > sbyte.MaxValue ||
            y0 is < sbyte.MinValue or > sbyte.MaxValue ||
            dx is < byte.MinValue or > byte.MaxValue ||
            pixelWidth is < byte.MinValue or > byte.MaxValue ||
            pixelHeight is < byte.MinValue or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"OpenType glyph U+{letter:X4} exceeds IW4's sbyte offsets or byte-sized advance/dimensions " +
                $"(x0={x0}, y0={y0}, dx={dx}, size={pixelWidth}x{pixelHeight}).");
        }

        return new GlyphRasterMetric(
            letter,
            glyphId,
            checked((sbyte)x0),
            checked((sbyte)y0),
            checked((byte)dx),
            checked((byte)pixelWidth),
            checked((byte)pixelHeight));
    }

    private static int PackGlyphs(IReadOnlyList<GlyphRasterMetric> metrics)
    {
        GlyphRasterMetric[] packed = metrics
            .Where(metric => metric.PixelWidth > 0 && metric.PixelHeight > 0)
            .OrderByDescending(metric => metric.PixelHeight)
            .ThenByDescending(metric => metric.PixelWidth)
            .ThenBy(metric => metric.Letter)
            .ToArray();
        for (int atlasSize = MinimumAtlasSize;
             atlasSize <= MaximumAtlasSize;
             atlasSize *= 2)
        {
            int x = GlyphGutter;
            int y = GlyphGutter;
            int shelfHeight = 0;
            bool fits = true;
            foreach (GlyphRasterMetric metric in packed)
            {
                int cellWidth = checked(metric.PixelWidth + GlyphGutter * 2);
                int cellHeight = checked(metric.PixelHeight + GlyphGutter * 2);
                if (cellWidth > atlasSize || cellHeight > atlasSize)
                {
                    fits = false;
                    break;
                }
                if (x + cellWidth > atlasSize)
                {
                    x = GlyphGutter;
                    y = checked(y + shelfHeight);
                    shelfHeight = 0;
                }
                if (y + cellHeight > atlasSize)
                {
                    fits = false;
                    break;
                }

                metric.AtlasX = checked(x + GlyphGutter);
                metric.AtlasY = checked(y + GlyphGutter);
                x = checked(x + cellWidth);
                shelfHeight = Math.Max(shelfHeight, cellHeight);
            }
            if (fits)
                return atlasSize;
        }

        throw new InvalidDataException(
            $"The font's {metrics.Count:N0} glyphs do not fit in IW4Studio's " +
            $"{MaximumAtlasSize:N0}×{MaximumAtlasSize:N0} raster-atlas limit at this pixel height.");
    }

    private static byte[] RasterizeAtlas(
        SKFont font,
        IReadOnlyList<GlyphRasterMetric> metrics,
        int atlasSize)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            atlasSize,
            atlasSize,
            SKColorType.Alpha8,
            SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        canvas.Clear(SKColors.Transparent);
        Span<ushort> glyph = stackalloc ushort[1];
        foreach (GlyphRasterMetric metric in metrics)
        {
            if (metric.PixelWidth == 0 || metric.PixelHeight == 0)
                continue;

            using var builder = new SKTextBlobBuilder();
            glyph[0] = metric.GlyphId;
            builder.AddRun(
                glyph,
                font,
                new SKPoint(
                    metric.AtlasX - metric.X0,
                    metric.AtlasY - metric.Y0));
            using SKTextBlob blob = builder.Build() ??
                throw new InvalidDataException(
                    $"Skia could not rasterize OpenType glyph U+{metric.Letter:X4}.");
            canvas.DrawText(blob, 0, 0, paint);
        }
        canvas.Flush();

        Span<byte> pixels = bitmap.GetPixelSpan();
        var alpha = new byte[checked(atlasSize * atlasSize)];
        for (int row = 0; row < atlasSize; row++)
        {
            pixels.Slice(checked(row * bitmap.RowBytes), atlasSize)
                .CopyTo(alpha.AsSpan(checked(row * atlasSize), atlasSize));
        }
        return alpha;
    }

    private sealed class GlyphRasterMetric
    {
        public GlyphRasterMetric(
            ushort letter,
            ushort glyphId,
            sbyte x0,
            sbyte y0,
            byte dx,
            byte pixelWidth,
            byte pixelHeight)
        {
            Letter = letter;
            GlyphId = glyphId;
            X0 = x0;
            Y0 = y0;
            Dx = dx;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }

        public ushort Letter { get; }
        public ushort GlyphId { get; }
        public sbyte X0 { get; }
        public sbyte Y0 { get; }
        public byte Dx { get; }
        public byte PixelWidth { get; }
        public byte PixelHeight { get; }
        public int AtlasX { get; set; }
        public int AtlasY { get; set; }
    }
}

internal sealed record OpenTypeFontRasterization(
    FontRasterization Rasterization,
    string FamilyName,
    int SubstitutedGlyphCount);
