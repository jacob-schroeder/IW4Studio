using System.Globalization;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

internal enum MenuScriptArgumentKind
{
    Text, Menu, MenuOrSelf, Item, Number, Integer, Choice, ColorChannel,
    PlayerPath, PlayerValue, PlayerIndex, MenuPattern, NativeArguments
}

internal sealed record MenuScriptCommandArgument(
    string Label,
    string Hint,
    MenuScriptArgumentKind Kind = MenuScriptArgumentKind.Text,
    bool AllowEmpty = false,
    IReadOnlyList<string>? Choices = null)
{
    public IReadOnlyList<string>? KnownValues => Kind == MenuScriptArgumentKind.ColorChannel
        ? ["forecolor", "backcolor", "bordercolor"]
        : Choices;
}

internal sealed partial record MenuScriptCommandChoice(
    string Name,
    string Label,
    IReadOnlyList<MenuScriptCommandArgument> Arguments,
    string PreviewNote = "Requires the game to preview.",
    string? Layout = null)
{
    // PS3 default_mp.elf: 61 names at 0x006f81e4; uiScript dispatches separately at 0x00248aac.
    public static IReadOnlyList<MenuScriptCommandChoice> All { get; } =
    [
        .. InterfaceCommands(),
        .. PlayerAndGameCommands(),
        new("uiScript", "Run a UI subcommand",
            [new("Subcommand", "Enter the native UI action, such as clearError",
                 Choices: ["clearError", "RefreshServers", "StartServer"]),
             new("Arguments", "Enter any arguments required by that subcommand in native syntax.",
                 MenuScriptArgumentKind.NativeArguments, AllowEmpty: true)])
    ];

    public override string ToString() => $"{Label} ({Name})";

    public string Usage => Arguments.Count == 0 ? $"{Name} takes no arguments." :
        FormatCommand(Arguments.Select(a => a.Kind == MenuScriptArgumentKind.NativeArguments
            ? "[arguments…]" : $"<{a.Label.ToLowerInvariant()}>").ToArray());

    public bool TryFormat(IReadOnlyList<string> values, out string script, out string error)
    {
        script = string.Empty;
        if (values.Count != Arguments.Count)
        {
            error = "Complete the arguments for this command.";
            return false;
        }

        var tokens = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            var argument = Arguments[i];
            string value = values[i];
            if (value.Any(char.IsControl))
            {
                error = $"{argument.Label} must fit on one line without control characters.";
                return false;
            }
            if (!argument.AllowEmpty && string.IsNullOrWhiteSpace(value))
            {
                error = $"Enter a value for {argument.Label.ToLowerInvariant()}.";
                return false;
            }

            if (!TryEncodeArgument(argument, value, out tokens[i], out error))
                return false;
        }

        script = FormatCommand(tokens);
        error = string.Empty;
        return true;
    }

    private string FormatCommand(string[] tokens)
    {
        string body = Layout is null ? string.Join(' ', tokens).TrimEnd()
            : string.Format(CultureInfo.InvariantCulture, Layout, tokens);
        return body.Length == 0 ? $"{Name} ;" : $"{Name} {body} ;";
    }

    private static bool TryEncodeArgument(
        MenuScriptCommandArgument argument, string value, out string token, out string error)
    {
        token = string.Empty;
        error = string.Empty;
        switch (argument.Kind)
        {
            case MenuScriptArgumentKind.Number:
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !float.IsFinite(number))
                    error = $"Enter a finite number for {argument.Label.ToLowerInvariant()}.";
                break;
            case MenuScriptArgumentKind.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    error = $"Enter a base-10 integer for {argument.Label.ToLowerInvariant()}.";
                break;
            case MenuScriptArgumentKind.Choice:
            case MenuScriptArgumentKind.ColorChannel:
                if (argument.KnownValues is { } choices &&
                    !choices.Contains(value, StringComparer.OrdinalIgnoreCase))
                    error = $"Choose one of {string.Join(", ", choices)}.";
                break;
            case MenuScriptArgumentKind.MenuPattern:
                if (!IsMenuPattern(value))
                    error = "Use at most one %s for the game variable and %% for a literal percent sign.";
                break;
            case MenuScriptArgumentKind.PlayerPath:
            case MenuScriptArgumentKind.PlayerValue:
            case MenuScriptArgumentKind.PlayerIndex:
                return TryEncodePlayerArgument(argument.Kind, value, out token, out error);
            case MenuScriptArgumentKind.NativeArguments:
                token = value.Trim();
                error = CheckRawArguments(token);
                return error.Length == 0;
        }
        if (error.Length != 0)
            return false;

        token = QuoteToken(value);
        return true;
    }

    // The PS3 quoted-token parser (0x0027b028) consumes escaped backslashes and quotes.
    private static string QuoteToken(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool IsMenuPattern(string value)
    {
        bool usedSubstitution = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (++i == value.Length) return false;
            if (value[i] == '%') continue;
            if (value[i] != 's' || usedSubstitution) return false;
            usedSubstitution = true;
        }
        return true;
    }

    private static string CheckRawArguments(string raw)
    {
        bool quoted = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char current = raw[i];
            if (quoted && current == '\\' && i + 1 < raw.Length && raw[i + 1] is '\\' or '"')
                i++;
            else if (current == '"')
                quoted = !quoted;
            else if (!quoted && current == ';')
                return "Enter only the arguments. Studio adds the command separator.";
        }
        return quoted ? "Close the quoted argument." : string.Empty;
    }
}
