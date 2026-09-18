using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

internal static class BrushContents
{
    // IW Radiant's native brush contents table. Detail combines with the
    // material contents; the other two entries are complete trace categories.
    private const int DetailContents = 0x08000000;
    private const int NonCollidingContents = 0x08000004;
    private const int WeaponClipContents = 0x08002080;

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

    internal static BrushKind ReadForCompilation(MapBrush brush)
    {
        BrushKind kind = BrushKind.Structural;
        bool found = false;
        foreach (string directive in brush.Directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count == 0 || tokens[0].Quoted || tokens[0].Value != "contents") continue;
            if (found)
                throw new InvalidDataException("A brush can have only one contents directive.");
            found = true;
            if (tokens.Count != 3 || tokens[1].Quoted || tokens[2].Quoted || tokens[2].Value != ";")
                throw new NotSupportedException($"Brush contents directive '{directive}' is not supported by compilation.");
            kind = tokens[1].Value switch
            {
                "detail" => BrushKind.Detail,
                "nonColliding" => BrushKind.NonColliding,
                "weaponClip" => BrushKind.WeaponClip,
                _ => throw new NotSupportedException($"Brush contents '{tokens[1].Value}' is not supported by compilation.")
            };
        }
        return kind;
    }

    internal static int Compile(BrushKind kind, int materialContents) => kind switch
    {
        BrushKind.Structural => materialContents,
        BrushKind.Detail => materialContents | DetailContents,
        BrushKind.NonColliding => NonCollidingContents,
        BrushKind.WeaponClip => WeaponClipContents,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static bool BlocksPlayer(BrushKind kind) => kind is BrushKind.Structural or BrushKind.Detail;
}
