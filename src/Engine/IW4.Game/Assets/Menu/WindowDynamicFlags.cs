namespace IW4.Game.Assets.Menu;

/// <remarks>
/// Additional PC-correlated interaction flags are identified on their members.
/// Source: OpenAssetTools commit 30d2d498bd8e3f2401037b24f3db6f96d39554c4,
/// src/Common/Game/IW4/IW4_Assets.h, WindowDefDynamicFlag.
/// </remarks>
[Flags]
public enum WindowDynamicFlags : int
{
    None = 0,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_MOUSE_OVER = 0x00000001,

    /// <summary>
    /// Window currently has UI focus.
    /// </summary>
    WINDOW_DYNAMIC_HAS_FOCUS = 0x00000002,

    /// <summary>
    /// Window is visible for the local client.
    /// </summary>
    WINDOW_DYNAMIC_VISIBLE = 0x00000004,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_FADING_OUT = 0x00000010,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_FADING_IN = 0x00000020,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_MOUSE_OVER_TEXT = 0x00000040,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_LISTBOX_PREV_ARROW = 0x00000100,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_LISTBOX_NEXT_ARROW = 0x00000200,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_LISTBOX_THUMB = 0x00000400,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_LISTBOX_PREV_PAGE = 0x00000800,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_LISTBOX_NEXT_PAGE = 0x00001000,

    /// <summary>PC-derived flag; PS3 interaction semantics are not confirmed.</summary>
    WINDOW_DYNAMIC_NON_DEFAULT_BACKCOLOR = 0x00008000,

    /// <summary>
    /// Use window.foreColor instead of the default UI foreground color.
    /// </summary>
    WINDOW_DYNAMIC_HAS_FORECOLOR = 0x00010000
}
