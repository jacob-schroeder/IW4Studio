using System.Text;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Extracts literal dvar assignments in command order for a preview scenario.</summary>
public static class MenuPreviewConfig
{
    public const string MultiplayerDefaultName = "default_mp.cfg";

    public static IReadOnlyDictionary<string, string> ReadAssignments(string text) =>
        ReadAssignments(text, null);

    internal static IReadOnlyDictionary<string, string> ReadAssignments(string text, Func<string, string?>? readInclude)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Read(text, values, readInclude, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return values;
    }

    private static void Read(string text, Dictionary<string, string> values,
        Func<string, string?>? readInclude, HashSet<string> active)
    {
        using var lines = new StringReader(text.TrimStart('\uFEFF'));
        bool blockComment = false;
        while (lines.ReadLine() is { } line)
        {
            var commandLine = new StringBuilder(line.Length);
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (blockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        blockComment = false;
                        i++;
                    }
                    continue;
                }
                if (!quoted && c == '/' && next == '/')
                    break;
                if (!quoted && c == '/' && next == '*')
                {
                    blockComment = true;
                    commandLine.Append(' ');
                    i++;
                    continue;
                }
                commandLine.Append(c);
                if (quoted && c == '\\' && next is '"' or '\\')
                    commandLine.Append(line[++i]);
                else if (c == '"')
                    quoted = !quoted;
            }
            var parsed = MenuDebugScriptParser.Parse(commandLine.ToString());
            if (parsed.Failure is { } failure)
                throw new InvalidDataException(failure);
            foreach (var command in parsed.Commands)
            {
                if (command.Tokens is [var verb, var file] &&
                    verb.Equals("exec", StringComparison.OrdinalIgnoreCase) && readInclude is not null)
                {
                    if (active.Count >= 32 || !active.Add(file))
                        throw new InvalidDataException($"Recursive or excessively nested cfg include '{file}'.");
                    string included = readInclude(file) ??
                        throw new InvalidDataException($"Referenced cfg '{file}' is unavailable.");
                    Read(included, values, readInclude, active);
                    active.Remove(file);
                }
                else if (command.Tokens is [var op, var key, var value] && key.Length > 0 &&
                    (op.Equals("set", StringComparison.OrdinalIgnoreCase) ||
                     op.Equals("seta", StringComparison.OrdinalIgnoreCase)))
                    values[key] = value;
            }
        }
    }
}
