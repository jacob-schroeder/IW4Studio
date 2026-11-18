using IW4.Game.Assets.Font;
using IW4.Render.UI;

namespace IW4.Render.UI.Text;

/// <summary>
/// Produces virtual-space atlas quads using the IW4 Font metrics. The planner
/// supports single-line and auto-wrapped layout in virtual coordinates.
/// </summary>
public static class UiGlyphRunPlanner
{
    private const int NativeFallbackGlyphOrdinal = 14;
    private const int NativeAsciiGlyphCount = 96;
    private const int NativeWrappedLineBufferSize = 1024;

    /// <summary>Plans independently aligned lines for an auto-wrapped IW4 item.</summary>
    public static UiGlyphRunPlan PlanWrapped(
        FontAsset font,
        UiGlyphRunRequest request,
        UiGlyphRunPlan measurement,
        float containerWidth,
        int horizontalAlignment)
    {
        if (!measurement.CanRender || !float.IsFinite(containerWidth))
            return Plan(font, request);

        // PS3 wrapped text (0x246E20) advances by the integer UI_TextHeight.
        float lineHeight = MathF.Truncate(measurement.PixelHeight * measurement.NormalizedScale);
        int widthLimit = (int)Math.Clamp((double)containerWidth, 0, int.MaxValue);
        int lineWidth = horizontalAlignment == 1
            ? BalancedWrapWidth(font, request.Text, measurement, widthLimit)
            : widthLimit;

        var quads = new List<UiGlyphQuad>(measurement.Quads.Count);
        var colorRuns = new List<UiGlyphColorRun>();
        var diagnostics = new List<UiTextDiagnostic>();
        float baselineY = request.BaselineY;
        float maxAdvance = 0;
        float endPenX = request.BaselineX;
        for (int start = 0; start < request.Text.Length && request.Text[start] != '\0';)
        {
            int end = WrapPosition(font, request.Text, start, lineWidth, measurement.NormalizedScale);
            UiGlyphRunPlan line = Plan(font, new UiGlyphRunRequest(request.Text[start..end], 0, 0, request.TextScale));
            float width = MathF.Truncate(line.MaxLineAdvance);
            float baselineX = request.BaselineX + (horizontalAlignment switch
            {
                0 => 0,
                1 => (containerWidth - width) * 0.5f,
                _ => containerWidth - width
            });
            foreach (UiGlyphQuad quad in line.Quads)
            {
                quads.Add(quad with
                {
                    SourceUtf16Index = start + quad.SourceUtf16Index,
                    ColorRunIndex = colorRuns.Count + quad.ColorRunIndex,
                    Bounds = quad.Bounds with { X = baselineX + quad.Bounds.X, Y = baselineY + quad.Bounds.Y }
                });
            }
            int firstLineQuad = quads.Count - line.Quads.Count;
            foreach (UiGlyphColorRun run in line.ColorRuns)
                colorRuns.Add(run with { FirstQuadIndex = firstLineQuad + run.FirstQuadIndex });
            diagnostics.AddRange(line.Diagnostics);
            endPenX = baselineX + line.MaxLineAdvance;
            maxAdvance = MathF.Max(maxAdvance, line.MaxLineAdvance);
            baselineY += lineHeight;
            start = SkipWrapSpace(request.Text, end);
        }

        return new UiGlyphRunPlan(measurement.FontName, measurement.PixelHeight,
            measurement.MaterialName, measurement.GlowMaterialName, measurement.NormalizedScale,
            maxAdvance, endPenX, baselineY, quads, colorRuns, diagnostics);
    }

    private static int BalancedWrapWidth(
        FontAsset font,
        string text,
        UiGlyphRunPlan measurement,
        int widthLimit)
    {
        float width = MathF.Truncate(measurement.MaxLineAdvance);
        if (widthLimit == 0 || width <= widthLimit)
            return widthLimit;

        float lines = MathF.Ceiling(width / widthLimit);
        int guess = (int)MathF.Ceiling(width / lines);
        while (guess < widthLimit)
        {
            int count = 0;
            for (int start = 0; start < text.Length && text[start] != '\0'; count++)
                start = SkipWrapSpace(text, WrapPosition(font, text, start, guess, measurement.NormalizedScale));
            if (count == lines)
                return guess;
            guess = (int)Math.Min((long)widthLimit, guess + 8L);
        }
        return widthLimit;
    }

