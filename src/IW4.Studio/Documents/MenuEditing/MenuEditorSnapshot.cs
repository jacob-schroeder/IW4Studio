using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Documents.MenuEditing;

public sealed record MenuWindowSnapshot(MenuNodeId Id, MenuWindowValue Value);

public sealed record MenuItemSnapshot(
    MenuNodeId Id,
    MenuNodeId WindowId,
    bool IsResolved,
    MenuItemValue Value,
    MenuItemBehaviorBindings Behavior);

/// <summary>Immutable editor view of one detached Menu definition.</summary>
public sealed class MenuEditorSnapshot
{
    private readonly IReadOnlyList<MenuItemSnapshot> _items;

    public MenuEditorSnapshot(
        MenuNodeId id,
        MenuSettingsValue settings,
        MenuWindowSnapshot window,
        IEnumerable<MenuItemSnapshot> items,
        MenuDefinitionBehaviorBindings definitionBehavior,
        MenuBehaviorSummary behavior,
        BehaviorExpressionSupport expressionSupport,
        MenuDebugProgram debugProgram,
        bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(definitionBehavior);
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(expressionSupport);
        ArgumentNullException.ThrowIfNull(debugProgram);
        Id = id;
        Settings = settings;
        Window = window;
        _items = Array.AsReadOnly(items.ToArray());
        DefinitionBehavior = definitionBehavior;
        Behavior = behavior;
        ExpressionSupport = expressionSupport;
        DebugProgram = debugProgram;
        IsComplete = isComplete;
    }

    public MenuNodeId Id { get; }
    public string? Name => Window.Value.Name;
    public bool IsComplete { get; }
    public MenuSettingsValue Settings { get; }
    public MenuWindowSnapshot Window { get; }
    public IReadOnlyList<MenuItemSnapshot> Items => _items;
    public MenuDefinitionBehaviorBindings DefinitionBehavior { get; }
    public MenuBehaviorSummary Behavior { get; }
    public BehaviorExpressionSupport ExpressionSupport { get; }
    public MenuDebugProgram DebugProgram { get; }
}

public sealed record MenuFileRegistrationSnapshot(
    MenuRegistrationId Id,
    int Index,
    bool IsEditableDefinition,
    string? Name,
    MenuEditorSnapshot? Menu);

public sealed class MenuFileEditorSnapshot
{
    private readonly IReadOnlyList<MenuFileRegistrationSnapshot> _registrations;

    public MenuFileEditorSnapshot(
        string? name,
        IEnumerable<MenuFileRegistrationSnapshot> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        Name = name;
        _registrations = Array.AsReadOnly(registrations.ToArray());
    }

    public string? Name { get; }
    public IReadOnlyList<MenuFileRegistrationSnapshot> Registrations => _registrations;
}

internal sealed record MenuItemIdentity(MenuNodeId Id, MenuNodeId WindowId);

internal sealed class MenuDocumentIdentity
{
    public MenuDocumentIdentity(
        MenuNodeId id,
        MenuNodeId windowId,
        IEnumerable<MenuItemIdentity> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Id = id;
        WindowId = windowId;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public MenuNodeId Id { get; }
    public MenuNodeId WindowId { get; }
    public IReadOnlyList<MenuItemIdentity> Items { get; }

    public static MenuDocumentIdentity Create(
        IW4.Assets.Assets.Menu.MenuDefAsset asset) =>
        new(
            MenuNodeId.New(),
            MenuNodeId.New(),
            asset.Items.Select(_ => new MenuItemIdentity(
                MenuNodeId.New(),
                MenuNodeId.New())));

    public MenuDocumentIdentity Clone() => new(Id, WindowId, Items);

    public MenuDocumentIdentity WithItems(
        IEnumerable<MenuItemIdentity> items) => new(Id, WindowId, items);
}
