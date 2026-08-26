using System.Collections.Immutable;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>The eight fixed ItemDef event-set fields.</summary>
public enum MenuItemBehaviorHook
{
    MouseEnterText,
    MouseExitText,
    MouseEnter,
    MouseExit,
    Action,
    Accept,
    OnFocus,
    LeaveFocus
}

public enum MenuBehaviorLocalValueType
{
    Boolean,
    Integer,
    Float,
    String
}

public enum MenuBehaviorValidationMode
{
    /// <summary>Keep imported malformed/opaque shapes representable as warnings.</summary>
    Imported,
    /// <summary>Reject malformed/opaque shapes when creating new authored content.</summary>
    Authored
}

public enum MenuBehaviorValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record MenuBehaviorValidationIssue(
    string Path,
    string Message,
    MenuBehaviorValidationSeverity Severity);

/// <summary>
/// Raw handler-root fields that are not behavior semantics but must survive a
/// no-edit import/export. The byte padding is part of the serialized shape.
/// </summary>
public sealed record MenuBehaviorRawEventHandlerShape(
    XPointerReference EventDataPointer,
    byte Pad05,
    byte Pad06,
    byte Pad07)
{
    public static MenuBehaviorRawEventHandlerShape Empty { get; } = new(
        default,
        0,
        0,
        0);
}

/// <summary>One immutable ordered pointer-table entry in an event-handler set.</summary>
public sealed record MenuBehaviorEventHandlerEntry(
    MenuBehaviorEventHandler? Handler,
    XPointer<MenuEventHandler> SourcePointer)
{
    /// <summary>
    /// Captures a malformed imported event row which may be retained only when
    /// it is still the exact row at its original position. This is intentionally
    /// not a general imported marker: copied records must not authorize newly
    /// authored malformed behavior.
    /// </summary>
    internal MenuBehaviorImportedEventHandlerShape? ImportedShape { get; init; }

    public static MenuBehaviorEventHandlerEntry Create(
        MenuBehaviorEventHandler handler) => new(handler, default);
}

/// <summary>
/// Identity-based provenance for one imported event row. Its handler identity
/// and ordinal define the shape required to retain an orphan else or opaque
/// handler without turning that allowance into a general authoring escape
/// hatch. Neighbor content is deliberately not included: it is unrelated to
/// whether this row itself remains losslessly preserved.
/// </summary>
internal sealed class MenuBehaviorImportedEventHandlerShape(
    MenuBehaviorEventHandler originalHandler,
    XPointer<MenuEventHandler> originalSourcePointer,
    int originalIndex)
{
    public bool Matches(
        MenuBehaviorEventHandlerEntry entry,
        int index) =>
        index == originalIndex &&
        entry.SourcePointer.Equals(originalSourcePointer) &&
        ReferenceEquals(entry.Handler, originalHandler);
}

/// <summary>
/// An immutable ordered handler set. The source pointer is retained for
/// no-edit shape preservation; authoring always treats the list order as the
/// semantic order.
/// </summary>
public sealed class MenuBehaviorEventHandlerSet
{
    public MenuBehaviorEventHandlerSet(
        IEnumerable<MenuBehaviorEventHandlerEntry> handlers,
        XPointer<XPointer<MenuEventHandler>[]> handlerTablePointer = default)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        Handlers = handlers.ToImmutableArray();
        HandlerTablePointer = handlerTablePointer;
    }

    public ImmutableArray<MenuBehaviorEventHandlerEntry> Handlers { get; }

    public XPointer<XPointer<MenuEventHandler>[]> HandlerTablePointer { get; }

    public static MenuBehaviorEventHandlerSet Empty { get; } = new([]);

    public MenuBehaviorEventHandlerSet WithHandlers(
        IEnumerable<MenuBehaviorEventHandlerEntry> handlers) =>
        new(handlers, HandlerTablePointer);
}

public abstract record MenuBehaviorEventHandler(
    MenuBehaviorRawEventHandlerShape Raw);

public sealed record MenuBehaviorScriptEventHandler(
    string? Script,
    MenuBehaviorRawEventHandlerShape Raw) : MenuBehaviorEventHandler(Raw)
{
    public static MenuBehaviorScriptEventHandler Create(string? script) =>
        new(script, MenuBehaviorRawEventHandlerShape.Empty);
}

