using System.Text;
using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Character;

public enum CharacterFieldKind
{
    Body,
    HeadAlias,
    Viewhands,
    Voice
}

public sealed record CharacterScriptField(string? Value, bool CanEdit, string? Reason);

/// <summary>Editable source locations for the stock Character script conventions.</summary>
public sealed partial class CharacterScriptProjection
{
    private const string CharacterPath = @"codescripts\character";
    private const string AliasPathPrefix = @"xmodelalias\";
    private readonly string _source;
    private readonly IReadOnlyDictionary<CharacterFieldKind, FieldEdit> _fields;

    private CharacterScriptProjection(string source, Dictionary<CharacterFieldKind, FieldEdit> fields)
    {
        _source = source;
        _fields = fields;
    }

    public CharacterScriptField Body => _fields[CharacterFieldKind.Body].Display;
    public CharacterScriptField HeadAlias => _fields[CharacterFieldKind.HeadAlias].Display;
    public CharacterScriptField Viewhands => _fields[CharacterFieldKind.Viewhands].Display;
    public CharacterScriptField Voice => _fields[CharacterFieldKind.Voice].Display;

    public static bool IsCharacterPath(string? name)
    {
        if (name is null)
            return false;

        ReadOnlySpan<char> path = name.AsSpan();
        if (path.StartsWith(",", StringComparison.Ordinal))
            path = path[1..];

        return path.Length > "character/.gsc".Length &&
            (path.StartsWith("character/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(@"character\", StringComparison.OrdinalIgnoreCase)) &&
            path.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) &&
            path[^5] is not ('/' or '\\');
    }

    public static CharacterScriptProjection Parse(string source)
    {
        var text = new GscSourceText(source);
        GscSyntaxTree? tree = new GscSyntaxAnalyzer().Analyze(text).SyntaxTree;
        if (tree is null)
            return Unavailable(source, "The Character source contains a syntax error.");

        GscSyntaxNode[] main = FindFunctions(text, tree, "main");
        if (main.Length != 1 || !TakesNoParameters(main[0]))
            return Unavailable(source, "The Character source requires one unambiguous main() function.");

        var candidates = Enum.GetValues<CharacterFieldKind>()
            .ToDictionary(kind => kind, _ => new List<FieldEdit?>());

        foreach ((GscSyntaxNode node, bool direct) in FunctionOperations(main[0]))
        {
            if (IsVoiceWrite(text, node))
            {
                candidates[CharacterFieldKind.Voice].Add(direct && node.Production == GscProduction.AssignmentStatement
                    ? StringEdit(text, Node(node, 2)) : null);
            }
            else if (ReadCall(text, node) is { } call && CharacterCallKind(call) is { } kind)
            {
                FieldEdit? edit = direct && call.IsDirect ? ReadCharacterCall(text, kind, call) : null;
                candidates[kind].Add(edit);
            }
        }

        var fields = candidates.ToDictionary(entry => entry.Key, entry => entry.Value switch
        {
            [] => FieldEdit.Unavailable("No recognized setting was found in main()."),
            [{ } edit] => edit,
            _ => FieldEdit.Unavailable("The setting in main() is dynamic or ambiguous.")
        });

        GscSyntaxNode[] precache = FindFunctions(text, tree, "precache");
        if (precache.Length != 0)
            AddPrecacheEdits(text, precache, fields);

        return new CharacterScriptProjection(source, fields);
    }

    public bool TryReplace(CharacterFieldKind kind, string value, out string source, out string? error)
    {
        source = _source;
        FieldEdit field = _fields[kind];
        error = field.Display.Reason;
        if (!field.Display.CanEdit)
            return false;

        if (string.Equals(field.Display.Value, value, StringComparison.Ordinal))
            return true;

        if (kind == CharacterFieldKind.HeadAlias ? !IsAliasName(value) : !IsStringValue(value))
        {
            error = kind == CharacterFieldKind.HeadAlias
                ? "Enter a head alias name using letters, digits, or underscores."
                : "Enter a non-empty value without control characters.";
            return false;
        }

        string literal = Quote(value);
        try
        {
            GscLexResult lexed = GscLexer.Lex(new GscSourceText(literal), CancellationToken.None);
            if (lexed.Error is not null ||
                lexed.Tokens is not [{ Kind: GscTokenKind.String, Span: var span }] ||
                span.Length != literal.Length ||
                !string.Equals(GscConstantEvaluator.DecodeString(literal), value, StringComparison.Ordinal))
            {
                error = lexed.Error?.Message ?? "The value cannot be written as a GSC string.";
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            error = "The value contains invalid characters.";
            return false;
        }

        var edits = field.StringSpans.Select(span => (Span: span, Value: literal))
            .Concat(field.AliasPathSpans.Select(span => (Span: span, Value: value)));
        var updated = new StringBuilder(_source);
        foreach ((GscTextSpan span, string replacement) in edits.OrderByDescending(edit => edit.Span.Start))
            updated.Remove(span.Start, span.Length).Insert(span.Start, replacement);

        source = updated.ToString();
        return true;
    }

    private static CharacterScriptProjection Unavailable(string source, string reason) => new(
        source,
        Enum.GetValues<CharacterFieldKind>().ToDictionary(kind => kind, _ => FieldEdit.Unavailable(reason)));

    private static CharacterFieldKind? CharacterCallKind(NamedCall call)
    {
        if (call.Path is null && call.IsSelfMethod)
        {
            return call.Name switch
            {
                "setmodel" => CharacterFieldKind.Body,
                "setviewmodel" => CharacterFieldKind.Viewhands,
                _ => null
            };
        }

        return !call.IsMethod && SamePath(call.Path, CharacterPath) && call.Name == "attachhead"
            ? CharacterFieldKind.HeadAlias : null;
    }

    private static FieldEdit? ReadCharacterCall(GscSourceText text, CharacterFieldKind kind, NamedCall call)
    {
        if (kind != CharacterFieldKind.HeadAlias)
            return call.Arguments is [var argument] ? StringEdit(text, argument) : null;

        if (call.Arguments is not [var key, var alias] ||
            ReadString(text, key) is not { } name ||
            ReadAliasCall(text, alias) is not { } reference ||
            !string.Equals(name, reference.Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FieldEdit(new(name, true, null), [Unwrap(key).Span], [reference.PathSpan]);
    }

    private static void AddPrecacheEdits(
        GscSourceText text,
        GscSyntaxNode[] functions,
        Dictionary<CharacterFieldKind, FieldEdit> fields)
    {
        string? body = fields[CharacterFieldKind.Body].Display.Value;
        string? hands = fields[CharacterFieldKind.Viewhands].Display.Value;
        (NamedCall Call, bool Direct)[] calls = functions.SelectMany(FunctionOperations)
            .Select(operation => (Call: ReadCall(text, operation.Node), operation.Direct))
            .Where(operation => operation.Call is not null)
            .Select(operation => (operation.Call.GetValueOrDefault(), operation.Direct))
            .ToArray();

        foreach (CharacterFieldKind kind in fields.Keys.Where(kind => kind != CharacterFieldKind.Voice).ToArray())
        {
            FieldEdit field = fields[kind];
            if (!field.Display.CanEdit || field.Display.Value is not { } value)
                continue;

            var matches = new List<(GscTextSpan Span, bool Direct)>();
            foreach ((NamedCall call, bool direct) in calls)
            {
                if (call.IsMethod || call.Arguments is not [var argument])
                    continue;

                if (kind == CharacterFieldKind.HeadAlias)
                {
                    if (SamePath(call.Path, CharacterPath) && call.Name == "precachemodelarray" &&
                        ReadAliasCall(text, argument) is { } alias &&
                        string.Equals(alias.Name, value, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add((alias.PathSpan, direct && call.IsDirect));
                    }
                }
                else if (call.Path is null && call.Name == "precachemodel" &&
                    string.Equals(ReadString(text, argument), value, StringComparison.Ordinal))
                {
                    matches.Add((Unwrap(argument).Span, direct && call.IsDirect));
                }
            }

            if (matches.Count == 0)
                continue;

            bool sharedModel = kind != CharacterFieldKind.HeadAlias && string.Equals(body, hands, StringComparison.Ordinal);
            if (functions.Length != 1 || !TakesNoParameters(functions[0]) ||
                matches.Count != 1 || !matches[0].Direct || sharedModel)
            {
                fields[kind] = new FieldEdit(new(value, false, "The matching precache calls are ambiguous."), [], []);
            }
            else
            {
                GscTextSpan span = matches[0].Span;
                fields[kind] = kind == CharacterFieldKind.HeadAlias
                    ? field with { AliasPathSpans = [.. field.AliasPathSpans, span] }
                    : field with { StringSpans = [.. field.StringSpans, span] };
            }
        }
    }

    private static FieldEdit? StringEdit(GscSourceText text, GscSyntaxNode argument) =>
        ReadString(text, argument) is { } value ? new(new(value, true, null), [Unwrap(argument).Span], []) : null;

    private static string? ReadString(GscSourceText text, GscSyntaxNode expression)
    {
        GscSyntaxNode literal = Unwrap(expression);
        if (literal.Production != GscProduction.StringLiteral)
            return null;

        string value = GscConstantEvaluator.DecodeString(text.GetText(literal.Span));
        return IsStringValue(value) ? value : null;
    }

    private static bool IsStringValue(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(character => !char.IsControl(character));

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool IsAliasName(string value) => !string.IsNullOrEmpty(value) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static AliasReference? ReadAliasCall(GscSourceText text, GscSyntaxNode expression)
    {
        if (ReadCall(text, Unwrap(expression)) is not
            { IsDirect: true, IsMethod: false, Name: "main", Arguments: [], Path: { } path, PathSpan: { } span })
        {
            return null;
        }

        string normalizedPath = path.Replace('/', '\\');
        if (!normalizedPath.StartsWith(AliasPathPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string name = normalizedPath[AliasPathPrefix.Length..];
        return IsAliasName(name) ? new(name, new GscTextSpan(span.End - name.Length, name.Length)) : null;
    }

    private static bool SamePath(string? actual, string expected) =>
        string.Equals(actual?.Replace('/', '\\'), expected, StringComparison.OrdinalIgnoreCase);

    private sealed record FieldEdit(CharacterScriptField Display, GscTextSpan[] StringSpans, GscTextSpan[] AliasPathSpans)
    {
        public static FieldEdit Unavailable(string reason) => new(new(null, false, reason), [], []);
    }

    private readonly record struct AliasReference(string Name, GscTextSpan PathSpan);
}