    private static int WrapPosition(FontAsset font, string text, int start, int maxWidth, float normalizedScale)
    {
        IReadOnlyList<FontGlyph> glyphs = font.Glyphs ?? [];
        int breakAt = -1;
        int advance = 0;
        int index = start;
        while (index < text.Length && text[index] != '\0')
        {
            int before = index;
            char character = text[index++];
            if (character == '\n')
                return index;
            if (character == '\r')
            {
                advance = 0;
                continue;
            }

            if (IsCaretColorEscape(text, before))
            {
                index++;
            }
            else
            {
                if (char.IsHighSurrogate(character) && index < text.Length && char.IsLowSurrogate(text[index]))
                    index++;
                FontGlyph? glyph = !char.IsSurrogate(character) && TryResolveNativeGlyph(glyphs, character, out FontGlyph? found)
                    ? found
                    : glyphs[NativeFallbackGlyphOrdinal];
                advance += glyph?.Dx ?? 0;
                if (before != start && IsWrapSpace(character))
                    breakAt = before;
            }

            if (breakAt >= 0 && advance * normalizedScale > maxWidth)
                return breakAt;
            if (index - start >= NativeWrappedLineBufferSize)
                return breakAt >= 0 ? breakAt : before;
        }
        return index;
    }

    private static int SkipWrapSpace(string text, int index)
    {
        while (index < text.Length && IsWrapSpace(text[index]))
            index++;
        return index;
    }

    private static bool IsWrapSpace(char character) => character is ' ' or '\t' or '\v' or '\f' or '\r';

    public static UiGlyphRunPlan Plan(
        FontAsset font,
        UiGlyphRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Text);

        var diagnostics = new List<UiTextDiagnostic>();
        ValidateFont(font, request, diagnostics);

        float normalizedScale = IsValidScale(font, request)
            ? request.TextScale * 48f / font.PixelHeight
            : 0f;
        IReadOnlyList<FontGlyph> glyphs = font.Glyphs ?? [];
        bool hasNativeGlyphTable = ValidateNativeGlyphTable(
            glyphs,
            diagnostics);
        FontGlyph? fallbackGlyph = hasNativeGlyphTable
                ? glyphs[NativeFallbackGlyphOrdinal]
                : null;

        var quads = new List<UiGlyphQuad>(request.Text.Length);
        var colorRuns = new List<UiGlyphColorRun>();
        var reportedMissingScalars = new HashSet<int>();
        var reportedInvalidUvLetters = new HashSet<ushort>();
        char? selectedCaretColor = null;
        char? openRunColor = null;
        int openRunIndex = -1;
        int openRunFirstQuad = -1;
        float penX = request.BaselineX;
        float penY = request.BaselineY;
        float maxLineAdvance = 0f;

        for (int sourceIndex = 0; sourceIndex < request.Text.Length;)
        {
            char first = request.Text[sourceIndex];
            if (first == '\0')
                break;
            if (IsInlineMaterialCommand(request.Text, sourceIndex))
            {
                diagnostics.Add(new UiTextDiagnostic(
                    UiTextDiagnosticCode.UnsupportedInlineMaterialCommand,
                    UiDiagnosticSeverity.Blocker,
                    "The native ^<type 1/2><width><height><Material*> " +
                    "inline-material command requires canonical runtime " +
                    "pointer virtualization and is not rendered as glyphs.",
                    sourceIndex));
                break;
            }
            if (IsCaretColorEscape(request.Text, sourceIndex))
            {
                char code = request.Text[sourceIndex + 1];
                selectedCaretColor = code == '7' ? null : code;
                sourceIndex += 2;
                continue;
            }
            if (first is '\r' or '\n')
            {
                penX = request.BaselineX;
                if (first == '\n')
                    penY += font.PixelHeight * normalizedScale;
                sourceIndex++;
                continue;
            }

            int sourceLength = 1;
            int scalar = first;
            bool unsupportedScalar = char.IsSurrogate(first);
            if (char.IsHighSurrogate(first) &&
                sourceIndex + 1 < request.Text.Length &&
                char.IsLowSurrogate(request.Text[sourceIndex + 1]))
            {
                scalar = char.ConvertToUtf32(
                    first,
                    request.Text[sourceIndex + 1]);
                sourceLength = 2;
            }

            FontGlyph? glyph = null;
            bool isFallback = false;
            bool exactGlyphFound = false;
            if (hasNativeGlyphTable &&
                !unsupportedScalar &&
                scalar <= ushort.MaxValue)
            {
                exactGlyphFound = TryResolveNativeGlyph(
                    glyphs,
                    (ushort)scalar,
                    out glyph);
            }
            if (hasNativeGlyphTable && !exactGlyphFound)
            {
                UiTextDiagnosticCode code = scalar > ushort.MaxValue ||
                    unsupportedScalar
                        ? UiTextDiagnosticCode.UnsupportedUnicodeScalar
                        : UiTextDiagnosticCode.MissingGlyph;
                if (reportedMissingScalars.Add(scalar))
                {
                    diagnostics.Add(new UiTextDiagnostic(
                        code,
                        UiDiagnosticSeverity.Warning,
                        code == UiTextDiagnosticCode.UnsupportedUnicodeScalar
                            ? $"Unicode scalar U+{scalar:X} cannot be represented by the Font asset's 16-bit letter field."
                            : $"Font '{DisplayName(font)}' has no glyph for U+{scalar:X4}; the native fallback glyph will be used when available.",
                        sourceIndex,
                        scalar));
                }

                glyph = fallbackGlyph;
                isFallback = glyph is not null;
            }

            if (glyph is not null)
            {
                int colorRunIndex = EnsureColorRun(
                    selectedCaretColor,
                    quads.Count,
                    colorRuns,
                    ref openRunColor,
                    ref openRunIndex,
                    ref openRunFirstQuad);
                UiGlyphTextureRect textureCoordinates = new(
                    glyph.S0,
                    glyph.T0,
                    glyph.S1,
                    glyph.T1);
                if (!IsValidTextureRect(textureCoordinates) &&
                    reportedInvalidUvLetters.Add(glyph.Letter))
                {
                    diagnostics.Add(new UiTextDiagnostic(
                        UiTextDiagnosticCode.InvalidGlyphTextureCoordinates,
                        UiDiagnosticSeverity.Warning,
                        $"Font '{DisplayName(font)}' glyph U+{glyph.Letter:X4} has invalid atlas coordinates.",
                        sourceIndex,
                        scalar));
                }

                float xOffset = glyph.X0 * normalizedScale;
                float yOffset = glyph.Y0 * normalizedScale;
                quads.Add(new UiGlyphQuad(
                    sourceIndex,
                    sourceLength,
                    scalar,
                    glyph.Letter,
                    isFallback,
                    new UiGlyphRect(
                        penX + xOffset,
                        penY + yOffset,
                        glyph.PixelWidth * normalizedScale,
                        glyph.PixelHeight * normalizedScale),
                    textureCoordinates,
                    colorRunIndex));
                penX += glyph.Dx * normalizedScale;
                maxLineAdvance = Math.Max(
                    maxLineAdvance,
                    penX - request.BaselineX);
            }

            sourceIndex += sourceLength;
        }

