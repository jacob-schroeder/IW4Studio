using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

// CoD4/IW3 names bit 0x100 splitGeo; W@W extends the native table with
// bit 0x8000 dontSplitLights. This compiler preserves both hints as authored.
internal static class MapToolFlags
{
    internal static bool ReadForCompilation(string directive)
    {
        var tokens = MapTokenizer.Tokenize(directive);
        if (tokens.Count == 0 || tokens[0].Value != "toolFlags") return false;
        if (tokens.Count < 3 || tokens[^1].Value != ";" || tokens.Any(token => token.Quoted))
            throw new NotSupportedException($"Tool directive '{directive}' is malformed. Expected unquoted toolFlags splitGeo and/or dontSplitLights;.");
        var flags = tokens.Skip(1).SkipLast(1).ToArray();
        if (flags.Length == 0 || flags.Any(token => token.Value is not ("splitGeo" or "dontSplitLights")))
            throw new NotSupportedException($"Tool directive '{directive}' is not supported. Expected only splitGeo and/or dontSplitLights in an unquoted toolFlags directive.");
        // Correlated splitGeo mesh/curve probes are byte-identical with/without
        // this hint. dontSplitLights is also output-neutral here because this
        // compiler does not perform native light-driven surface splitting.
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
        if (split)
        {
            int target = -1;
            for (int index = directives.Count - 1; index >= 0; index--)
            {
                var tokens = MapTokenizer.Tokenize(directives[index]);
                if (tokens.Count > 0 && tokens[0].Value == "toolFlags")
                {
                    target = index;
                    break;
                }
            }

            if (target < 0) directives.Add("toolFlags splitGeo;");
            else
            {
                var tokens = MapTokenizer.Tokenize(directives[target]);
                string[] remaining = tokens.Skip(1).SkipLast(1).Where(token => token.Value != "splitGeo").Select(token => token.Value).ToArray();
                directives[target] = "toolFlags splitGeo" + (remaining.Length == 0 ? ";" : " " + string.Join(' ', remaining) + ";");
            }
        }
    }
}
