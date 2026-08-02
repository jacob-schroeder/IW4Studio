using IW4.Assets.Assets.Material;

namespace IW4.Render.Materials;

/// <summary>
/// Classifies exact material-table tag/hash tuples. Image filenames are
/// deliberately never consulted.
/// </summary>
public static class MapRenderEditorMaterialTextureRoleClassifier
{
    public const uint BaseColorHash = 0xA0AB1041u;
    public const uint ColorLayer1Hash = 0xB60D1850u;
    public const uint ColorLayer2Hash = 0xB60D1853u;
    public const uint ColorLayer3Hash = 0xB60D1852u;
    public const uint ColorLayer4Hash = 0xB60D1855u;
    public const uint BaseNormalHash = 0x59D30D0Fu;
    public const uint NormalLayer1Hash = 0x9434AEDEu;
    public const uint NormalLayer2Hash = 0x9434AEDDu;
    public const uint NormalLayer3Hash = 0x9434AEDCu;
    public const uint BaseSpecularHash = 0x34ECCCB3u;
    public const uint SpecularLayer1Hash = 0xD2866322u;
    public const uint SpecularLayer2Hash = 0xD2866321u;
    public const uint DetailHash = 0xEB529B4Du;

    private const byte ColorSemantic = 0x02;
    private const byte NormalSemantic = 0x05;
    private const byte SpecularSemantic = 0x08;

    private static readonly KnownTuple[] KnownTuples =
    [
        Exact('c', 'p', BaseColorHash, ColorSemantic,
            MapRenderEditorMaterialTextureRole.BaseColor),
        Exact('c', '1', ColorLayer1Hash, ColorSemantic,
            MapRenderEditorMaterialTextureRole.ColorLayer1),
        Exact('c', '2', ColorLayer2Hash, ColorSemantic,
            MapRenderEditorMaterialTextureRole.ColorLayer2),
        Exact('c', '3', ColorLayer3Hash, ColorSemantic,
            MapRenderEditorMaterialTextureRole.ColorLayer3),
        Exact('c', '4', ColorLayer4Hash, ColorSemantic,
            MapRenderEditorMaterialTextureRole.ColorLayer4),
        Exact('n', 'p', BaseNormalHash, NormalSemantic,
            MapRenderEditorMaterialTextureRole.BaseNormal),
        Exact('n', '1', NormalLayer1Hash, NormalSemantic,
            MapRenderEditorMaterialTextureRole.NormalLayer1),
        Exact('n', '2', NormalLayer2Hash, NormalSemantic,
            MapRenderEditorMaterialTextureRole.NormalLayer2),
        Exact('n', '3', NormalLayer3Hash, NormalSemantic,
            MapRenderEditorMaterialTextureRole.NormalLayer3),
        Exact('s', 'p', BaseSpecularHash, SpecularSemantic,
            MapRenderEditorMaterialTextureRole.BaseSpecular),
        Exact('s', '1', SpecularLayer1Hash, SpecularSemantic,
            MapRenderEditorMaterialTextureRole.SpecularLayer1),
        Exact('s', '2', SpecularLayer2Hash, SpecularSemantic,
            MapRenderEditorMaterialTextureRole.SpecularLayer2),
        new KnownTuple(
            (byte)'d',
            (byte)'p',
            DetailHash,
            null,
            MapRenderEditorMaterialTextureRole.DetailUnknownComposition)
    ];

    public static MapRenderEditorMaterialTextureClassification Classify(
        MaterialTextureDef texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        foreach (KnownTuple tuple in KnownTuples)
        {
            if (texture.NameStart == tuple.NameStart &&
                texture.NameEnd == tuple.NameEnd &&
                texture.NameHash == tuple.NameHash &&
                (!tuple.Semantic.HasValue || texture.Semantic == tuple.Semantic.Value))
            {
                return new MapRenderEditorMaterialTextureClassification(
                    tuple.Role);
            }
        }

        return new MapRenderEditorMaterialTextureClassification(
            MapRenderEditorMaterialTextureRole.Unknown);
    }

    internal static int DeterministicRoleOrder(
        MapRenderEditorMaterialTextureRole role) => role switch
        {
            MapRenderEditorMaterialTextureRole.BaseColor => 0,
            MapRenderEditorMaterialTextureRole.ColorLayer1 => 1,
            MapRenderEditorMaterialTextureRole.ColorLayer2 => 2,
            MapRenderEditorMaterialTextureRole.ColorLayer3 => 3,
            MapRenderEditorMaterialTextureRole.ColorLayer4 => 4,
            MapRenderEditorMaterialTextureRole.BaseNormal => 5,
            MapRenderEditorMaterialTextureRole.NormalLayer1 => 6,
            MapRenderEditorMaterialTextureRole.NormalLayer2 => 7,
            MapRenderEditorMaterialTextureRole.NormalLayer3 => 8,
            MapRenderEditorMaterialTextureRole.BaseSpecular => 9,
            MapRenderEditorMaterialTextureRole.SpecularLayer1 => 10,
            MapRenderEditorMaterialTextureRole.SpecularLayer2 => 11,
            MapRenderEditorMaterialTextureRole.DetailUnknownComposition => 12,
            MapRenderEditorMaterialTextureRole.Unknown => int.MaxValue,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

    private static KnownTuple Exact(
        char nameStart,
        char nameEnd,
        uint nameHash,
        byte semantic,
        MapRenderEditorMaterialTextureRole role) =>
        new(
            (byte)nameStart,
            (byte)nameEnd,
            nameHash,
            semantic,
            role);

    private readonly record struct KnownTuple(
        byte NameStart,
        byte NameEnd,
        uint NameHash,
        byte? Semantic,
        MapRenderEditorMaterialTextureRole Role);
}