        CloseColorRun(
            quads.Count,
            colorRuns,
            openRunColor,
            openRunIndex,
            openRunFirstQuad);

        string? materialName = font.Material?.Info.Name;
        string? glowMaterialName = font.GlowMaterial?.Info.Name;
        return new UiGlyphRunPlan(
            DisplayName(font),
            font.PixelHeight,
            materialName,
            glowMaterialName,
            normalizedScale,
            maxLineAdvance,
            penX,
            penY,
            quads,
            colorRuns,
            diagnostics);
    }

    private static void ValidateFont(
        FontAsset font,
        UiGlyphRunRequest request,
        ICollection<UiTextDiagnostic> diagnostics)
    {
        if (!float.IsFinite(request.TextScale) || request.TextScale < 0f)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidTextScale,
                UiDiagnosticSeverity.Blocker,
                $"Text scale {request.TextScale} must be finite and non-negative."));
        }
        if (!float.IsFinite(request.BaselineX) ||
            !float.IsFinite(request.BaselineY))
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidTextOrigin,
                UiDiagnosticSeverity.Blocker,
                $"Text baseline ({request.BaselineX}, {request.BaselineY}) must be finite."));
        }
        if (font.PixelHeight <= 0)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidFontPixelHeight,
                UiDiagnosticSeverity.Blocker,
                $"Font '{DisplayName(font)}' has invalid pixel height {font.PixelHeight}."));
        }
        if (font.Glyphs is null || font.Glyphs.Count == 0)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.FontGlyphTableEmpty,
                UiDiagnosticSeverity.Blocker,
                $"Font '{DisplayName(font)}' has no glyph table."));
        }
        else if (font.GlyphCount != font.Glyphs.Count)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.FontGlyphCountMismatch,
                UiDiagnosticSeverity.Blocker,
                $"Font '{DisplayName(font)}' declares {font.GlyphCount} glyphs but exposes {font.Glyphs.Count}."));
        }

        if (font.Material is null)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.FontMaterialMissing,
                UiDiagnosticSeverity.Blocker,
                $"Font '{DisplayName(font)}' has no resolved material."));
        }
        else if (string.IsNullOrWhiteSpace(font.Material.Info.Name))
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.FontMaterialNameMissing,
                UiDiagnosticSeverity.Blocker,
                $"Font '{DisplayName(font)}' resolves a material without an identity."));
        }
    }

    private static bool ValidateNativeGlyphTable(
        IReadOnlyList<FontGlyph> glyphs,
        ICollection<UiTextDiagnostic> diagnostics)
    {
        if (glyphs.Count < NativeAsciiGlyphCount)
        {
            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidNativeGlyphTable,
                UiDiagnosticSeverity.Blocker,
                "The native Font glyph table requires 96 direct ASCII " +
                $"entries before its searchable suffix; found {glyphs.Count}."));
            return false;
        }

        for (int ordinal = 0; ordinal < NativeAsciiGlyphCount; ordinal++)
        {
            ushort expected = checked((ushort)(0x20 + ordinal));
            if (glyphs[ordinal].Letter == expected)
                continue;

            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidNativeGlyphTable,
                UiDiagnosticSeverity.Blocker,
                $"Native Font glyph ordinal {ordinal} contains " +
                $"U+{glyphs[ordinal].Letter:X4}; expected U+{expected:X4}."));
            return false;
        }

        for (int ordinal = NativeAsciiGlyphCount + 1;
             ordinal < glyphs.Count;
             ordinal++)
        {
            if (glyphs[ordinal - 1].Letter <= glyphs[ordinal].Letter)
                continue;

            diagnostics.Add(new UiTextDiagnostic(
                UiTextDiagnosticCode.InvalidNativeGlyphTable,
                UiDiagnosticSeverity.Blocker,
                "The native Font glyph suffix must be sorted by letter for " +
                $"binary search, but ordinal {ordinal - 1} is " +
                $"U+{glyphs[ordinal - 1].Letter:X4} and ordinal {ordinal} is " +
                $"U+{glyphs[ordinal].Letter:X4}."));
            return false;
        }

        return true;
    }

    private static bool TryResolveNativeGlyph(
        IReadOnlyList<FontGlyph> glyphs,
        ushort letter,
        out FontGlyph? glyph)
    {
        if (letter is >= 0x20 and <= 0x7F)
        {
            glyph = glyphs[letter - 0x20];
            return true;
        }

        int bottom = NativeAsciiGlyphCount;
        int top = glyphs.Count - 1;
        while (bottom <= top)
        {
            int middle = bottom + ((top - bottom) / 2);
            FontGlyph candidate = glyphs[middle];
            if (candidate.Letter == letter)
            {
                glyph = candidate;
                return true;
            }

            if (candidate.Letter >= letter)
                top = middle - 1;
            else
                bottom = middle + 1;
        }

        glyph = null;
        return false;
    }

    private static int EnsureColorRun(
        char? selectedColor,
        int nextQuadIndex,
        ICollection<UiGlyphColorRun> completedRuns,
        ref char? openColor,
        ref int openRunIndex,
        ref int openRunFirstQuad)
    {
        if (openRunIndex >= 0 && openColor == selectedColor)
            return openRunIndex;

        if (openRunIndex >= 0)
        {
            completedRuns.Add(new UiGlyphColorRun(
                openRunFirstQuad,
                nextQuadIndex - openRunFirstQuad,
                openColor));
        }

        openColor = selectedColor;
        openRunFirstQuad = nextQuadIndex;
        openRunIndex = completedRuns.Count;
        return openRunIndex;
    }

    private static void CloseColorRun(
        int nextQuadIndex,
        ICollection<UiGlyphColorRun> completedRuns,
        char? openColor,
        int openRunIndex,
        int openRunFirstQuad)
    {
        if (openRunIndex < 0)
            return;

        completedRuns.Add(new UiGlyphColorRun(
            openRunFirstQuad,
            nextQuadIndex - openRunFirstQuad,
            openColor));
    }

    private static bool IsCaretColorEscape(string text, int index) =>
        text[index] == '^' &&
        index + 1 < text.Length &&
        text[index + 1] is >= '0' and <= '9';

    private static bool IsInlineMaterialCommand(string text, int index) =>
        text[index] == '^' &&
        index + 1 < text.Length &&
        text[index + 1] is '\u0001' or '\u0002';

    private static bool IsValidScale(
        FontAsset font,
        UiGlyphRunRequest request) =>
        font.PixelHeight > 0 &&
        float.IsFinite(request.TextScale) &&
        request.TextScale >= 0f;

    private static bool IsValidTextureRect(UiGlyphTextureRect value) =>
        float.IsFinite(value.S0) &&
        float.IsFinite(value.T0) &&
        float.IsFinite(value.S1) &&
        float.IsFinite(value.T1) &&
        value.S0 is >= 0f and <= 1f &&
        value.T0 is >= 0f and <= 1f &&
        value.S1 is >= 0f and <= 1f &&
        value.T1 is >= 0f and <= 1f &&
        value.S1 >= value.S0 &&
        value.T1 >= value.T0;

    private static string DisplayName(FontAsset font) =>
        string.IsNullOrWhiteSpace(font.Name) ? "<unnamed font>" : font.Name;
}
