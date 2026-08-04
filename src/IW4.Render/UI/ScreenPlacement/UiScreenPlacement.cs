using IW4.Assets.Assets.Menu;

namespace IW4.Render.UI.ScreenPlacement;

/// <summary>
/// One affine axis of IW4's native ScreenPlacement transform.
/// Positions receive both scale and origin; lengths receive scale only.
/// </summary>
public readonly record struct UiScreenAxisPlacement(float Scale, float Origin)
{
    public float ApplyPosition(float value) => value * Scale + Origin;

    public float ApplyLength(float value) => value * Scale;

    public float RemovePosition(float value) => (value - Origin) / Scale;

    public float RemoveLength(float value) => value / Scale;
}

public readonly record struct UiScreenInsets(
    float Left,
    float Top,
    float Right,
    float Bottom);

/// <summary>
/// Renderer-neutral implementation of IW4's PS3 ScreenPlacement state.
/// Values are physical output pixels and reproduce ScrPlace_ApplyRect,
/// including the PS3 adjustable-safe-area alignment cases 8 through 10.
/// </summary>
public sealed class UiScreenPlacement
{
    private const float VirtualWidthValue = 640f;
    private const float VirtualHeightValue = 480f;

    private UiScreenPlacement(
        float outputWidth,
        float outputHeight,
        float virtualToRealX,
        float virtualToRealY,
        float virtualToFullX,
        float virtualToFullY,
        float realToVirtualX,
        float realToVirtualY,
        float subScreenLeft,
        float viewableMinX,
        float viewableMinY,
        float viewableMaxX,
        float viewableMaxY,
        float adjustableMinX,
        float adjustableMinY,
        float adjustableMaxX,
        float adjustableMaxY)
    {
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        VirtualToRealX = virtualToRealX;
        VirtualToRealY = virtualToRealY;
        VirtualToFullX = virtualToFullX;
        VirtualToFullY = virtualToFullY;
        RealToVirtualX = realToVirtualX;
        RealToVirtualY = realToVirtualY;
        SubScreenLeft = subScreenLeft;
        ViewableMinX = viewableMinX;
        ViewableMinY = viewableMinY;
        ViewableMaxX = viewableMaxX;
        ViewableMaxY = viewableMaxY;
        AdjustableMinX = adjustableMinX;
        AdjustableMinY = adjustableMinY;
        AdjustableMaxX = adjustableMaxX;
        AdjustableMaxY = adjustableMaxY;
    }

    /// <summary>
    /// Native default_mp PS3 placement for the game's 1280x720 output.
    /// Both regular and profile-adjustable safe areas use their registered
    /// default of 85 percent.
    /// </summary>
    public static UiScreenPlacement Iw4Ps3Hd { get; } = new(
        outputWidth: 1280f,
        outputHeight: 720f,
        virtualToRealX: 1.5f,
        virtualToRealY: 1.5f,
        virtualToFullX: 2f,
        virtualToFullY: 1.5f,
        realToVirtualX: 2f / 3f,
        realToVirtualY: 2f / 3f,
        subScreenLeft: 160f,
        viewableMinX: 96f,
        viewableMinY: 54f,
        viewableMaxX: 1184f,
        viewableMaxY: 666f,
        adjustableMinX: 96f,
        adjustableMinY: 54f,
        adjustableMaxX: 1184f,
        adjustableMaxY: 666f);

    public float VirtualWidth => VirtualWidthValue;

    public float VirtualHeight => VirtualHeightValue;

    public float OutputWidth { get; }

    public float OutputHeight { get; }

    public float VirtualToRealX { get; }

    public float VirtualToRealY { get; }

    public float VirtualToFullX { get; }

    public float VirtualToFullY { get; }

    public float RealToVirtualX { get; }

    public float RealToVirtualY { get; }

    public float SubScreenLeft { get; }

    public float ViewableMinX { get; }

    public float ViewableMinY { get; }

    public float ViewableMaxX { get; }

    public float ViewableMaxY { get; }

    public float AdjustableMinX { get; }

    public float AdjustableMinY { get; }

    public float AdjustableMaxX { get; }

    public float AdjustableMaxY { get; }

    public UiScreenInsets ViewableInsets => new(
        ViewableMinX,
        ViewableMinY,
        OutputWidth - ViewableMaxX,
        OutputHeight - ViewableMaxY);

