namespace IW4.Assets.Assets.Menu;

public enum VerticalAlign : byte
{
    /// <summary>
    /// Anchor Y to the top edge of the 4:3 virtual screen, before safe-area adjustment.
    /// This is also the default vertical rect alignment.
    /// </summary>
    VERTICAL_ALIGN_SUBTOP = 0,

    /// <summary>
    /// Anchor Y to the top viewable safe-area edge.
    /// </summary>
    VERTICAL_ALIGN_TOP = 1,

    /// <summary>
    /// Anchor Y to the vertical center of the screen.
    /// The authored Y is offset from that origin; height is not subtracted.
    /// </summary>
    VERTICAL_ALIGN_CENTER = 2,

    /// <summary>
    /// Anchor Y to the bottom viewable safe-area edge.
    /// The authored Y is added to that origin and is commonly negative.
    /// </summary>
    VERTICAL_ALIGN_BOTTOM = 3,

    /// <summary>
    /// Apply the full-screen placement scale without safe-area adjustment.
    /// On the editor's canonical 480-high canvas, authored Y and height remain unchanged.
    /// </summary>
    VERTICAL_ALIGN_FULLSCREEN = 4,

    /// <summary>
    /// Use exact Y and height parameters without safe-area adjustment or screen-size scaling.
    /// </summary>
    VERTICAL_ALIGN_NOSCALE = 5,

    /// <summary>
    /// Scale a real-screen-resolution Y coordinate down into the 0..480 virtual coordinate range.
    /// </summary>
    VERTICAL_ALIGN_TO480 = 6,

    /// <summary>
    /// Anchor Y to the vertical center of the safe area.
    /// The authored Y is offset from that origin; height is not subtracted.
    /// </summary>
    VERTICAL_ALIGN_CENTER_SAFEAREA = 7,

    // Unnamed wire values are preserved without assigning another alignment.
    VERTICAL_ALIGN_PS3_RAW_8 = 8,
    VERTICAL_ALIGN_PS3_RAW_9 = 9,
    VERTICAL_ALIGN_PS3_RAW_10 = 10
}
