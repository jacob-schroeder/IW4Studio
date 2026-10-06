using Iw4Radiant.MapSource.Parsing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.MapSource;

internal static class BrushContents
{
    // IW Radiant's native brush contents table. Detail combines with the
    // material contents; the other two entries are complete trace categories.
    private const int DetailContents = 0x08000000;
    private const int NonCollidingContents = 0x08000004;
    private const int WeaponClipContents = 0x08002080;
    private const int GlassContents = 0x08000010;

    internal static BrushKind Read(MapBrush brush)
    {
        if (brush.Faces.Count > 0 && brush.Faces.All(face => ClipBrushMaterial.IsLadder(face.Material)))
            return BrushKind.Ladder;
        BrushKind kind = BrushKind.Structural;
        foreach (string directive in brush.Directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count < 3 || tokens[0].Value != "contents" || tokens[^1].Value != ";") continue;
            foreach (var token in tokens.Skip(1).SkipLast(1))
                kind = token.Value switch
                {
                    "weaponClip" => BrushKind.WeaponClip,
                    "glass" => BrushKind.BreakableGlass,
                    "nonColliding" when kind != BrushKind.WeaponClip => BrushKind.NonColliding,
                    "detail" when kind == BrushKind.Structural => BrushKind.Detail,
                    _ => kind
                };
        }
        return kind;
    }

    internal static void Set(MapBrush brush, BrushKind kind)
    {
        if (kind != BrushKind.BreakableGlass) BrushGlass.Clear(brush);
        foreach (MapFace face in brush.Faces)
        {
            if (kind == BrushKind.Ladder)
                face.Material = ClipBrushMaterial.Ladder;
            else if (ClipBrushMaterial.IsLadder(face.Material))
                face.Material = CaulkMaterial.Name;
        }
        // Preserve unrelated native contents flags and other brush directives.
        var contents = new List<string>();
        for (int index = brush.Directives.Count - 1; index >= 0; index--)
        {
            var tokens = MapTokenizer.Tokenize(brush.Directives[index]);
            if (tokens.Count < 3 || tokens[0].Value != "contents" || tokens[^1].Value != ";") continue;
            contents.InsertRange(0, tokens.Skip(1).SkipLast(1).Select(token => token.Value)
                .Where(value => value is not ("detail" or "nonColliding" or "weaponClip" or "glass")));
            brush.Directives.RemoveAt(index);
        }
        if (kind is not (BrushKind.Structural or BrushKind.Ladder))
            contents.Add(kind switch
            {
                BrushKind.Detail => "detail",
                BrushKind.NonColliding => "nonColliding",
                BrushKind.WeaponClip => "weaponClip",
                BrushKind.BreakableGlass => "glass",
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
                "glass" => BrushKind.BreakableGlass,
                _ => throw new NotSupportedException($"Brush contents '{tokens[1].Value}' is not supported by compilation.")
            };
        }
        if (brush.Faces.Count > 0 && brush.Faces.All(face => ClipBrushMaterial.IsLadder(face.Material)))
        {
            if (found)
                throw new NotSupportedException("Ladder brushes define their native contents through the ladder material. Remove the contents override.");
            return BrushKind.Ladder;
        }
        return kind;
    }

    internal static int Compile(BrushKind kind, int materialContents) => kind switch
    {
        BrushKind.Structural => materialContents,
        BrushKind.Detail => materialContents | DetailContents,
        BrushKind.NonColliding => NonCollidingContents,
        BrushKind.WeaponClip => WeaponClipContents,
        BrushKind.BreakableGlass => GlassContents,
        BrushKind.Ladder => ClipBrushMaterial.Contents,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static bool BlocksPlayer(BrushKind kind) => kind is BrushKind.Structural or BrushKind.Detail or BrushKind.BreakableGlass or BrushKind.Ladder;
}
