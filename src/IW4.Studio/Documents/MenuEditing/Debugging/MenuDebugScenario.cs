using System.Collections.ObjectModel;
using System.Collections.Frozen;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public readonly struct MenuDebugEnvironmentKey : IEquatable<MenuDebugEnvironmentKey>
{
    public MenuDebugEnvironmentKey(OperationEnum operation, string? qualifier = null)
    {
        Operation = operation;
        Qualifier = string.IsNullOrWhiteSpace(qualifier) ? null : qualifier;
    }

    public OperationEnum Operation { get; }
    public string? Qualifier { get; }

    public bool Equals(MenuDebugEnvironmentKey other) =>
        Operation == other.Operation &&
        StringComparer.OrdinalIgnoreCase.Equals(Qualifier, other.Qualifier);

    public override bool Equals(object? obj) =>
        obj is MenuDebugEnvironmentKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Operation,
        Qualifier is null
            ? 0
            : StringComparer.OrdinalIgnoreCase.GetHashCode(Qualifier));

    public override string ToString() => Qualifier is null
        ? Operation.ToString()
        : $"{Operation}({Qualifier})";
}

public delegate string? MenuDebugLocalizationResolver(string reference);

/// <summary>
/// Runtime-only Window color overrides produced by debugger-safe Menu script
/// commands. Null channels continue to use the authored value.
/// </summary>
public sealed record MenuDebugItemRuntimeState(
    MenuColorValue? ForeColor = null,
    MenuColorValue? BackColor = null,
    MenuColorValue? BorderColor = null);

/// <summary>
/// Immutable input snapshot for one deterministic menu evaluation. Scenario
/// state is editor-only and never mutates the authored Menu graph.
/// </summary>
public sealed class MenuDebugScenario
{
    private readonly IReadOnlyDictionary<string, MenuDebugValue> _dvars;
    private readonly IReadOnlyDictionary<string, MenuDebugValue> _localVariables;
    private readonly IReadOnlyDictionary<MenuDebugEnvironmentKey, MenuDebugValue> _environment;
    private readonly IReadOnlySet<string> _openMenus;
    private readonly IReadOnlyDictionary<MenuNodeId, MenuDebugItemRuntimeState>
        _itemRuntimeStates;

    public MenuDebugScenario(
        int milliseconds = 0,
        IReadOnlyDictionary<string, MenuDebugValue>? dvars = null,
        IReadOnlyDictionary<string, MenuDebugValue>? localVariables = null,
        IReadOnlyDictionary<MenuDebugEnvironmentKey, MenuDebugValue>? environment = null,
        IEnumerable<string>? openMenus = null,
        MenuNodeId? focusedItemId = null,
        MenuDebugLocalizationResolver? localizationResolver = null,
        IReadOnlyDictionary<MenuNodeId, MenuDebugItemRuntimeState>?
            itemRuntimeStates = null)
    {
        Milliseconds = milliseconds;
        _dvars = CopyNamedValues(dvars);
        _localVariables = CopyNamedValues(localVariables);
        _environment = new ReadOnlyDictionary<MenuDebugEnvironmentKey, MenuDebugValue>(
            environment is null
                ? new Dictionary<MenuDebugEnvironmentKey, MenuDebugValue>()
                : new Dictionary<MenuDebugEnvironmentKey, MenuDebugValue>(environment));
        _openMenus = (openMenus ?? []).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _itemRuntimeStates = new ReadOnlyDictionary<
            MenuNodeId,
            MenuDebugItemRuntimeState>(
                itemRuntimeStates is null
                    ? new Dictionary<MenuNodeId, MenuDebugItemRuntimeState>()
                    : new Dictionary<MenuNodeId, MenuDebugItemRuntimeState>(
                        itemRuntimeStates));
        FocusedItemId = focusedItemId;
        LocalizationResolver = localizationResolver;
    }

    public static MenuDebugScenario Empty { get; } = new();

    public int Milliseconds { get; }
    public IReadOnlyDictionary<string, MenuDebugValue> Dvars => _dvars;
    public IReadOnlyDictionary<string, MenuDebugValue> LocalVariables => _localVariables;
    public IReadOnlyDictionary<MenuDebugEnvironmentKey, MenuDebugValue> Environment => _environment;
    public IReadOnlySet<string> OpenMenus => _openMenus;
    public IReadOnlyDictionary<MenuNodeId, MenuDebugItemRuntimeState>
        ItemRuntimeStates => _itemRuntimeStates;
    public MenuNodeId? FocusedItemId { get; }
    public MenuDebugLocalizationResolver? LocalizationResolver { get; }

    private static IReadOnlyDictionary<string, MenuDebugValue> CopyNamedValues(
        IReadOnlyDictionary<string, MenuDebugValue>? values) =>
        new ReadOnlyDictionary<string, MenuDebugValue>(
            values is null
                ? new Dictionary<string, MenuDebugValue>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, MenuDebugValue>(values, StringComparer.OrdinalIgnoreCase));
}
