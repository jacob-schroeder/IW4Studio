namespace IW4.Render.Materials;

/// <summary>
/// Editor-only interpretation of one material texture-table row. These roles
/// describe resource identity; they do not claim the selected PS3 shader's
/// composition equation, channel swizzle, or color-space behavior.
/// </summary>
public enum EditorMaterialTextureRole
{
    Unknown = 0,
    BaseColor,
    ColorLayer1,
    ColorLayer2,
    ColorLayer3,
    ColorLayer4,
    BaseNormal,
    NormalLayer1,
    NormalLayer2,
    NormalLayer3,
    BaseSpecular,
    SpecularLayer1,
    SpecularLayer2,
    DetailUnknownComposition
}
