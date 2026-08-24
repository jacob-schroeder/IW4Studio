namespace IW4.Assets.Assets.Menu;

[Flags]
public enum WindowStaticFlags : int
{
    None = 0,

    /// <summary>
    /// Window is decorative and does not accept input.
    /// </summary>
    WINDOW_STATIC_DECORATION = 0x00100000,

    /// <summary>
    /// Window uses horizontal layout or scrolling.
    /// </summary>
    WINDOW_STATIC_HORIZONTAL = 0x00200000,

    /// <summary>
    /// Window coordinates are screen-space.
    /// </summary>
    WINDOW_STATIC_SCREEN_SPACE = 0x00400000,

    /// <summary>
    /// Route item text through the wrapped-text rectangle path.
    /// </summary>
    WINDOW_STATIC_AUTOWRAPPED = 0x00800000,

    /// <summary>
    /// Window is authored with the popup menu keyword.
    /// </summary>
    WINDOW_STATIC_POPUP = 0x01000000,

    /// <summary>
    /// Window is authored with the outOfBoundsClick menu keyword.
    /// </summary>
    WINDOW_STATIC_OUT_OF_BOUNDS_CLICK = 0x02000000,

    /// <summary>
    /// Apply legacy split-screen placement scaling while drawing.
    /// </summary>
    WINDOW_STATIC_LEGACY_SPLITSCREEN_SCALE = 0x04000000,

    /// <summary>
    /// Hide while the matching local-client flash state is active.
    /// </summary>
    WINDOW_STATIC_HIDDEN_DURING_FLASH = 0x10000000,

    /// <summary>
    /// Hide while the matching local-client scope state is active.
    /// </summary>
    WINDOW_STATIC_HIDDEN_DURING_SCOPE = 0x20000000,

    /// <summary>
    /// Hide while the matching local-client UI/HUD state is active.
    /// </summary>
    WINDOW_STATIC_HIDDEN_DURING_UI = 0x40000000,

    /// <summary>
    /// Window is authored with the textOnlyFocus menu keyword.
    /// </summary>
    WINDOW_STATIC_TEXT_ONLY_FOCUS = unchecked((int)0x80000000)
}
