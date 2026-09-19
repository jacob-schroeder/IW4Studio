using System.Text;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

// PS3 AnimTreeParseInternal (0x202100) and Com_Parse's space-delimited mode.
internal sealed class GscAnimationTree
{
    private readonly GscSourceText _source;
    private readonly string _text;
    private readonly IReadOnlySet<string> _requested;
    private readonly CancellationToken _cancellationToken;
    private int _position;
    private int _lastToken;
    internal HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase) { "root" };
    internal string? Error { get; private set; }
    internal int ErrorOffset { get; private set; }

    internal GscAnimationTree(GscSourceText source, IReadOnlySet<string> requested, CancellationToken cancellationToken)
    {
        _source = source;
        _text = Encoding.Latin1.GetString(source.Bytes);
        _requested = requested;
        _cancellationToken = cancellationToken;
        var parsed = ParseBlock(include: true, complete: false, loop: false);
        if (!parsed.EndOfFile && Error is null)
            Fail("bad token");
        if (parsed.Names.Contains("root") && _requested.Contains("root"))
            Fail("duplicate animation root");
    }

    private (bool EndOfFile, HashSet<string> Names) ParseBlock(bool include, bool complete, bool loop)
    {
        var siblings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        bool ignored = false;
        bool eof;
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string? token = Next(onLine: false);
            if (token is null) { eof = true; break; }
            bool completeChild = false, loopChild = false;
            if (Identifier(token))
            {
                if (current is not null && !ignored) names.Add(current);
                if (ignored && current is not null) siblings.Remove(current);
                current = token.ToLowerInvariant();
                if (!siblings.Add(current)) Fail("duplicate animation");
                ignored = !complete && !_requested.Contains(current);
                token = Next(onLine: true);
                if (token is null) continue;
                if (Identifier(token)) Fail("FIXME: aliases not yet implemented");
                if (token != ":") Fail("bad token");
                while ((token = Next(onLine: true)) is not null)
                {
                    switch (token.ToLowerInvariant())
                    {
                        case "loopsync": loopChild = true; break;
                        case "nonloopsync": break;
                        case "complete": completeChild = true; break;
                        case "additive": break;
                        default: Fail("unknown anim property"); break;
                    }
                }
                token = Next(onLine: false);
                if (token != "{") { Fail("properties cannot be applied to primitive animations"); eof = token is null; break; }
            }
            if (token == "{")
            {
                if (Next(onLine: true) is not null) Fail("token not allowed after '{'");
                if (current is null) Fail("no animation specified for this block");
                var child = ParseBlock(!ignored, complete || (completeChild && !ignored), loopChild);
                if (child.EndOfFile) Fail("unexpected end of file");
                if (child.Names.Count != 0)
                {
                    if (current is not null) names.Add(current);
                    foreach (string name in child.Names)
                    {
                        if (!names.Add(name) && _requested.Contains(name)) Fail($"duplicate animation {name}");
                    }
                }
                else if (current is not null) siblings.Remove(current);
                current = null;
                ignored = false;
                if (child.EndOfFile) { eof = true; break; }
                continue;
            }
            if (token != "}") Fail("bad token");
            if (Next(onLine: true) is not null) Fail("token not allowed after '}'");
            eof = false;
            break;
        }
        if (current is not null && !ignored) names.Add(current);
        if (include && names.Count == 0) names.Add(loop ? "void_loop" : "void");
        foreach (string name in names)
            Names.Add(name);
        return (eof, names);
    }

    private void Fail(string message)
    {
        if (Error is not null) return;
        Error = message;
        ErrorOffset = _source.GetTextSpan(_lastToken, 0).Start;
    }

    private static bool Identifier(string text) => text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private string? Next(bool onLine)
    {
        int saved = _position;
        string text = _text;
        while (_position < text.Length)
        {
            bool newline = false;
            while (_position < text.Length && text[_position] <= ' ')
            {
                if (text[_position] == '\0') { _position = text.Length; return null; }
                newline |= text[_position++] == '\n';
            }
            if (onLine && newline) { _position = saved; return null; }
            if (_position + 1 < text.Length && text[_position] == '/')
            {
                if (text[_position + 1] == '/')
                {
                    while (_position < text.Length && text[_position] != '\n') _position++;
                    continue;
                }
                if (text[_position + 1] == '*')
                {
                    int end = text.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                    _position = end < 0 ? text.Length : end + 2;
                    continue;
                }
            }
            break;
        }
        if (_position >= text.Length) return null;
        _lastToken = _position;
        var value = new StringBuilder();
        if (text[_position] == '"')
        {
            _position++;
            while (_position < text.Length)
            {
                char character = text[_position++];
                if (character == '"' || character == '\0') break;
                if (character == '\\' && _position < text.Length && text[_position] is '"' or '\\') character = text[_position++];
                if (value.Length < 1023) value.Append(character);
            }
        }
        else
        {
            while (_position < text.Length && text[_position] > ' ')
            {
                if (value.Length < 1023) value.Append(text[_position]);
                _position++;
            }
        }
        return value.ToString();
    }
}
