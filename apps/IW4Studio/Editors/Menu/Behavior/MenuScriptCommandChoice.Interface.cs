namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

internal sealed partial record MenuScriptCommandChoice
{
    private static IReadOnlyList<MenuScriptCommandChoice> InterfaceCommands()
    {
        var menu = new MenuScriptCommandArgument("Menu", "Choose or enter a menu name", MenuScriptArgumentKind.Menu);
        var selector = new MenuScriptCommandArgument(
            "Item or group", "Enter a name or group, or use prefix* to match a prefix", MenuScriptArgumentKind.Item);
        var colorTarget = new MenuScriptCommandArgument(
            "Item or group", "Enter a name, group, prefix*, or self for the event item", MenuScriptArgumentKind.Item);
        var dvar = new MenuScriptCommandArgument("Game variable", "Enter a dvar name");
        var stringValue = new MenuScriptCommandArgument(
            "Expected text", "Matches regardless of case. Leave blank for an empty string", AllowEmpty: true);
        var intValue = new MenuScriptCommandArgument(
            "Expected integer", "Enter a base-10 integer", MenuScriptArgumentKind.Integer);
        var floatValue = new MenuScriptCommandArgument(
            "Expected number", "Matches when the difference is less than 0.00001", MenuScriptArgumentKind.Number);
        var consoleCommand = new MenuScriptCommandArgument(
            "Console command", "Enter the full console command, including its arguments");
        var response = new MenuScriptCommandArgument("Response", "For example, class0");
        MenuScriptCommandArgument[] color =
        [
            new("Color channel", "Choose backcolor, forecolor, or bordercolor", MenuScriptArgumentKind.ColorChannel),
            new("Red", "Enter a finite number", MenuScriptArgumentKind.Number),
            new("Green", "Enter a finite number", MenuScriptArgumentKind.Number),
            new("Blue", "Enter a finite number", MenuScriptArgumentKind.Number),
            new("Alpha", "Use 0 for transparent or 1 for opaque", MenuScriptArgumentKind.Number)
        ];

        return
        [
            new("open", "Open a menu", [menu], "Play can open menus available in the workspace."),
            new("close", "Close a menu",
                [new("Menu", "Use lowercase self to close the current menu", MenuScriptArgumentKind.MenuOrSelf)],
                "Play can close the active menu by name or with self."),
            new("setfocus", "Focus an item",
                [new("Item", "Choose or enter an exact item name", MenuScriptArgumentKind.Item)],
                "Play can move focus to an eligible item."),
            new("focusfirst", "Focus the first item", [], "Play can focus the first eligible item."),
            new("setfocusbydvar", "Focus an item by game variable",
                [new("Game variable", "Enter the dvar named in the item’s dvarTest field")],
                "Play tries to focus the first eligible item whose dvar condition passes."),
            new("setdvar", "Set a game variable",
                [dvar, new("Value", "Leave blank for an empty string", AllowEmpty: true)],
                "Play updates the game variable in the current session."),
            new("play", "Play a sound", [new("Sound alias", "For example, mouse_click")]),
            new("scriptmenuresponse", "Send a menu response", [response]),
            new("setitemcolor", "Set an item color", [colorTarget, ..color],
                "Play changes the selected color channel on matching items."),
            new("setcolor", "Set the event item’s color", color),
            new("exec", "Queue a console command", [consoleCommand],
                "Play applies stock button and stick layout commands to the simulated controller."),
            new("execnow", "Run a console command", [consoleCommand]),
            new("fadein", "Fade in items", [selector]),
            new("fadeout", "Fade out items", [selector]),
            new("show", "Show items", [selector], "Play shows matching items when their display conditions pass."),
            new("hide", "Hide items", [selector], "Play hides matching items."),
            new("showMenu", "Show a loaded menu", [menu]),
            new("hideMenu", "Hide a loaded menu", [menu]),
            new("closeForAllPlayers", "Close a menu for all local players", [menu]),
            new("ingameopen", "Open a menu during gameplay", [menu]),
            new("ingameclose", "Close a menu during gameplay", [menu]),
            new("setbackground", "Set the event item’s background", [new("Material", "Enter a material name")]),
            new("execOnDvarStringValue", "Queue a command when a game variable matches text",
                [dvar, stringValue, consoleCommand]),
            new("execOnDvarIntValue", "Queue a command when a game variable matches an integer",
                [dvar, intValue, consoleCommand]),
            new("execOnDvarFloatValue", "Queue a command when a game variable matches a number",
                [dvar, floatValue, consoleCommand]),
            new("execNowOnDvarStringValue", "Run a command when a game variable matches text",
                [dvar, stringValue, consoleCommand]),
            new("execNowOnDvarIntValue", "Run a command when a game variable matches an integer",
                [dvar, intValue, consoleCommand]),
            new("execNowOnDvarFloatValue", "Run a command when a game variable matches a number",
                [dvar, floatValue, consoleCommand]),
            new("scriptMenuRespondOnDvarStringValue", "Send a response when a game variable matches text",
                [dvar, stringValue, response]),
            new("scriptMenuRespondOnDvarIntValue", "Send a response when a game variable matches an integer",
                [dvar, intValue, response]),
            new("scriptMenuRespondOnDvarFloatValue", "Send a response when a game variable matches a number",
                [dvar, floatValue, response])
        ];
    }
}
