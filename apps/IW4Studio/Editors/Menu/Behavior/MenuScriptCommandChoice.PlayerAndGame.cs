using System.Globalization;
using System.Text;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

internal sealed partial record MenuScriptCommandChoice
{
    private static IReadOnlyList<MenuScriptCommandChoice> PlayerAndGameCommands() =>
    [
        new("setPlayerData", "Set player data",
            [
                new("Data path", "Comma-separated fields and indices, such as round, awardCount",
                    MenuScriptArgumentKind.PlayerPath),
                new("Value", "A literal, local variable lookup, or keyboard prompt",
                    MenuScriptArgumentKind.PlayerValue)
            ],
            Layout: "( {0} , {1} )"),
        new("setPlayerDataSplitScreen", "Set split-screen player data",
            [
                new("Player index", "A zero-based index or localVarInt ( name )",
                    MenuScriptArgumentKind.PlayerIndex),
                new("Data path", "Comma-separated fields and indices, such as round, awardCount",
                    MenuScriptArgumentKind.PlayerPath),
                new("Value", "A literal, local variable lookup, or keyboard prompt",
                    MenuScriptArgumentKind.PlayerValue)
            ],
            Layout: "( {0} , {1} , {2} )"),
        new("updateMail", "Update mail", []),
        new("openMail", "Open mail", [], "No effect on PS3."),
        new("deleteMail", "Delete mail", [], "No effect on PS3."),
        new("doMailLottery", "Run the mail lottery", [], "No effect on PS3."),
        new("resetStatsConfirm", "Confirm a stats reset", []),
        new("resetStatsCancel", "Cancel a stats reset", []),
        new("setGameMode", "Set the game mode", [new("Game mode", "Enter a game mode name")]),
        new("setLocalVarBool", "Set a local boolean",
            [
                new("Local variable", "Enter the variable name"),
                new("Value", "Use 0 for false or 1 for true", MenuScriptArgumentKind.Choice,
                    Choices: ["0", "1"])
            ]),
        new("setLocalVarInt", "Set a local integer",
            [
                new("Local variable", "Enter the variable name"),
                new("Value", "Enter an integer literal", MenuScriptArgumentKind.Integer)
            ]),
        new("setLocalVarFloat", "Set a local float",
            [
                new("Local variable", "Enter the variable name"),
                new("Value", "Enter a decimal literal", MenuScriptArgumentKind.Number)
            ]),
        new("setLocalVarString", "Set a local string",
            [
                new("Local variable", "Enter the variable name"),
                new("Value", "Enter text or leave blank for an empty string", AllowEmpty: true)
            ]),
        new("feederTop", "Go to the top of a feeder", []),
        new("feederBottom", "Go to the bottom of a feeder", []),
        new("showGamerCard", "Show a gamer card", [], "No effect on PS3."),
        new("openforgametype", "Open a menu for a game type",
            [new("Menu pattern", "Use %s for the item's game variable, such as settings_%s",
                MenuScriptArgumentKind.MenuPattern)]),
        new("closeforgametype", "Close a menu for a game type",
            [new("Menu pattern", "Use %s for the item's game variable, such as settings_%s",
                MenuScriptArgumentKind.MenuPattern)]),
        new("statclearperknew", "Clear a perk’s new marker",
            [new("Perk name", "Enter a perk name from mp/statstable.csv")]),
        new("statsetusingtable", "Set a stat using a table",
            [
                new("Stat index", "Enter a create-a-class stat index", MenuScriptArgumentKind.Integer),
                new("String table", "Enter the CSV asset path"),
                new("Match column", "Enter the zero-based column to search", MenuScriptArgumentKind.Integer),
                new("Match value", "Enter the text to match", AllowEmpty: true),
                new("Result column", "Enter the zero-based column holding the integer result",
                    MenuScriptArgumentKind.Integer)
            ],
            Layout: "( {0} , tablelookup ( {1} , {2} , {3} , {4} ) )"),
        new("statclearbitmask", "Clear a stat bit mask",
            [
                new("Stat index variable", "Enter the dvar containing the stat index"),
                new("Mask variable", "Enter the dvar containing the bits to clear")
            ],
            Layout: "( {0} , {1} )"),
        new("kickPlayer", "Kick the selected player", []),
        new("getKickPlayerQuestion", "Show the kick confirmation for the selected player", []),
        new("partyUpdateMissingMapPackDvar", "Update the missing map pack variable", []),
        new("getHostMigrateQuestion", "Show the host migration confirmation", []),
        new("makehost", "Make the selected player the host", []),
        new("togglePlayerMute", "Toggle mute for the selected player", []),
        new("addFriendFromId", "Add the selected player as a friend", []),
        new("resolveError", "Resolve an error", []),
        new("lerp", "Interpolate a menu property",
            [
                new("Property", "Choose the property to animate", MenuScriptArgumentKind.Choice,
                    Choices: ["scale", "alpha", "x", "y"]),
                new("Start value", "Enter the starting value", MenuScriptArgumentKind.Number),
                new("End value", "Enter the ending value", MenuScriptArgumentKind.Number),
                new("Duration", "Enter the duration in seconds", MenuScriptArgumentKind.Number)
            ],
            Layout: "{0} from {1} to {2} over {3}")
    ];

