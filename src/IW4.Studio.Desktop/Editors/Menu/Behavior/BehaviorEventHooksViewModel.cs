using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>All ItemDef event roots, hydrated from one immutable behavior value.</summary>
public sealed class BehaviorEventHooksViewModel : ObservableObject
{
    private readonly Action _changed;

    internal BehaviorEventHooksViewModel(
        MenuItemBehaviorBindings bindings,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        bool supportsListBoxDoubleClick,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Slots = Array.AsReadOnly(
        [
            CreateSlot(MenuItemBehaviorHook.MouseEnterText, "Mouse enter text", "Runs when the pointer enters this item's text.", bindings.GetHook(MenuItemBehaviorHook.MouseEnterText), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.MouseExitText, "Mouse exit text", "Runs when the pointer leaves this item's text.", bindings.GetHook(MenuItemBehaviorHook.MouseExitText), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.MouseEnter, "Mouse enter", "Runs when the pointer enters this item.", bindings.GetHook(MenuItemBehaviorHook.MouseEnter), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.MouseExit, "Mouse exit", "Runs when the pointer leaves this item.", bindings.GetHook(MenuItemBehaviorHook.MouseExit), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.Action, "Action", "Runs when the item action is invoked.", bindings.GetHook(MenuItemBehaviorHook.Action), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.Accept, "Accept", "Runs when the item accepts its current value.", bindings.GetHook(MenuItemBehaviorHook.Accept), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.OnFocus, "On focus", "Runs when this item receives focus.", bindings.GetHook(MenuItemBehaviorHook.OnFocus), expressionSupport),
            CreateSlot(MenuItemBehaviorHook.LeaveFocus, "Leave focus", "Runs when this item loses focus.", bindings.GetHook(MenuItemBehaviorHook.LeaveFocus), expressionSupport)
        ]);
        ListBoxDoubleClick = CreateSlot(
            hook: null,
            title: "List box double click",
            description: "Runs when a list-box item is double-clicked.",
            binding: bindings.ListBoxDoubleClick,
            expressionSupport);
        SupportsListBoxDoubleClick = supportsListBoxDoubleClick;
    }

    public IReadOnlyList<BehaviorEventSlotViewModel> Slots { get; }

    public BehaviorEventSlotViewModel ListBoxDoubleClick { get; }

    public bool SupportsListBoxDoubleClick { get; }

    public string Summary
    {
        get
        {
            IEnumerable<BehaviorEventSlotViewModel> slots = SupportsListBoxDoubleClick
                ? Slots.Append(ListBoxDoubleClick)
                : Slots;
            int activeSlots = slots.Count(slot => slot.HasHandlers);
            int handlerCount = slots.Sum(slot => slot.HandlerCount);
            return activeSlots == 0
                ? "No event hooks"
                : $"{activeSlots} hook{Plural(activeSlots)} · {handlerCount} handler{Plural(handlerCount)}";
        }
    }

    internal MenuItemBehaviorBindings ApplyTo(MenuItemBehaviorBindings source) =>
        source with
        {
            MouseEnterText = Slots[0].ToBinding(),
            MouseExitText = Slots[1].ToBinding(),
            MouseEnter = Slots[2].ToBinding(),
            MouseExit = Slots[3].ToBinding(),
            Action = Slots[4].ToBinding(),
            Accept = Slots[5].ToBinding(),
            OnFocus = Slots[6].ToBinding(),
            LeaveFocus = Slots[7].ToBinding(),
            ListBoxDoubleClick = SupportsListBoxDoubleClick
                ? ListBoxDoubleClick.ToBinding()
                : source.ListBoxDoubleClick
        };

    internal IReadOnlyList<string> Validate()
    {
        var messages = new List<string>();
        foreach (BehaviorEventSlotViewModel slot in Slots)
            messages.AddRange(slot.Validate());
        if (SupportsListBoxDoubleClick)
            messages.AddRange(ListBoxDoubleClick.Validate());
        return messages;
    }

    private BehaviorEventSlotViewModel CreateSlot(
        MenuItemBehaviorHook? hook,
        string title,
        string description,
        MenuBehaviorEventBinding binding,
        BehaviorExpressionSupportDraftViewModel expressionSupport) =>
        new(hook, title, description, binding, expressionSupport, NotifyChanged);

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public sealed class BehaviorEventSlotViewModel : ObservableObject
{
    private readonly MenuBehaviorEventBinding _source;
    private readonly Action _changed;

    internal BehaviorEventSlotViewModel(
        MenuItemBehaviorHook? hook,
        string title,
        string description,
        MenuBehaviorEventBinding source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        Hook = hook;
        Title = title;
        Description = description;
        _source = source;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Handlers = new BehaviorEventHandlerSetDraftViewModel(
            source.Handlers, expressionSupport, NotifyChanged);
    }

    public MenuItemBehaviorHook? Hook { get; }

    public string Title { get; }

    public string Description { get; }

    public BehaviorEventHandlerSetDraftViewModel Handlers { get; }

    public int HandlerCount => Handlers.Handlers.Count;

    public bool HasHandlers => Handlers.HasHandlers;

    public string HandlerSummary => Handlers.Summary;

    internal MenuBehaviorEventBinding ToBinding() => _source with
    {
        Handlers = Handlers.ToDomain()
    };

    internal IReadOnlyList<string> Validate() => Handlers.Validate(Title);

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HandlerCount));
        OnPropertyChanged(nameof(HasHandlers));
        OnPropertyChanged(nameof(HandlerSummary));
        _changed();
    }
}
