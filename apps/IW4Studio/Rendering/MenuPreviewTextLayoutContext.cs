using IW4.Render.UI.ScreenPlacement;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Describes the IW4 target viewport and registered font-selection values
/// used while planning renderer-neutral Menu text.
/// </summary>
public sealed class MenuPreviewTextLayoutContext
{
    public MenuPreviewTextLayoutContext(
        float virtualCanvasWidth,
        float virtualCanvasHeight,
        float targetViewportWidth,
        float targetViewportHeight,
        float smallFontThreshold,
        float bigFontThreshold,
        float extraBigFontThreshold)
    {
        ValidatePositiveFinite(
            virtualCanvasWidth,
            nameof(virtualCanvasWidth));
        ValidatePositiveFinite(
            virtualCanvasHeight,
            nameof(virtualCanvasHeight));
        ValidatePositiveFinite(
            targetViewportWidth,
            nameof(targetViewportWidth));
        ValidatePositiveFinite(
            targetViewportHeight,
            nameof(targetViewportHeight));
        ValidateNonNegativeFinite(
            smallFontThreshold,
            nameof(smallFontThreshold));
        ValidateNonNegativeFinite(
            bigFontThreshold,
            nameof(bigFontThreshold));
        ValidateNonNegativeFinite(
            extraBigFontThreshold,
            nameof(extraBigFontThreshold));
        float virtualToPhysicalScaleY =
            targetViewportHeight / virtualCanvasHeight;
        if (!float.IsFinite(virtualToPhysicalScaleY) ||
            virtualToPhysicalScaleY <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetViewportHeight),
                "The virtual-to-physical Y scale must be finite.");
        }

        VirtualCanvasWidth = virtualCanvasWidth;
        VirtualCanvasHeight = virtualCanvasHeight;
        TargetViewportWidth = targetViewportWidth;
        TargetViewportHeight = targetViewportHeight;
        SmallFontThreshold = smallFontThreshold;
        BigFontThreshold = bigFontThreshold;
        ExtraBigFontThreshold = extraBigFontThreshold;
    }

    public float VirtualCanvasWidth { get; }

    public float VirtualCanvasHeight { get; }

    public float TargetViewportWidth { get; }

    public float TargetViewportHeight { get; }

    public float SmallFontThreshold { get; }

    public float BigFontThreshold { get; }

    public float ExtraBigFontThreshold { get; }

    public float VirtualToPhysicalScaleY =>
        TargetViewportHeight / VirtualCanvasHeight;

    public static MenuPreviewTextLayoutContext FromScreenPlacement(
        UiScreenPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return new MenuPreviewTextLayoutContext(
            placement.VirtualWidth,
            placement.VirtualHeight,
            placement.OutputWidth,
            placement.OutputHeight,
            smallFontThreshold: 0.25f,
            bigFontThreshold: 0.4f,
            extraBigFontThreshold: 0.55f);
    }

    public MenuFontSelectionContext CreateFontSelectionContext(
        float textScale) => new(
            textScale,
            VirtualToPhysicalScaleY,
            SmallFontThreshold,
            BigFontThreshold,
            ExtraBigFontThreshold);

    private static void ValidatePositiveFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateNonNegativeFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(name);
    }
}
