namespace Iw4Radiant.MapSource.Parsing;

// Native Com_ParseOnLine ignores line breaks inside block comments.
internal readonly record struct MapToken(string Value, int Start, int End, int Line, int LogicalLine, bool Quoted = false);
