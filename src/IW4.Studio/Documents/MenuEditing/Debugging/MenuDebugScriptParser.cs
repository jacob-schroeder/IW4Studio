using System.Text;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

internal sealed record MenuDebugScriptCommand(
    string RawText,
    IReadOnlyList<string> Tokens);

internal sealed record MenuDebugScriptParseResult(
    IReadOnlyList<MenuDebugScriptCommand> Commands,
    string? Failure)
{
    public bool IsValid => Failure is null;
}

/// <summary>
/// Bounded tokenizer for the small native Menu command subset supported by
/// the debugger. It recognizes quoted or bare tokens and semicolon command
/// separators; it does not evaluate substitutions or arbitrary UI script.
/// </summary>
internal static class MenuDebugScriptParser
{
    private const int MaximumScriptLength = 64 * 1024;
    private const int MaximumCommands = 256;
    private const int MaximumTokensPerCommand = 64;
    private const int MaximumTokenLength = 4096;

    public static MenuDebugScriptParseResult Parse(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Length > MaximumScriptLength)
        {
            return Invalid(
                $"Menu script length {script.Length:N0} exceeds the debugger limit of {MaximumScriptLength:N0} characters.");
        }

        var commands = new List<MenuDebugScriptCommand>();
        int position = 0;
        while (position < script.Length)
        {
            SkipWhitespaceAndSeparators(script, ref position);
            if (position >= script.Length)
                break;
            if (commands.Count >= MaximumCommands)
            {
                return Invalid(
                    $"Menu script contains more than {MaximumCommands:N0} commands.");
            }

            int commandStart = position;
            var tokens = new List<string>();
            while (position < script.Length && script[position] != ';')
            {
                SkipWhitespace(script, ref position);
                if (position >= script.Length || script[position] == ';')
                    break;
                if (tokens.Count >= MaximumTokensPerCommand)
                {
                    return Invalid(
                        $"Menu script command contains more than {MaximumTokensPerCommand:N0} tokens.");
                }

                if (!TryReadToken(script, ref position, out string token, out string? failure))
                    return Invalid(failure!);
                tokens.Add(token);
            }

            int commandEnd = position;
            if (position < script.Length && script[position] == ';')
                position++;
            if (tokens.Count == 0)
                continue;

            commands.Add(new MenuDebugScriptCommand(
                script[commandStart..commandEnd].Trim(),
                Array.AsReadOnly(tokens.ToArray())));
        }

        return new MenuDebugScriptParseResult(
            Array.AsReadOnly(commands.ToArray()),
            null);
    }

    private static bool TryReadToken(
        string script,
        ref int position,
        out string token,
        out string? failure)
    {
        failure = null;
        if (script[position] != '"')
        {
            int start = position;
            while (position < script.Length &&
                   script[position] != ';' &&
                   !char.IsWhiteSpace(script[position]))
            {
                position++;
            }
            int length = position - start;
            if (length > MaximumTokenLength)
            {
                token = string.Empty;
                failure = $"Menu script token exceeds {MaximumTokenLength:N0} characters.";
                return false;
            }
            token = script.Substring(start, length);
            return true;
        }

        position++;
        var value = new StringBuilder();
        while (position < script.Length)
        {
            char current = script[position++];
            if (current == '"')
            {
                token = value.ToString();
                return true;
            }
            if (current == '\\' && position < script.Length &&
                script[position] is '\\' or '"')
            {
                current = script[position++];
            }
            if (value.Length >= MaximumTokenLength)
            {
                token = string.Empty;
                failure = $"Quoted Menu script token exceeds {MaximumTokenLength:N0} characters.";
                return false;
            }
            value.Append(current);
        }

        token = string.Empty;
        failure = "Menu script contains an unterminated quoted token.";
        return false;
    }

    private static void SkipWhitespaceAndSeparators(string value, ref int position)
    {
        while (position < value.Length &&
               (char.IsWhiteSpace(value[position]) || value[position] == ';'))
        {
            position++;
        }
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
            position++;
    }

    private static MenuDebugScriptParseResult Invalid(string failure) =>
        new([], failure);
}
