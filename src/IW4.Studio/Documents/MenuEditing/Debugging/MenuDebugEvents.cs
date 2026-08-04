using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public sealed class MenuDebugExpression
{
    private readonly IReadOnlyList<MenuDebugDependency> _dependencies;

    internal MenuDebugExpression(
        DebugExpressionNode root,
        IEnumerable<MenuDebugDependency> dependencies)
    {
        Root = root;
        _dependencies = Array.AsReadOnly(dependencies.Distinct().ToArray());
    }

    internal DebugExpressionNode Root { get; }
    public IReadOnlyList<MenuDebugDependency> Dependencies => _dependencies;
}

public abstract record MenuDebugEventHandler(MenuEventHandlerType Type);

public sealed record MenuDebugScriptEventHandler(string Script)
    : MenuDebugEventHandler(MenuEventHandlerType.UnconditionalScript);

public sealed record MenuDebugConditionalEventHandler(
    MenuDebugExpression? Condition,
    MenuDebugEventSet Handlers)
    : MenuDebugEventHandler(MenuEventHandlerType.ConditionalScript);

public sealed record MenuDebugElseEventHandler(MenuDebugEventSet Handlers)
    : MenuDebugEventHandler(MenuEventHandlerType.ElseScript);

public sealed record MenuDebugSetLocalVariableEventHandler(
    MenuEventHandlerType ValueType,
    string? Name,
    MenuDebugExpression? ValueExpression)
    : MenuDebugEventHandler(ValueType);

public sealed class MenuDebugEventSet
{
    private readonly IReadOnlyList<MenuDebugEventHandler> _handlers;

    internal MenuDebugEventSet(IEnumerable<MenuDebugEventHandler> handlers) =>
        _handlers = Array.AsReadOnly(handlers.ToArray());

    public static MenuDebugEventSet Empty { get; } = new([]);
    public IReadOnlyList<MenuDebugEventHandler> Handlers => _handlers;
}

public sealed record MenuDebugKeyHandler(int Key, MenuDebugEventSet Actions);

public sealed record MenuDebugMenuHooks(
    MenuDebugEventSet OnOpen,
    MenuDebugEventSet OnCloseRequest,
    MenuDebugEventSet OnClose,
    MenuDebugEventSet OnEscape,
    IReadOnlyList<MenuDebugKeyHandler> KeyHandlers);

public sealed record MenuDebugItemHooks(
    MenuDebugEventSet MouseEnterText,
    MenuDebugEventSet MouseExitText,
    MenuDebugEventSet MouseEnter,
    MenuDebugEventSet MouseExit,
    MenuDebugEventSet Action,
    MenuDebugEventSet Accept,
    MenuDebugEventSet OnFocus,
    MenuDebugEventSet LeaveFocus,
    MenuDebugEventSet DoubleClick,
    IReadOnlyList<MenuDebugKeyHandler> KeyHandlers);