    private static bool TryEncodePlayerArgument(
        MenuScriptArgumentKind kind,
        string value,
        out string token,
        out string error)
    {
        int position = 0;
        var components = new List<string>();
        do
        {
            if (!TryReadPlayerComponent(kind, value, ref position, out string component))
                break;

            components.Add(component);
            SkipPlayerArgumentWhitespace(value, ref position);
            if (position == value.Length)
            {
                token = string.Join(" , ", components);
                error = string.Empty;
                return true;
            }

            if (kind != MenuScriptArgumentKind.PlayerPath || value[position] != ',')
                break;
            position++;
        } while (true);

        token = string.Empty;
        error = kind switch
        {
            MenuScriptArgumentKind.PlayerPath =>
                "Enter a comma-separated path of field names, indices, or local variable lookups.",
            MenuScriptArgumentKind.PlayerIndex =>
                "Enter a nonnegative integer or localVarInt ( name ).",
            _ => "Enter one value, such as a number, quoted text, local variable lookup, or keyboard prompt."
        };
        return false;
    }

    private static bool TryReadPlayerComponent(
        MenuScriptArgumentKind kind,
        string value,
        ref int position,
        out string component)
    {
        component = string.Empty;
        if (!TryReadPlayerAtom(value, ref position, out string atom))
            return false;

        SkipPlayerArgumentWhitespace(value, ref position);
        bool isLocalInt = atom.Equals("localvarint", StringComparison.OrdinalIgnoreCase);
        bool isLocalString = atom.Equals("localvarstring", StringComparison.OrdinalIgnoreCase);
        bool isValueOnlyFunction = atom.Equals("localvarbool", StringComparison.OrdinalIgnoreCase)
            || atom.Equals("keyboard", StringComparison.OrdinalIgnoreCase);
        if (isLocalInt || isLocalString || isValueOnlyFunction)
        {
            if ((isLocalString && kind == MenuScriptArgumentKind.PlayerIndex)
                || (isValueOnlyFunction && kind != MenuScriptArgumentKind.PlayerValue)
                || position == value.Length || value[position++] != '('
                || !TryReadPlayerAtom(value, ref position, out string name)
                || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            SkipPlayerArgumentWhitespace(value, ref position);
            if (position == value.Length || value[position++] != ')')
                return false;

            component = $"{atom} ( {QuoteToken(name)} )";
            return true;
        }

        if (kind == MenuScriptArgumentKind.PlayerPath && string.IsNullOrWhiteSpace(atom))
            return false;
        if (kind == MenuScriptArgumentKind.PlayerIndex
            && (!int.TryParse(atom, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index)
                || index < 0))
        {
            return false;
        }

        component = QuoteToken(atom);
        return true;
    }

    private static bool TryReadPlayerAtom(string value, ref int position, out string atom)
    {
        atom = string.Empty;
        SkipPlayerArgumentWhitespace(value, ref position);
        if (position == value.Length)
            return false;

        if (value[position] != '"')
        {
            int start = position;
            while (position < value.Length
                && !char.IsWhiteSpace(value[position])
                && value[position] is not ('"' or ',' or '(' or ')' or ';' or '{' or '}'))
            {
                position++;
            }

            atom = value[start..position];
            return position > start;
        }

        position++;
        var decoded = new StringBuilder();
        while (position < value.Length)
        {
            char character = value[position++];
            if (character == '"')
            {
                atom = decoded.ToString();
                // The game compares the decoded token to delimiters, even when it was quoted.
                return atom is not ("," or "(" or ")" or ";" or "{" or "}");
            }

            if (character == '\\' && position < value.Length && value[position] is '\\' or '"')
                character = value[position++];
            decoded.Append(character);
        }

        return false;
    }

    private static void SkipPlayerArgumentWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
            position++;
    }
}
