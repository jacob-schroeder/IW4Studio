using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

// The native Radiant brush/mesh toolFlags table names bit 0x100 splitGeo.
internal static class MapToolFlags
{
    internal static bool ReadForCompilation(string directive)
    {
        var tokens = MapTokenizer.Tokenize(directive);
        if (tokens.Count == 0 || tokens[0].Value != "toolFlags") return false;
        if (tokens.Count != 3 || tokens.Any(token => token.Quoted) || tokens[1].Value != "splitGeo" || tokens[2].Value != ";")
            throw new NotSupportedException($"Tool directive '{directive}' is not supported. The recovered native form is toolFlags splitGeo;.");
        // Correlated native mesh/curve probes are byte-identical with/without
        // this hint. Retain authored tessellation; brush parity is not established.
        return true;
    }

    internal static void SetSplitCoplanar(List<string> directives, bool split)
    {
        for (int index = directives.Count - 1; index >= 0; index--)
        {
            var tokens = MapTokenizer.Tokenize(directives[index]);
            if (tokens.Count == 0 || tokens[0].Value != "toolFlags") continue;
            if (tokens.Count < 2 || tokens[^1].Value != ";" || tokens.Any(token => token.Quoted))
                throw new FormatException("The toolFlags directive must end with a semicolon and contain unquoted flags.");
            string[] remaining = tokens.Skip(1).SkipLast(1).Where(token => token.Value != "splitGeo").Select(token => token.Value).ToArray();
            if (remaining.Length == 0) directives.RemoveAt(index);
            else directives[index] = "toolFlags " + string.Join(' ', remaining) + ";";
        }
        if (split) directives.Add("toolFlags splitGeo;");
    }
}
