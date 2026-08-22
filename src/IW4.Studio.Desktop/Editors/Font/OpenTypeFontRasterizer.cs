using IW4.Assets.Assets.Font;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Documents;
using SkiaSharp;

namespace IW4.Studio.Desktop.Editors.Font;

internal static class OpenTypeFontRasterizer
{
    private const int GlyphGutter = 1;
    private const int MinimumAtlasSize = 128;
    private const int MaximumAtlasSize = 4096;

    private static ReadOnlySpan<ushort> NativePs3ButtonLetters =>
    [
        0x0001, // Cross
        0x0002, // Circle
        0x0003, // Square
        0x0004, // Triangle
        0x0005, // L1
        0x0006, // R1
        0x000E, // Start
        0x000F, // Select
        0x0010, // L3
        0x0011, // R3
        0x0012, // L2
        0x0013, // R2
        0x0014, // D-pad up
        0x0015, // D-pad down
        0x0016, // D-pad left
        0x0017, // D-pad right
        0x00BC, // Alternate L3
        0x00BD  // Alternate R3
    ];

    public static OpenTypeFontRasterization Rasterize(
        ReadOnlyMemory<byte> sourceBytes,
        FontAsset template,
        IGfxImagePayloadResolver imagePayloadResolver)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(imagePayloadResolver);
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
        NativeGlyphAtlas nativeAtlas = DecodeNativePs3ButtonAtlas(
            template,
            imagePayloadResolver);

        ushort fallbackGlyph = typeface.GetGlyph('.');
        var metrics = new List<GlyphRasterMetric>(template.Glyphs.Count);
        int substitutedGlyphCount = 0;
        foreach (FontGlyph templateGlyph in template.Glyphs)
        {
            ushort letter = templateGlyph.Letter;
            if (IsNativePs3Button(letter))
            {
                metrics.Add(CreateNativeGlyphMetric(templateGlyph, nativeAtlas));
                continue;
            }

            ushort glyphId = typeface.GetGlyph(letter);
            if (glyphId == 0 && letter != 0)
            {
                glyphId = fallbackGlyph;
                substitutedGlyphCount++;
            }

            metrics.Add(MeasureGlyph(font, letter, glyphId));
        }

        int atlasSize = PackGlyphs(metrics);
        byte[] rgbaBytes = RasterizeAtlas(font, metrics, atlasSize);
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
                rgbaBytes,
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

