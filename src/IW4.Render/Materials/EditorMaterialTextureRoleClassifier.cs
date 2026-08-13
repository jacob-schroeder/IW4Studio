using IW4.Assets.Assets.Material;

namespace IW4.Render.Materials;

/// <summary>
/// Classifies exact material-table tag/hash tuples. Image filenames are
/// deliberately never consulted.
/// </summary>
public static class EditorMaterialTextureRoleClassifier
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
            EditorMaterialTextureRole.BaseColor),
        Exact('c', '1', ColorLayer1Hash, ColorSemantic,
            EditorMaterialTextureRole.ColorLayer1),
        Exact('c', '2', ColorLayer2Hash, ColorSemantic,
            EditorMaterialTextureRole.ColorLayer2),
        Exact('c', '3', ColorLayer3Hash, ColorSemantic,
            EditorMaterialTextureRole.ColorLayer3),
        Exact('c', '4', ColorLayer4Hash, ColorSemantic,
            EditorMaterialTextureRole.ColorLayer4),
        Exact('n', 'p', BaseNormalHash, NormalSemantic,
            EditorMaterialTextureRole.BaseNormal),
        Exact('n', '1', NormalLayer1Hash, NormalSemantic,
            EditorMaterialTextureRole.NormalLayer1),
        Exact('n', '2', NormalLayer2Hash, NormalSemantic,
            EditorMaterialTextureRole.NormalLayer2),
        Exact('n', '3', NormalLayer3Hash, NormalSemantic,
            EditorMaterialTextureRole.NormalLayer3),
        Exact('s', 'p', BaseSpecularHash, SpecularSemantic,
            EditorMaterialTextureRole.BaseSpecular),
        Exact('s', '1', SpecularLayer1Hash, SpecularSemantic,
            EditorMaterialTextureRole.SpecularLayer1),
        Exact('s', '2', SpecularLayer2Hash, SpecularSemantic,
            EditorMaterialTextureRole.SpecularLayer2),
        new KnownTuple(
            (byte)'d',
            (byte)'p',
            DetailHash,
            null,
            EditorMaterialTextureRole.DetailUnknownComposition)
    ];

    public static EditorMaterialTextureClassification Classify(
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
                return new EditorMaterialTextureClassification(
                    tuple.Role);
            }
        }

        return new EditorMaterialTextureClassification(
            EditorMaterialTextureRole.Unknown);
    }

    internal static int DeterministicRoleOrder(
        EditorMaterialTextureRole role) => role switch
        {
            EditorMaterialTextureRole.BaseColor => 0,
            EditorMaterialTextureRole.ColorLayer1 => 1,
            EditorMaterialTextureRole.ColorLayer2 => 2,
            EditorMaterialTextureRole.ColorLayer3 => 3,
            EditorMaterialTextureRole.ColorLayer4 => 4,
            EditorMaterialTextureRole.BaseNormal => 5,
            EditorMaterialTextureRole.NormalLayer1 => 6,
            EditorMaterialTextureRole.NormalLayer2 => 7,
            EditorMaterialTextureRole.NormalLayer3 => 8,
            EditorMaterialTextureRole.BaseSpecular => 9,
            EditorMaterialTextureRole.SpecularLayer1 => 10,
            EditorMaterialTextureRole.SpecularLayer2 => 11,
            EditorMaterialTextureRole.DetailUnknownComposition => 12,
            EditorMaterialTextureRole.Unknown => int.MaxValue,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

    private static KnownTuple Exact(
        char nameStart,
        char nameEnd,
        uint nameHash,
        byte semantic,
        EditorMaterialTextureRole role) =>
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
        EditorMaterialTextureRole Role);
}
