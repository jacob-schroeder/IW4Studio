namespace IW4.Radiant.Desktop.MapSource.Parsing;

// Native Com_ParseOnLine ignores line breaks inside block comments.
internal readonly record struct MapToken(string TextSource, int Start, int End, int Line, int LogicalLine, bool Quoted = false)
{
    // Quoted text is already unescaped. Bare tokens refer to their original source range.
    public ReadOnlySpan<char> Span => Quoted ? TextSource.AsSpan() : TextSource.AsSpan(Start, End - Start);
    public string Value => Quoted ? TextSource : Span.ToString();
}