    private static NativeGlyphAtlas DecodeNativePs3ButtonAtlas(
        FontAsset template,
        IGfxImagePayloadResolver imagePayloadResolver)
    {
        var missingLetters = new List<string>();
        foreach (ushort letter in NativePs3ButtonLetters)
        {
            if (!template.Glyphs.Any(glyph => glyph.Letter == letter))
                missingLetters.Add($"U+{letter:X4}");
        }
        if (missingLetters.Count > 0)
        {
            throw new InvalidDataException(
                "IW4 PS3 Font replacement requires the native controller-button rows " +
                $"{string.Join(", ", missingLetters)}, but this Font does not contain them.");
        }

        if (template.Material is not { } material)
        {
            throw new InvalidDataException(
                "IW4 PS3 Font replacement requires the native Font material so its " +
                "controller-button pixels can be preserved.");
        }
        var atlasRows = material.Textures
            .Where(row => row.Image is not null && row.Water is null)
            .ToArray();
        if (atlasRows.Length != 1)
        {
            throw new InvalidDataException(
                "IW4 PS3 Font replacement requires exactly one materialized native " +
                "Font atlas image so its controller-button pixels can be preserved.");
        }

        var image = atlasRows[0].Image!;
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                image,
                imagePayloadResolver,
                out GfxImagePreviewSnapshot? preview,
                out string reason) ||
            preview is null)
        {
            throw new InvalidDataException(
                $"The native Font atlas '{image.Name ?? "unnamed image"}' could not be " +
                $"decoded to preserve its PS3 controller buttons: {reason}");
        }

        byte[] rgbaBytes = preview.GetRgbaBytesCopy();
        int expectedByteCount = checked(preview.Width * preview.Height * 4);
        if (rgbaBytes.Length != expectedByteCount)
        {
            throw new InvalidDataException(
                $"The decoded native Font atlas contains {rgbaBytes.Length:N0} RGBA bytes; " +
                $"{expectedByteCount:N0} are required for {preview.Width:N0}×{preview.Height:N0} pixels.");
        }
        return new NativeGlyphAtlas(
            preview.Width,
            preview.Height,
            rgbaBytes);
    }

    private static GlyphRasterMetric CreateNativeGlyphMetric(
        FontGlyph glyph,
        NativeGlyphAtlas atlas)
    {
        if (glyph.PixelWidth == 0 || glyph.PixelHeight == 0)
        {
            throw new InvalidDataException(
                $"Native PS3 controller-button glyph U+{glyph.Letter:X4} has no pixel rectangle.");
        }

        int sourceX = ResolveTexturePixel(glyph.S0, atlas.Width, glyph.Letter, "S0");
        int sourceY = ResolveTexturePixel(glyph.T0, atlas.Height, glyph.Letter, "T0");
        int sourceRight = ResolveTexturePixel(glyph.S1, atlas.Width, glyph.Letter, "S1");
        int sourceBottom = ResolveTexturePixel(glyph.T1, atlas.Height, glyph.Letter, "T1");
        if (sourceRight - sourceX != glyph.PixelWidth ||
            sourceBottom - sourceY != glyph.PixelHeight ||
            sourceX < 0 ||
            sourceY < 0 ||
            sourceRight > atlas.Width ||
            sourceBottom > atlas.Height)
        {
            throw new InvalidDataException(
                $"Native PS3 controller-button glyph U+{glyph.Letter:X4} has atlas coordinates " +
                $"({sourceX},{sourceY})–({sourceRight},{sourceBottom}) that do not match its " +
                $"{glyph.PixelWidth:N0}×{glyph.PixelHeight:N0} pixel rectangle inside the " +
                $"{atlas.Width:N0}×{atlas.Height:N0} native atlas.");
        }

        int rowByteCount = checked(glyph.PixelWidth * 4);
        var rgbaBytes = new byte[checked(rowByteCount * glyph.PixelHeight)];
        for (int row = 0; row < glyph.PixelHeight; row++)
        {
            atlas.RgbaBytes.AsSpan(
                    checked(((sourceY + row) * atlas.Width + sourceX) * 4),
                    rowByteCount)
                .CopyTo(rgbaBytes.AsSpan(checked(row * rowByteCount), rowByteCount));
        }

        return new GlyphRasterMetric(
            glyph.Letter,
            glyphId: 0,
            glyph.X0,
            glyph.Y0,
            glyph.Dx,
            glyph.PixelWidth,
            glyph.PixelHeight,
            rgbaBytes);
    }

    private static int ResolveTexturePixel(
        float coordinate,
        int dimension,
        ushort letter,
        string coordinateName)
    {
        if (!float.IsFinite(coordinate) || coordinate is < 0f or > 1f)
        {
            throw new InvalidDataException(
                $"Native PS3 controller-button glyph U+{letter:X4} has invalid {coordinateName} " +
                $"texture coordinate {coordinate}.");
        }

        const float tolerance = 1f / 1024f;
        float scaled = coordinate * dimension;
        float edgePixel = MathF.Round(scaled);
        if (MathF.Abs(scaled - edgePixel) <= tolerance)
            return checked((int)edgePixel);

        float centerPixel = MathF.Round(scaled - 0.5f);
        if (MathF.Abs((scaled - 0.5f) - centerPixel) <= tolerance)
            return checked((int)centerPixel);

        throw new InvalidDataException(
            $"Native PS3 controller-button glyph U+{letter:X4} {coordinateName} does not " +
            "address a native atlas texel edge or center.");
    }

    private static bool IsNativePs3Button(ushort letter) =>
        NativePs3ButtonLetters.Contains(letter);

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
            if (metric.NativeRgbaBytes is not null)
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
        var rgba = new byte[checked(atlasSize * atlasSize * 4)];
        for (int row = 0; row < atlasSize; row++)
        {
            ReadOnlySpan<byte> alphaRow = pixels.Slice(
                checked(row * bitmap.RowBytes),
                atlasSize);
            for (int column = 0; column < atlasSize; column++)
            {
                int offset = checked((row * atlasSize + column) * 4);
                rgba[offset] = byte.MaxValue;
                rgba[offset + 1] = byte.MaxValue;
                rgba[offset + 2] = byte.MaxValue;
                rgba[offset + 3] = alphaRow[column];
            }
        }

        foreach (GlyphRasterMetric metric in metrics)
        {
            if (metric.NativeRgbaBytes is not { } nativeRgbaBytes)
                continue;

            int rowByteCount = checked(metric.PixelWidth * 4);
            for (int row = 0; row < metric.PixelHeight; row++)
            {
                nativeRgbaBytes.AsSpan(checked(row * rowByteCount), rowByteCount)
                    .CopyTo(rgba.AsSpan(
                        checked(((metric.AtlasY + row) * atlasSize + metric.AtlasX) * 4),
                        rowByteCount));
            }
        }
        return rgba;
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
            byte pixelHeight,
            byte[]? nativeRgbaBytes = null)
        {
            Letter = letter;
            GlyphId = glyphId;
            X0 = x0;
            Y0 = y0;
            Dx = dx;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            NativeRgbaBytes = nativeRgbaBytes;
        }

        public ushort Letter { get; }
        public ushort GlyphId { get; }
        public sbyte X0 { get; }
        public sbyte Y0 { get; }
        public byte Dx { get; }
        public byte PixelWidth { get; }
        public byte PixelHeight { get; }
        public byte[]? NativeRgbaBytes { get; }
        public int AtlasX { get; set; }
        public int AtlasY { get; set; }
    }

    private sealed record NativeGlyphAtlas(
        int Width,
        int Height,
        byte[] RgbaBytes);
}

internal sealed record OpenTypeFontRasterization(
    FontRasterization Rasterization,
    string FamilyName,
    int SubstitutedGlyphCount);
