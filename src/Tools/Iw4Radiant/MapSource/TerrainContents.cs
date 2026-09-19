using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

/// <summary>Native iwmap mesh collision directives, independent of material alpha.</summary>
internal static class TerrainContents
{
    internal static bool ReadNonColliding(MapTerrain terrain)
    {
        bool nonColliding = false;
        bool hasContents = false;
        foreach (string directive in terrain.Directives)
        {
            List<MapToken> tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count == 2 && !tokens[0].Quoted && tokens[0].Value == "layer" && tokens[1].Quoted)
                continue;
            if (MapToolFlags.ReadForCompilation(directive)) continue;
            if (tokens.Count < 3 || tokens.Any(token => token.Quoted) ||
                tokens[0].Value != "contents" || tokens[^1].Value != ";")
                throw new NotSupportedException($"Terrain directive '{directive}' is not supported by compilation.");
            if (hasContents)
                throw new InvalidDataException("A terrain mesh can have only one contents directive.");
            hasContents = true;
            foreach (MapToken token in tokens.Skip(1).SkipLast(1))
                switch (token.Value)
                {
                    case "detail": break;
                    case "nonColliding": nonColliding = true; break;
                    default: throw new NotSupportedException($"Terrain contents '{token.Value}' is not supported by compilation.");
                }
        }
        return nonColliding;
    }

    internal static void SetNonColliding(MapTerrain terrain, bool nonColliding)
    {
        if (ReadNonColliding(terrain) == nonColliding) return;
        for (int index = 0; index < terrain.Directives.Count; index++)
        {
            List<MapToken> tokens = MapTokenizer.Tokenize(terrain.Directives[index]);
            if (tokens[0].Value != "contents") continue;
            var remaining = tokens.Skip(1).SkipLast(1).Select(token => token.Value)
                .Where(value => value != "nonColliding").ToList();
            if (nonColliding) remaining.Add("nonColliding");
            if (remaining.Count == 0) terrain.Directives.RemoveAt(index);
            else terrain.Directives[index] = "contents " + string.Join(' ', remaining) + ";";
            return;
        }
        if (nonColliding) terrain.Directives.Add("contents nonColliding;");
    }
}