    public UiScreenInsets AdjustableInsets => new(
        AdjustableMinX,
        AdjustableMinY,
        OutputWidth - AdjustableMaxX,
        OutputHeight - AdjustableMaxY);

    public UiScreenAxisPlacement Resolve(HorizontalAlign alignment) =>
        alignment switch
        {
            HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT =>
                new UiScreenAxisPlacement(VirtualToRealX, SubScreenLeft),
            HorizontalAlign.HORIZONTAL_ALIGN_LEFT =>
                new UiScreenAxisPlacement(VirtualToRealX, ViewableMinX),
            HorizontalAlign.HORIZONTAL_ALIGN_CENTER =>
                new UiScreenAxisPlacement(
                    VirtualToRealX,
                    OutputWidth * 0.5f),
            HorizontalAlign.HORIZONTAL_ALIGN_RIGHT =>
                new UiScreenAxisPlacement(VirtualToRealX, ViewableMaxX),
            HorizontalAlign.HORIZONTAL_ALIGN_FULLSCREEN =>
                new UiScreenAxisPlacement(VirtualToFullX, 0),
            HorizontalAlign.HORIZONTAL_ALIGN_NOSCALE =>
                new UiScreenAxisPlacement(1, 0),
            HorizontalAlign.HORIZONTAL_ALIGN_TO640 =>
                new UiScreenAxisPlacement(RealToVirtualX, 0),
            HorizontalAlign.HORIZONTAL_ALIGN_CENTER_SAFEAREA =>
                new UiScreenAxisPlacement(
                    VirtualToRealX,
                    (ViewableMinX + ViewableMaxX) * 0.5f),
            HorizontalAlign.HORIZONTAL_ALIGN_LEFT_ADJUSTABLE =>
                new UiScreenAxisPlacement(VirtualToRealX, AdjustableMinX),
            HorizontalAlign.HORIZONTAL_ALIGN_CENTER_ADJUSTABLE =>
                new UiScreenAxisPlacement(
                    VirtualToRealX,
                    (AdjustableMinX + AdjustableMaxX) * 0.5f),
            HorizontalAlign.HORIZONTAL_ALIGN_RIGHT_ADJUSTABLE =>
                new UiScreenAxisPlacement(VirtualToRealX, AdjustableMaxX),
            _ => new UiScreenAxisPlacement(VirtualToRealX, SubScreenLeft)
        };

    public UiScreenAxisPlacement Resolve(VerticalAlign alignment) =>
        alignment switch
        {
            VerticalAlign.VERTICAL_ALIGN_SUBTOP =>
                new UiScreenAxisPlacement(VirtualToRealY, 0),
            VerticalAlign.VERTICAL_ALIGN_TOP =>
                new UiScreenAxisPlacement(VirtualToRealY, ViewableMinY),
            VerticalAlign.VERTICAL_ALIGN_CENTER =>
                new UiScreenAxisPlacement(
                    VirtualToRealY,
                    OutputHeight * 0.5f),
            VerticalAlign.VERTICAL_ALIGN_BOTTOM =>
                new UiScreenAxisPlacement(VirtualToRealY, ViewableMaxY),
            VerticalAlign.VERTICAL_ALIGN_FULLSCREEN =>
                new UiScreenAxisPlacement(VirtualToFullY, 0),
            VerticalAlign.VERTICAL_ALIGN_NOSCALE =>
                new UiScreenAxisPlacement(1, 0),
            VerticalAlign.VERTICAL_ALIGN_TO480 =>
                new UiScreenAxisPlacement(RealToVirtualY, 0),
            VerticalAlign.VERTICAL_ALIGN_CENTER_SAFEAREA =>
                new UiScreenAxisPlacement(
                    VirtualToRealY,
                    (ViewableMinY + ViewableMaxY) * 0.5f),
            VerticalAlign.VERTICAL_ALIGN_TOP_ADJUSTABLE =>
                new UiScreenAxisPlacement(VirtualToRealY, AdjustableMinY),
            VerticalAlign.VERTICAL_ALIGN_MIDDLE_ADJUSTABLE =>
                new UiScreenAxisPlacement(
                    VirtualToRealY,
                    (AdjustableMinY + AdjustableMaxY) * 0.5f),
            VerticalAlign.VERTICAL_ALIGN_BOTTOM_ADJUSTABLE =>
                new UiScreenAxisPlacement(VirtualToRealY, AdjustableMaxY),
            _ => new UiScreenAxisPlacement(VirtualToRealY, 0)
        };
}
