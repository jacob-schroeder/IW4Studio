namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 IW4 material-technique flags. Console values differ from the PC flag
/// layout; names here follow PS3 consumers and captured shader families.
/// </summary>
[Flags]
public enum MaterialTechniqueFlags : ushort
{
    None = 0,
    NeedsResolvedPostSun = 0x0001,
    NeedsResolvedScene = 0x0002,
    ZPrepass = 0x0004,
    DeclarationHasOptionalSource = 0x0008,
    UsesLightSpotFactors = 0x0010,
    UsesFloatZ = 0x0020
}
