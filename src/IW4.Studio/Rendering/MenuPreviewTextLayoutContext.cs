namespace IW4.Studio.Rendering;

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

    /// <summary>
    /// Creates the profile used by IW4 on a PS3 720p output: a 640x480
    /// authored canvas placed into a 1280x720 target viewport, with the
    /// registered UI font thresholds.
    /// </summary>
    public static MenuPreviewTextLayoutContext CreateIw4Ps3Hd() => new(
        virtualCanvasWidth: 640f,
        virtualCanvasHeight: 480f,
        targetViewportWidth: 1280f,
        targetViewportHeight: 720f,
        smallFontThreshold: 0.25f,
        bigFontThreshold: 0.4f,
        extraBigFontThreshold: 0.55f);

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
