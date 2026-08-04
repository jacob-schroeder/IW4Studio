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
    /// The authored X is offset from that origin; width is not subtracted.
    /// </summary>
    HORIZONTAL_ALIGN_CENTER = 2,

    /// <summary>
    /// Anchor X to the right viewable safe-area edge.
    /// The authored X is added to that origin and is commonly negative.
    /// </summary>
    HORIZONTAL_ALIGN_RIGHT = 3,

    /// <summary>
    /// Apply the full-screen placement scale without safe-area adjustment.
    /// On the editor's canonical 640-wide canvas, authored X and width remain unchanged.
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
    /// The authored X is offset from that origin; width is not subtracted.
    /// </summary>
    HORIZONTAL_ALIGN_CENTER_SAFEAREA = 7,

    // Unnamed wire values are preserved without assigning another alignment.
    HORIZONTAL_ALIGN_PS3_RAW_8 = 8,
    HORIZONTAL_ALIGN_PS3_RAW_9 = 9,
    HORIZONTAL_ALIGN_PS3_RAW_10 = 10
}
