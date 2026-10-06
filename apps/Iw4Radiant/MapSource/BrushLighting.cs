using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

// Editor bake attribute: controls light-grid bounds, not water rendering or shadows.
internal static class BrushLighting
{
    internal static bool Read(MapBrush brush)
    {
        bool enabled = true, found = false;
        foreach (string directive in brush.Directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count == 0 || tokens[0].Value != "lighting") continue;
            if (found || tokens.Count != 3 || tokens.Any(token => token.Quoted) ||
                tokens[1].Value is not ("0" or "1") || tokens[2].Value != ";")
                throw new InvalidDataException("A brush may have one lighting attribute: lighting 0; or lighting 1;.");
            found = true;
            enabled = tokens[1].Value == "1";
        }
        return enabled;
    }

    internal static void Set(MapBrush brush, bool enabled)
    {
        for (int index = brush.Directives.Count - 1; index >= 0; index--)
        {
            var tokens = MapTokenizer.Tokenize(brush.Directives[index]);
            if (tokens.Count > 0 && tokens[0].Value == "lighting") brush.Directives.RemoveAt(index);
        }
        brush.Directives.Add(enabled ? "lighting 1;" : "lighting 0;");
    }
}
