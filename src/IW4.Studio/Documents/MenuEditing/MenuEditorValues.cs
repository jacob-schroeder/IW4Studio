using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Stable, editor-only identity for a selectable Menu node. It is cloned with
/// a draft and is never written to a fastfile.
/// </summary>
public readonly record struct MenuNodeId(Guid Value)
{
    public static MenuNodeId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

/// <summary>Stable identity for one ordered MenuFile registration.</summary>
public readonly record struct MenuRegistrationId(Guid Value)
{
    public static MenuRegistrationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

/// <summary>Renderer- and serialization-independent rectangle value.</summary>
public readonly record struct MenuRectangleValue(
    float X,
    float Y,
    float Width,
    float Height,
    HorizontalAlign HorizontalAlignment,
    VerticalAlign VerticalAlignment);

/// <summary>
/// Menu color with explicit semantic alpha, red, green, and blue channels.
/// The serialized Menu Vec4 stores these as R/G/B/A; conversion at the
/// document boundary keeps that raw layout out of editor and renderer code.
/// </summary>
public readonly record struct MenuColorValue(float A, float R, float G, float B);

public sealed record MenuTransitionValue(
    MenuTransitionType TransitionType,
    int TargetField,
    int StartTime,
    float StartValue,
    float EndValue,
    float Time,
    MenuTransitionEndTrigger EndTriggerType);

/// <summary>Authored Window fields. Runtime animation state is omitted.</summary>
public sealed record MenuWindowValue(
    string? Name,
    MenuRectangleValue Rect,
    MenuRectangleValue RectClient,
    string? Group,
    WindowStyle Style,
    WindowBorder Border,
    WindowOwnerDraw OwnerDraw,
    int OwnerDrawFlags,
    float BorderSize,
    WindowStaticFlags StaticFlags,
    IReadOnlyList<WindowDynamicFlags> DynamicFlags,
    MenuColorValue ForeColor,
    MenuColorValue BackColor,
    MenuColorValue BorderColor,
    MenuColorValue OutlineColor,
    MenuColorValue DisableColor,
    string? BackgroundMaterialName);

/// <summary>Authored Menu root fields that are not part of its Window.</summary>
public sealed record MenuSettingsValue(
    string? Font,
    int Fullscreen,
    int FontIndex,
    IReadOnlyList<int> CursorItems,
    int FadeCycle,
    float FadeClamp,
    float FadeAmount,
    float FadeInAmount,
    float BlurRadius,
    string? AllowedBinding,
    string? SoundName,
    int ImageTrack,
    MenuColorValue FocusColor,
    IReadOnlyList<MenuTransitionValue> ScaleTransitions,
    IReadOnlyList<MenuTransitionValue> AlphaTransitions,
    IReadOnlyList<MenuTransitionValue> XTransitions,
    IReadOnlyList<MenuTransitionValue> YTransitions);

public abstract record MenuItemPayloadValue;

/// <summary>
/// The item has no serialized type-specific payload. The selected
/// <see cref="ItemDefType"/> still determines the native union arm; for a
/// pointer-bearing type this represents that arm with a null pointer.
/// </summary>
public sealed record MenuNoItemPayloadValue : MenuItemPayloadValue
{
    public static MenuNoItemPayloadValue Instance { get; } = new();

    private MenuNoItemPayloadValue()
    {
    }
}

public sealed record MenuEditFieldPayloadValue(
    float MinValue,
    float MaxValue,
    float DefaultValue,
    float Range,
    int MaxChars,
    int MaxCharsGotoNext,
    int MaxPaintChars,
    int PaintOffset) : MenuItemPayloadValue;

public sealed record MenuListBoxColumnValue(
    int Position,
    int Width,
    int MaxChars,
    int Alignment);

public sealed record MenuListBoxPayloadValue(
    int DrawPadding,
    float ElementWidth,
    float ElementHeight,
    int ElementStyle,
    int NumColumns,
    IReadOnlyList<MenuListBoxColumnValue> Columns,
    bool NotSelectable,
    bool NoScrollbars,
    int UsePaging,
    MenuColorValue SelectBorder,
    string? SelectIconMaterialName,
    bool HasDoubleClickHandler) : MenuItemPayloadValue;

public sealed record MenuMultiEntryValue(
    string? DvarListValue,
    string? DvarStringValue,
    float NumericValue);

public sealed record MenuMultiPayloadValue(
    int Count,
    int StringDefinition,
    IReadOnlyList<MenuMultiEntryValue> Entries) : MenuItemPayloadValue;

public sealed record MenuDvarEnumPayloadValue(string? DvarName) : MenuItemPayloadValue;

public sealed record MenuNewsTickerPayloadValue(
    int FeedId,
    int Speed,
    int Spacing,
    float X) : MenuItemPayloadValue;

/// <summary>
/// TextScroll currently exposes no authored scalar fields. Its timing slot is
/// runtime state and is deliberately omitted.
/// </summary>
public sealed record MenuTextScrollPayloadValue : MenuItemPayloadValue
{
    public static MenuTextScrollPayloadValue Instance { get; } = new();

    private MenuTextScrollPayloadValue()
    {
    }
}

/// <summary>
/// Read-only summary of recursive behavior preserved by the detached source
/// graph. A dedicated behavior editor can replace this projection later.
/// </summary>
public sealed record MenuItemBehaviorSummary(
    bool HasMouseEnterText,
    bool HasMouseExitText,
    bool HasMouseEnter,
    bool HasMouseExit,
    bool HasAction,
    bool HasAccept,
    bool HasOnFocus,
    bool HasLeaveFocus,
    bool HasKeyHandlers,
    bool HasVisibleExpression,
    bool HasDisabledExpression,
    bool HasTextExpression,
    bool HasMaterialExpression,
    int FloatExpressionCount);

/// <summary>Editable authored values for one ItemDef.</summary>
public sealed record MenuItemValue(
    MenuWindowValue Window,
    IReadOnlyList<MenuRectangleValue> TextRectangles,
    ItemDefType Type,
    int DataType,
    int Align,
    int FontEnum,
    int TextAlignMode,
    float TextAlignX,
    float TextAlignY,
    float TextScale,
    int TextStyle,
    int GameMessageWindowIndex,
    int GameMessageWindowMode,
    string? Text,
    int TextSaveGameInfo,
    string? Dvar,
    string? DvarTest,
    string? EnableDvar,
    int DvarFlags,
    string? FocusSoundName,
    float Special,
    IReadOnlyList<int> CursorPositions,
    int ImageTrack,
    MenuColorValue GlowColor,
    byte DecayActive,
    MenuItemPayloadValue Payload,
    MenuItemBehaviorSummary Behavior);

public sealed record MenuBehaviorSummary(
    bool HasOnOpen,
    bool HasOnCloseRequest,
    bool HasOnClose,
    bool HasOnEscape,
    bool HasKeyHandlers,
    bool HasVisibleExpression,
    bool HasRectXExpression,
    bool HasRectYExpression,
    bool HasRectWidthExpression,
    bool HasRectHeightExpression,
    bool HasExpressionSupportingData);