public sealed record MenuBehaviorConditionalEventHandler(
    MenuBehaviorExpressionBinding Condition,
    MenuBehaviorEventHandlerSet? Then,
    XPointer<MenuEventHandlerSet> ThenPointer,
    MenuBehaviorRawEventHandlerShape Raw) : MenuBehaviorEventHandler(Raw)
{
    public static MenuBehaviorConditionalEventHandler Create(
        BehaviorExpression condition,
        MenuBehaviorEventHandlerSet then) =>
        new(
            new MenuBehaviorExpressionBinding(condition, default),
            then,
            default,
            MenuBehaviorRawEventHandlerShape.Empty);
}

public sealed record MenuBehaviorElseEventHandler(
    MenuBehaviorEventHandlerSet? Handlers,
    XPointer<MenuEventHandlerSet> HandlersPointer,
    MenuBehaviorRawEventHandlerShape Raw) : MenuBehaviorEventHandler(Raw)
{
    public static MenuBehaviorElseEventHandler Create(
        MenuBehaviorEventHandlerSet handlers) =>
        new(handlers, default, MenuBehaviorRawEventHandlerShape.Empty);
}

public sealed record MenuBehaviorSetLocalVariableEventHandler(
    MenuBehaviorLocalValueType ValueType,
    string? Name,
    MenuBehaviorExpressionBinding Expression,
    XPointer<SetLocalVarData> DataPointer,
    XPointer<string> NamePointer,
    MenuBehaviorRawEventHandlerShape Raw) : MenuBehaviorEventHandler(Raw)
{
    public static MenuBehaviorSetLocalVariableEventHandler Create(
        MenuBehaviorLocalValueType valueType,
        string name,
        BehaviorExpression expression) =>
        new(
            valueType,
            name,
            new MenuBehaviorExpressionBinding(expression, default),
            default,
            default,
            MenuBehaviorRawEventHandlerShape.Empty);
}

/// <summary>
/// An imported event discriminator/payload which has no modeled authored
/// variant. The codec retains it unchanged instead of discarding data.
/// </summary>
public sealed record MenuBehaviorOpaqueEventHandler(
    byte EventType,
    MenuBehaviorRawEventHandlerShape Raw) : MenuBehaviorEventHandler(Raw);

/// <summary>One ordered key-handler node. The linked-list representation is codec-only.</summary>
public sealed record MenuBehaviorKeyHandlerBinding(
    int Key,
    MenuBehaviorEventHandlerSet? Action,
    XPointer<MenuEventHandlerSet> ActionPointer,
    XPointer<ItemKeyHandler> NextPointer)
{
    public static MenuBehaviorKeyHandlerBinding Create(
        int key,
        MenuBehaviorEventHandlerSet action) =>
        new(key, action, default, default);
}

/// <summary>
/// Immutable list view of the native ItemKeyHandler chain. A non-null
/// terminating pointer records a malformed/cyclic imported tail without
/// converting the authored domain into a mutable graph.
/// </summary>
public sealed class MenuBehaviorKeyHandlerBindings
{
    public MenuBehaviorKeyHandlerBindings(
        IEnumerable<MenuBehaviorKeyHandlerBinding> handlers,
        XPointer<ItemKeyHandler> rootPointer = default,
        bool hasTruncatedImportedTail = false)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        Handlers = handlers.ToImmutableArray();
        RootPointer = rootPointer;
        HasTruncatedImportedTail = hasTruncatedImportedTail;
    }

    public ImmutableArray<MenuBehaviorKeyHandlerBinding> Handlers { get; }

    public XPointer<ItemKeyHandler> RootPointer { get; }

    public bool HasTruncatedImportedTail { get; }

    public static MenuBehaviorKeyHandlerBindings Empty { get; } = new([]);
}

/// <summary>One fixed event-set attachment field on ItemDef or ListBoxDef.</summary>
public sealed record MenuBehaviorEventBinding(
    MenuBehaviorEventHandlerSet? Handlers,
    XPointer<MenuEventHandlerSet> SourcePointer)
{
    public static MenuBehaviorEventBinding Empty { get; } = new(null, default);
}

/// <summary>One expression attachment and its original statement pointer.</summary>
public sealed record MenuBehaviorExpressionBinding(
    BehaviorExpression? Value,
    XPointer<Statement> SourcePointer)
{
    /// <summary>
    /// Support table which owns this expression. It may differ from the
    /// Menu-wide table for imported statements and is safe for Desktop use.
    /// </summary>
    public BehaviorExpressionSupport Support { get; init; } =
        BehaviorExpressionSupport.Empty;

    /// <summary>Non-fatal diagnostics captured while importing the statement.</summary>
    public ImmutableArray<BehaviorExpressionDiagnostic> ImportDiagnostics
        { get; init; } = [];

    /// <summary>
    /// The untouched native statement captured at import time. It remains an
    /// implementation detail so Desktop-facing behavior values never expose
    /// packed menu pointers.
    /// </summary>
    internal Statement? SourceStatement { get; init; }

    public static MenuBehaviorExpressionBinding Empty { get; } = new(null, default);
}

