namespace IW4.Assets.Assets.Menu;

public enum HorizontalAlign : byte
{
    /// <summary>
    /// Anchor X to the left edge of the 4:3 virtual screen, before safe-area adjustment.
    /// This is also the default horizontal rect alignment.
    /// </summary>
    HORIZONTAL_ALIGN_SUBLEFT = 0,

    /// <summary>
    /// Anchor X to the left viewable safe-area edge.
    /// </summary>
    HORIZONTAL_ALIGN_LEFT = 1,

    /// <summary>
    /// Anchor X to the horizontal center of the screen.
    /// </summary>
    HORIZONTAL_ALIGN_CENTER = 2,

    /// <summary>
    /// Anchor X to the right viewable safe-area edge.
    /// The rect offset is applied from the right side, so preview code should subtract width and X.
    /// </summary>
    HORIZONTAL_ALIGN_RIGHT = 3,

    /// <summary>
    /// Use the full horizontal screen span, disregarding safe-area adjustment.
    /// Preview code should treat the resulting X as the screen left and width as the full available width.
    /// </summary>
    HORIZONTAL_ALIGN_FULLSCREEN = 4,

    /// <summary>
    /// Use exact X and width parameters without safe-area adjustment or screen-size scaling.
    /// </summary>
    HORIZONTAL_ALIGN_NOSCALE = 5,

    /// <summary>
    /// Scale a real-screen-resolution X coordinate down into the 0..640 virtual coordinate range.
    /// </summary>
    HORIZONTAL_ALIGN_TO640 = 6,

    /// <summary>
    /// Anchor X to the horizontal center of the safe area.
    /// </summary>
    HORIZONTAL_ALIGN_CENTER_SAFEAREA = 7,

    // Unnamed wire values are preserved without assigning another alignment.
    HORIZONTAL_ALIGN_PS3_RAW_8 = 8,
    HORIZONTAL_ALIGN_PS3_RAW_9 = 9,
    HORIZONTAL_ALIGN_PS3_RAW_10 = 10
}
