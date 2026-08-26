using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Immutable handoff from the Menu designer to its view-owned MenuDef
/// behavior modal. Applying the detached value commits one typed Menu edit.
/// </summary>
public sealed class MenuDefinitionBehaviorEditRequestedEventArgs : EventArgs
{
    private readonly Action<MenuDefinitionBehaviorBindings> _apply;

    internal MenuDefinitionBehaviorEditRequestedEventArgs(
        string menuTitle,
        MenuDefinitionBehaviorBindings value,
        BehaviorExpressionSupport expressionSupport,
        Action<MenuDefinitionBehaviorBindings> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuTitle);
        MenuTitle = menuTitle;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ExpressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public string MenuTitle { get; }

    public MenuDefinitionBehaviorBindings Value { get; }

    public BehaviorExpressionSupport ExpressionSupport { get; }

    public void Apply(MenuDefinitionBehaviorBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _apply(value);
    }
}
