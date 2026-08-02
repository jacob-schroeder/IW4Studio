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
    /// </summary>
    VERTICAL_ALIGN_CENTER = 2,

    /// <summary>
    /// Anchor Y to the bottom viewable safe-area edge.
    /// The rect offset is applied from the bottom side, so preview code should subtract height and Y.
    /// </summary>
    VERTICAL_ALIGN_BOTTOM = 3,

    /// <summary>
    /// Use the full vertical screen span, disregarding safe-area adjustment.
    /// Preview code should treat the resulting Y as the screen top and height as the full available height.
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
    /// </summary>
    VERTICAL_ALIGN_CENTER_SAFEAREA = 7,

    // Unnamed wire values are preserved without assigning another alignment.
    VERTICAL_ALIGN_PS3_RAW_8 = 8,
    VERTICAL_ALIGN_PS3_RAW_9 = 9,
    VERTICAL_ALIGN_PS3_RAW_10 = 10
}