/// <summary>One present row of ItemFloatExpression[] in its authored order.</summary>
public sealed record MenuBehaviorFloatExpressionBinding(
    ItemFloatExpressionTarget Target,
    MenuBehaviorExpressionBinding Expression)
{
    /// <summary>
    /// Captures an imported unknown-target or duplicate-target group. The
    /// validator permits it only while every member remains in its original
    /// position with its original target and expression binding.
    /// </summary>
    internal MenuBehaviorImportedFloatExpressionShape? ImportedShape { get; init; }
}

/// <summary>
/// Identity-based provenance for one malformed float-target group. A duplicate
/// is a property of every row using that target, so changing any member makes
/// the entire imported exception ineligible for authored output.
/// </summary>
internal sealed class MenuBehaviorImportedFloatExpressionShape
{
    private readonly ImmutableArray<Member> _members;

    public MenuBehaviorImportedFloatExpressionShape(
        IEnumerable<(int Index, ItemFloatExpressionTarget Target,
            MenuBehaviorExpressionBinding Expression)> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _members = members
            .Select(member => new Member(
                member.Index,
                member.Target,
                member.Expression))
            .ToImmutableArray();
    }

    public bool Matches(ImmutableArray<MenuBehaviorFloatExpressionBinding> entries)
    {
        foreach (Member member in _members)
        {
            if (member.Index < 0 || member.Index >= entries.Length)
                return false;

            MenuBehaviorFloatExpressionBinding entry = entries[member.Index];
            if (entry.Target != member.Target ||
                !ReferenceEquals(entry.Expression, member.Expression))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record Member(
        int Index,
        ItemFloatExpressionTarget Target,
        MenuBehaviorExpressionBinding Expression);
}

/// <summary>
/// Immutable bindings for every native ItemFloatExpressionTarget. Unset
/// targets have no row; present rows retain their original serialized order.
/// </summary>
public sealed class MenuBehaviorFloatExpressionBindings
{
    private readonly ImmutableDictionary<ItemFloatExpressionTarget, MenuBehaviorExpressionBinding>
        _values;

    public MenuBehaviorFloatExpressionBindings(
        IEnumerable<MenuBehaviorFloatExpressionBinding> values,
        XPointer<ItemFloatExpression[]> sourcePointer = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        SourcePointer = sourcePointer;
        Entries = values.ToImmutableArray();
        var builder = ImmutableDictionary.CreateBuilder<
            ItemFloatExpressionTarget,
            MenuBehaviorExpressionBinding>();
        foreach (MenuBehaviorFloatExpressionBinding value in Entries)
        {
            ArgumentNullException.ThrowIfNull(value);
            // Keep the first row as the convenient fixed-target projection;
            // retain every row in Entries so imported duplicate/opaque shapes
            // survive a no-edit round trip and can be diagnosed centrally.
            builder.TryAdd(value.Target, value.Expression);
        }
        _values = builder.ToImmutable();
    }

    public XPointer<ItemFloatExpression[]> SourcePointer { get; }

    /// <summary>
    /// Ordered native rows, including imported duplicate or unknown targets.
    /// Authoring validation decides whether those shapes can be changed.
    /// </summary>
    public ImmutableArray<MenuBehaviorFloatExpressionBinding> Entries { get; }

    internal bool RetainsImportedInvalidShape(int index) =>
        index >= 0 && index < Entries.Length &&
        Entries[index].ImportedShape?.Matches(Entries) == true;

    public IReadOnlyDictionary<ItemFloatExpressionTarget, MenuBehaviorExpressionBinding> Values =>
        _values;

    public MenuBehaviorExpressionBinding? this[ItemFloatExpressionTarget target] =>
        _values.GetValueOrDefault(target);

    public MenuBehaviorExpressionBinding? RectX => this[ItemFloatExpressionTarget.RectX];
    public MenuBehaviorExpressionBinding? RectY => this[ItemFloatExpressionTarget.RectY];
    public MenuBehaviorExpressionBinding? RectW => this[ItemFloatExpressionTarget.RectW];
    public MenuBehaviorExpressionBinding? RectH => this[ItemFloatExpressionTarget.RectH];
    public MenuBehaviorExpressionBinding? ForeColorR => this[ItemFloatExpressionTarget.ForeColorR];
    public MenuBehaviorExpressionBinding? ForeColorG => this[ItemFloatExpressionTarget.ForeColorG];
    public MenuBehaviorExpressionBinding? ForeColorB => this[ItemFloatExpressionTarget.ForeColorB];
    public MenuBehaviorExpressionBinding? ForeColorRgb => this[ItemFloatExpressionTarget.ForeColorRgb];
    public MenuBehaviorExpressionBinding? ForeColorA => this[ItemFloatExpressionTarget.ForeColorA];
    public MenuBehaviorExpressionBinding? GlowColorR => this[ItemFloatExpressionTarget.GlowColorR];
    public MenuBehaviorExpressionBinding? GlowColorG => this[ItemFloatExpressionTarget.GlowColorG];
    public MenuBehaviorExpressionBinding? GlowColorB => this[ItemFloatExpressionTarget.GlowColorB];
    public MenuBehaviorExpressionBinding? GlowColorRgb => this[ItemFloatExpressionTarget.GlowColorRgb];
    public MenuBehaviorExpressionBinding? GlowColorA => this[ItemFloatExpressionTarget.GlowColorA];
    public MenuBehaviorExpressionBinding? BackColorR => this[ItemFloatExpressionTarget.BackColorR];
    public MenuBehaviorExpressionBinding? BackColorG => this[ItemFloatExpressionTarget.BackColorG];
    public MenuBehaviorExpressionBinding? BackColorB => this[ItemFloatExpressionTarget.BackColorB];
    public MenuBehaviorExpressionBinding? BackColorRgb => this[ItemFloatExpressionTarget.BackColorRgb];
    public MenuBehaviorExpressionBinding? BackColorA => this[ItemFloatExpressionTarget.BackColorA];

    public static ImmutableArray<ItemFloatExpressionTarget> AllTargets { get; } =
    [
        ItemFloatExpressionTarget.RectX,
        ItemFloatExpressionTarget.RectY,
        ItemFloatExpressionTarget.RectW,
        ItemFloatExpressionTarget.RectH,
        ItemFloatExpressionTarget.ForeColorR,
        ItemFloatExpressionTarget.ForeColorG,
        ItemFloatExpressionTarget.ForeColorB,
        ItemFloatExpressionTarget.ForeColorRgb,
        ItemFloatExpressionTarget.ForeColorA,
        ItemFloatExpressionTarget.GlowColorR,
        ItemFloatExpressionTarget.GlowColorG,
        ItemFloatExpressionTarget.GlowColorB,
        ItemFloatExpressionTarget.GlowColorRgb,
        ItemFloatExpressionTarget.GlowColorA,
        ItemFloatExpressionTarget.BackColorR,
        ItemFloatExpressionTarget.BackColorG,
        ItemFloatExpressionTarget.BackColorB,
        ItemFloatExpressionTarget.BackColorRgb,
        ItemFloatExpressionTarget.BackColorA
    ];

    public static MenuBehaviorFloatExpressionBindings Empty { get; } = new([]);
}

/// <summary>Fixed ItemDef statement bindings plus its complete float-target set.</summary>
public sealed record MenuItemBehaviorExpressionBindings(
    MenuBehaviorExpressionBinding Visible,
    MenuBehaviorExpressionBinding Disabled,
    MenuBehaviorExpressionBinding Text,
    MenuBehaviorExpressionBinding Material,
    MenuBehaviorFloatExpressionBindings FloatExpressions)
{
    public static MenuItemBehaviorExpressionBindings Empty { get; } = new(
        MenuBehaviorExpressionBinding.Empty,
        MenuBehaviorExpressionBinding.Empty,
        MenuBehaviorExpressionBinding.Empty,
        MenuBehaviorExpressionBinding.Empty,
        MenuBehaviorFloatExpressionBindings.Empty);
}

/// <summary>Immutable MenuDef event hooks and ordered key handlers.</summary>
public sealed record MenuDefinitionBehaviorBindings(
    MenuBehaviorEventBinding OnOpen,
    MenuBehaviorEventBinding OnCloseRequest,
    MenuBehaviorEventBinding OnClose,
    MenuBehaviorEventBinding OnEscape,
    MenuBehaviorKeyHandlerBindings KeyHandlers)
{
    /// <summary>
    /// Modal-local additions to the Menu-wide expression support graph. Native
    /// table construction belongs exclusively to document compilation.
    /// </summary>
    public MenuBehaviorExpressionSupportDelta ExpressionSupportDelta
        { get; init; } = MenuBehaviorExpressionSupportDelta.Empty;

    public static MenuDefinitionBehaviorBindings Empty { get; } = new(
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorKeyHandlerBindings.Empty);
}

/// <summary>
/// Immutable fixed ItemDef behavior surface. ListBoxDoubleClick is present for
/// every item but only meaningful when its payload is a ListBoxDef.
/// </summary>
public sealed record MenuItemBehaviorBindings(
    MenuBehaviorEventBinding MouseEnterText,
    MenuBehaviorEventBinding MouseExitText,
    MenuBehaviorEventBinding MouseEnter,
    MenuBehaviorEventBinding MouseExit,
    MenuBehaviorEventBinding Action,
    MenuBehaviorEventBinding Accept,
    MenuBehaviorEventBinding OnFocus,
    MenuBehaviorEventBinding LeaveFocus,
    MenuBehaviorEventBinding ListBoxDoubleClick,
    MenuBehaviorKeyHandlerBindings KeyHandlers,
    MenuItemBehaviorExpressionBindings Expressions)
{
    /// <summary>
    /// Modal-local additions to the Menu-wide expression support graph. Native
    /// table construction belongs exclusively to document compilation.
    /// </summary>
    public MenuBehaviorExpressionSupportDelta ExpressionSupportDelta
        { get; init; } = MenuBehaviorExpressionSupportDelta.Empty;

    public static MenuItemBehaviorBindings Empty { get; } = new(
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorEventBinding.Empty,
        MenuBehaviorKeyHandlerBindings.Empty,
        MenuItemBehaviorExpressionBindings.Empty);

    public MenuBehaviorEventBinding GetHook(MenuItemBehaviorHook hook) => hook switch
    {
        MenuItemBehaviorHook.MouseEnterText => MouseEnterText,
        MenuItemBehaviorHook.MouseExitText => MouseExitText,
        MenuItemBehaviorHook.MouseEnter => MouseEnter,
        MenuItemBehaviorHook.MouseExit => MouseExit,
        MenuItemBehaviorHook.Action => Action,
        MenuItemBehaviorHook.Accept => Accept,
        MenuItemBehaviorHook.OnFocus => OnFocus,
        MenuItemBehaviorHook.LeaveFocus => LeaveFocus,
        _ => throw new ArgumentOutOfRangeException(nameof(hook))
    };
}

/// <summary>
/// Raw native values produced by <see cref="MenuItemBehaviorCodec"/>. A later
/// compiler stage composes them into an ItemDef clone without giving this
/// behavior package ownership of document mutation.
/// </summary>
public sealed record MenuItemBehaviorAssetBindings(
    MenuBehaviorNativeEventBinding MouseEnterText,
    MenuBehaviorNativeEventBinding MouseExitText,
    MenuBehaviorNativeEventBinding MouseEnter,
    MenuBehaviorNativeEventBinding MouseExit,
    MenuBehaviorNativeEventBinding Action,
    MenuBehaviorNativeEventBinding Accept,
    MenuBehaviorNativeEventBinding OnFocus,
    MenuBehaviorNativeEventBinding LeaveFocus,
    MenuBehaviorNativeEventBinding ListBoxDoubleClick,
    XPointer<ItemKeyHandler> OnKeyPointer,
    ItemKeyHandler? OnKeyHandler,
    MenuBehaviorNativeExpressionBinding Visible,
    MenuBehaviorNativeExpressionBinding Disabled,
    MenuBehaviorNativeExpressionBinding Text,
    MenuBehaviorNativeExpressionBinding Material,
    XPointer<ItemFloatExpression[]> FloatExpressionsPointer,
    IReadOnlyList<ItemFloatExpression> FloatExpressions);

/// <summary>Native MenuDef behavior values produced at the document boundary.</summary>
public sealed record MenuDefinitionBehaviorAssetBindings(
    MenuBehaviorNativeEventBinding OnOpen,
    MenuBehaviorNativeEventBinding OnCloseRequest,
    MenuBehaviorNativeEventBinding OnClose,
    MenuBehaviorNativeEventBinding OnEscape,
    XPointer<ItemKeyHandler> ExecKeysPointer,
    ItemKeyHandler? ExecKeyHandler);

/// <summary>One native event-set result to be composed into an ItemDef clone.</summary>
public sealed record MenuBehaviorNativeEventBinding(
    MenuEventHandlerSet? Handlers,
    XPointer<MenuEventHandlerSet> Pointer);

/// <summary>One native statement result to be composed into an ItemDef clone.</summary>
public sealed record MenuBehaviorNativeExpressionBinding(
    Statement? Statement,
    XPointer<Statement> Pointer);
