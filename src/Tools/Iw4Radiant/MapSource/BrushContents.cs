using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

internal static class BrushContents
{
    internal static BrushKind Read(MapBrush brush)
    {
        BrushKind kind = BrushKind.Structural;
        foreach (string directive in brush.Directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count < 3 || tokens[0].Value != "contents" || tokens[^1].Value != ";") continue;
            foreach (var token in tokens.Skip(1).SkipLast(1))
                kind = token.Value switch
                {
                    "weaponClip" => BrushKind.WeaponClip,
                    "nonColliding" when kind != BrushKind.WeaponClip => BrushKind.NonColliding,
                    "detail" when kind == BrushKind.Structural => BrushKind.Detail,
                    _ => kind
                };
        }
        return kind;
    }

    internal static void Set(MapBrush brush, BrushKind kind)
    {
        // Preserve unrelated native contents flags and other brush directives.
        var contents = new List<string>();
        for (int index = brush.Directives.Count - 1; index >= 0; index--)
        {
            var tokens = MapTokenizer.Tokenize(brush.Directives[index]);
            if (tokens.Count < 3 || tokens[0].Value != "contents" || tokens[^1].Value != ";") continue;
            contents.InsertRange(0, tokens.Skip(1).SkipLast(1).Select(token => token.Value)
                .Where(value => value is not ("detail" or "nonColliding" or "weaponClip")));
            brush.Directives.RemoveAt(index);
        }
        if (kind != BrushKind.Structural)
            contents.Add(kind switch
            {
                BrushKind.Detail => "detail",
                BrushKind.NonColliding => "nonColliding",
                BrushKind.WeaponClip => "weaponClip",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });
        if (contents.Count > 0) brush.Directives.Add("contents " + string.Join(' ', contents.Distinct()) + ";");
    }
}
