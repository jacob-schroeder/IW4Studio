using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Immutable handoff from the Menu designer to its view-owned modal host.
/// The callback preserves the designer/document authority boundary: the
/// modal edits a detached value and commits exactly one typed Menu edit.
/// </summary>
public sealed class MenuItemBehaviorEditRequestedEventArgs : EventArgs
{
    private readonly Action<MenuItemBehaviorBindings> _apply;

    internal MenuItemBehaviorEditRequestedEventArgs(
        MenuNodeId itemId,
        string itemTitle,
        MenuItemBehaviorBindings value,
        BehaviorExpressionSupport expressionSupport,
        bool supportsListBoxDoubleClick,
        Action<MenuItemBehaviorBindings> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemTitle);
        ItemId = itemId;
        ItemTitle = itemTitle;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ExpressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        SupportsListBoxDoubleClick = supportsListBoxDoubleClick;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public MenuNodeId ItemId { get; }

    public string ItemTitle { get; }

    public MenuItemBehaviorBindings Value { get; }

    public BehaviorExpressionSupport ExpressionSupport { get; }

    public bool SupportsListBoxDoubleClick { get; }

    public void Apply(MenuItemBehaviorBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _apply(value);
    }
}
