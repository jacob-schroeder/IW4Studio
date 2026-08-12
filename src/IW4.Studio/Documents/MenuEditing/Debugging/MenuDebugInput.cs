namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// One explicit debugger input. Inputs select exactly one authored hook set;
/// they do not model or infer native input-routing precedence.
/// </summary>
public abstract record MenuDebugInput;

public enum MenuDebugMenuHook
{
    Open,
    CloseRequest,
    Close,
    Escape
}

public sealed record MenuDebugMenuHookInput(MenuDebugMenuHook Hook) : MenuDebugInput;

/// <summary>
/// Selects a key binding by key and, when necessary, its index in the
/// authored linked-list order. The index may be omitted only when the key is
/// unique in the selected menu or item hook table.
/// </summary>
public readonly record struct MenuDebugKeySelection(
    int Key,
    int? AuthoredHandlerIndex = null);

public sealed record MenuDebugMenuKeyInput(MenuDebugKeySelection Selection)
    : MenuDebugInput;

public enum MenuDebugItemHook
{
    /// <summary>Selects ItemDef.mouseEnter.</summary>
    PointerEnter,

    /// <summary>Selects ItemDef.mouseExit.</summary>
    PointerExit,

    /// <summary>Selects ItemDef.mouseEnterText.</summary>
    TextPointerEnter,

    /// <summary>Selects ItemDef.mouseExitText.</summary>
    TextPointerExit,

    Focus,
    LeaveFocus,
    Action,
    Accept,
    DoubleClick
}

public sealed record MenuDebugItemHookInput(
    MenuNodeId ItemId,
    MenuDebugItemHook Hook) : MenuDebugInput;

public sealed record MenuDebugItemKeyInput(
    MenuNodeId ItemId,
    MenuDebugKeySelection Selection) : MenuDebugInput;
