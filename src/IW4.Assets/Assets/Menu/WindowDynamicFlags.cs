namespace IW4.Assets.Assets.Menu;

[Flags]
public enum WindowDynamicFlags : int
{
    None = 0,

    /// <summary>
    /// Window currently has UI focus.
    /// </summary>
    WINDOW_DYNAMIC_HAS_FOCUS = 0x00000002,

    /// <summary>
    /// Window is visible for the local client.
    /// </summary>
    WINDOW_DYNAMIC_VISIBLE = 0x00000004,

    /// <summary>
    /// Use window.foreColor instead of the default UI foreground color.
    /// </summary>
    WINDOW_DYNAMIC_HAS_FORECOLOR = 0x00010000
}
