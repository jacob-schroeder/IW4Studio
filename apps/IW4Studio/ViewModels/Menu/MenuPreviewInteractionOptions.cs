using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public sealed record MenuPreviewInteractionOption(
    string Label,
    MenuDebugInput Input);

internal sealed record MenuPreviewInteractionSelection(
    string Summary,
    MenuDebugInput? FocusInput,
    IReadOnlyList<MenuPreviewInteractionOption> Events);

/// <summary>
/// Pure presentation projection from one authored selection to the focus and
/// event actions that the simulation can dispatch.
/// </summary>
internal static class MenuPreviewInteractionOptions
{
    public static MenuPreviewInteractionSelection Build(
        MenuDebugProgram? program,
        MenuOutlineNodeKind? selectedKind,
        MenuNodeId? selectedNodeId)
    {
        MenuDebugItemProgram? item = selectedKind ==
                MenuOutlineNodeKind.Item && selectedNodeId is { } itemId
            ? program?.Items.FirstOrDefault(value => value.Id == itemId)
            : null;
        string summary = selectedKind switch
        {
            MenuOutlineNodeKind.Item when item is not null =>
                "Selected: " + (string.IsNullOrWhiteSpace(item.Name)
                    ? item.Id.ToString()
                    : item.Name),
            MenuOutlineNodeKind.Menu or MenuOutlineNodeKind.Window =>
                "Selected: menu",
            _ => "Selected: none"
        };
        MenuDebugInput? focusInput = item is null
            ? null
            : new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Focus);
        return new MenuPreviewInteractionSelection(
            summary,
            focusInput,
            Array.AsReadOnly(Events(program, selectedKind, item).ToArray()));
    }

    private static IEnumerable<MenuPreviewInteractionOption> Events(
        MenuDebugProgram? program,
        MenuOutlineNodeKind? selectedKind,
        MenuDebugItemProgram? item)
    {
        if (program is null)
            yield break;
        if (selectedKind is MenuOutlineNodeKind.Menu or
            MenuOutlineNodeKind.Window)
        {
            foreach (MenuPreviewInteractionOption option in MenuEvents(program))
                yield return option;
            yield break;
        }
        if (item is null)
            yield break;
        foreach (MenuPreviewInteractionOption option in ItemEvents(item))
            yield return option;
    }

    private static IEnumerable<MenuPreviewInteractionOption> MenuEvents(
        MenuDebugProgram program)
    {
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On open",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Open),
            program.Hooks.OnOpen))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On close request",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.CloseRequest),
            program.Hooks.OnCloseRequest))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On close",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Close),
            program.Hooks.OnClose))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On escape",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Escape),
            program.Hooks.OnEscape))
        {
            yield return option;
        }
        for (int index = 0; index < program.Hooks.KeyHandlers.Count; index++)
        {
            MenuDebugKeyHandler key = program.Hooks.KeyHandlers[index];
            yield return KeyOption(
                key,
                index,
                new MenuDebugMenuKeyInput(
                    new MenuDebugKeySelection(key.Key, index)));
        }
    }

    private static IEnumerable<MenuPreviewInteractionOption> ItemEvents(
        MenuDebugItemProgram item)
    {
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Action",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Action),
            item.Hooks.Action))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Accept",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Accept),
            item.Hooks.Accept))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Pointer enter",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.PointerEnter),
            item.Hooks.MouseEnter))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Pointer exit",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.PointerExit),
            item.Hooks.MouseExit))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Text pointer enter",
            new MenuDebugItemHookInput(
                item.Id,
                MenuDebugItemHook.TextPointerEnter),
            item.Hooks.MouseEnterText))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Text pointer exit",
            new MenuDebugItemHookInput(
                item.Id,
                MenuDebugItemHook.TextPointerExit),
            item.Hooks.MouseExitText))
        {
            yield return option;
        }
        yield return new MenuPreviewInteractionOption(
            FocusLabel("Leave focus", item.Hooks.LeaveFocus),
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.LeaveFocus));
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Double click",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.DoubleClick),
            item.Hooks.DoubleClick))
        {
            yield return option;
        }
        for (int index = 0; index < item.Hooks.KeyHandlers.Count; index++)
        {
            MenuDebugKeyHandler key = item.Hooks.KeyHandlers[index];
            yield return KeyOption(
                key,
                index,
                new MenuDebugItemKeyInput(
                    item.Id,
                    new MenuDebugKeySelection(key.Key, index)));
        }
    }

    private static IEnumerable<MenuPreviewInteractionOption> NonEmpty(
        string label,
        MenuDebugInput input,
        MenuDebugEventSet eventSet)
    {
        if (eventSet.Handlers.Count > 0)
        {
            yield return new MenuPreviewInteractionOption(
                $"{label} ({eventSet.Handlers.Count:N0} handler(s))",
                input);
        }
    }

    private static MenuPreviewInteractionOption KeyOption(
        MenuDebugKeyHandler key,
        int index,
        MenuDebugInput input) => new(
        $"Key {key.Key} · authored #{index} " +
        $"({key.Actions.Handlers.Count:N0} handler(s))",
        input);

    private static string FocusLabel(
        string label,
        MenuDebugEventSet eventSet) =>
        eventSet.Handlers.Count == 0
            ? $"{label} (transition only)"
            : $"{label} + {eventSet.Handlers.Count:N0} handler(s)";
}
