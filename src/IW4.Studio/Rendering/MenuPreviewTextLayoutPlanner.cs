using IW4.Render.UI.Text;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Preview;

namespace IW4.Studio.Rendering;

/// <summary>
/// Resolves localization and Font assets, then reproduces IW4's
/// Item_SetTextExtents placement in the 640x480 virtual coordinate space.
/// The result remains renderer-neutral; a presentation backend only has to
/// draw the planned atlas quads.
/// </summary>
public static class MenuPreviewTextLayoutPlanner
{
    public static MenuPreviewTextLayout Plan(
        MenuPreviewText text,
        IMenuTextResourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(resolver);

        long revision = resolver.Revision;
        MenuLocalizedTextResolution localized = resolver.ResolveText(text.Text);
        MenuFontAssetResolution font = resolver.ResolveFont(text.Font);
        var diagnostics = new List<string>();
        if (localized.Status == MenuLocalizationStatus.Missing)
        {
            diagnostics.Add(
                localized.Failure ??
                $"Localization '{localized.LookupName}' is unavailable.");
        }
        diagnostics.AddRange(font.Diagnostics.Select(value => value.Message));

        if (localized.PoolRevision != revision ||
            font.PoolRevision != revision ||
            resolver.Revision != revision)
        {
            diagnostics.Add(
                "The asset pool changed while Menu text resources were being resolved.");
            return MenuPreviewTextLayout.Fallback(
                text,
                localized.DisplayText,
                diagnostics,
                revision);
        }

        if (font.Font is null)
        {
            return MenuPreviewTextLayout.Fallback(
                text,
                localized.DisplayText,
                diagnostics,
                revision);
        }

        UiGlyphRunPlan measurement = UiGlyphRunPlanner.Plan(
            font.Font,
            new UiGlyphRunRequest(localized.DisplayText, 0, 0, text.Scale));
        diagnostics.AddRange(measurement.Diagnostics.Select(value => value.Message));
        if (!measurement.CanRender)
        {
            return MenuPreviewTextLayout.Fallback(
                text,
                localized.DisplayText,
                diagnostics.Distinct(StringComparer.Ordinal),
                revision);
        }

        float textWidth = MathF.Truncate(measurement.MaxLineAdvance);
        float textHeight = MathF.Truncate(
            measurement.PixelHeight * measurement.NormalizedScale);
        float baselineX = text.Bounds.X + text.BorderInset + text.OffsetX +
            HorizontalAdjustment(text.Alignment & 3, text.Bounds.Width, textWidth);
        float baselineY = text.Bounds.Y + text.BorderInset + text.OffsetY +
            VerticalAdjustment(
                text.Alignment & 0xC,
                text.Bounds.Height,
                textHeight);

        if ((text.Alignment & 3) is not (0 or 1 or 2))
        {
            diagnostics.Add(
                $"Horizontal text alignment {text.Alignment & 3} is invalid; IW4's asserted right-aligned branch is shown.");
        }
        if ((text.Alignment & 0xC) is not (0 or 4 or 8 or 12))
        {
            diagnostics.Add(
                $"Vertical text alignment {text.Alignment & 0xC} is invalid; IW4's asserted bottom-aligned branch is shown.");
        }

        UiGlyphRunPlan glyphRun = UiGlyphRunPlanner.Plan(
            font.Font,
            new UiGlyphRunRequest(
                localized.DisplayText,
                baselineX,
                baselineY,
                text.Scale));
        foreach (UiGlyphColorRun colorRun in glyphRun.ColorRuns)
        {
            if (colorRun.CaretColorCode is '8' or '9')
            {
                diagnostics.Add(
                    $"Caret color ^{colorRun.CaretColorCode} requires scenario team colors; the authored text color is shown.");
            }
        }

        return MenuPreviewTextLayout.Resolved(
            text,
            localized.DisplayText,
            glyphRun,
            diagnostics.Distinct(StringComparer.Ordinal),
            revision);
    }

    public static MenuColorValue ResolveGlyphColor(
        MenuColorValue baseColor,
        char? caretColorCode)
    {
        (float R, float G, float B)? rgb = caretColorCode switch
        {
            '0' => (0, 0, 0),
            '1' => (1, 0.36f, 0.36f),
            '2' => (0, 1, 0),
            '3' => (1, 1, 0),
            '4' => (0, 0, 1),
            '5' => (0, 1, 1),
            '6' => (1, 0.36f, 1),
            _ => null
        };
        return rgb is { } selected
            ? new MenuColorValue(
                baseColor.A,
                selected.R,
                selected.G,
                selected.B)
            : baseColor;
    }

    private static float HorizontalAdjustment(
        int alignment,
        float containerWidth,
        float textWidth) => alignment switch
    {
        0 => 0,
        1 => (containerWidth - textWidth) * 0.5f,
        _ => containerWidth - textWidth
    };

    private static float VerticalAdjustment(
        int alignment,
        float containerHeight,
        float textHeight) => alignment switch
    {
        0 => 0,
        4 => textHeight,
        8 => (containerHeight + textHeight) * 0.5f,
        _ => containerHeight
    };
}

public sealed class MenuPreviewTextLayout
{
    private readonly string[] _diagnostics;

    private MenuPreviewTextLayout(
        MenuPreviewText source,
        string displayText,
        UiGlyphRunPlan? glyphRun,
        IEnumerable<string> diagnostics,
        long poolRevision)
    {
        Source = source;
        DisplayText = displayText;
        GlyphRun = glyphRun;
        _diagnostics = diagnostics.ToArray();
        Diagnostics = Array.AsReadOnly(_diagnostics);
        PoolRevision = poolRevision;
    }

    public MenuPreviewText Source { get; }

    public string DisplayText { get; }

    public UiGlyphRunPlan? GlyphRun { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public long PoolRevision { get; }

    public bool UsesGameGlyphs => GlyphRun?.CanRender == true;

    internal static MenuPreviewTextLayout Resolved(
        MenuPreviewText source,
        string displayText,
        UiGlyphRunPlan glyphRun,
        IEnumerable<string> diagnostics,
        long poolRevision) =>
        new(source, displayText, glyphRun, diagnostics, poolRevision);

    internal static MenuPreviewTextLayout Fallback(
        MenuPreviewText source,
        string displayText,
        IEnumerable<string> diagnostics,
        long poolRevision) =>
        new(source, displayText, null, diagnostics, poolRevision);
}
