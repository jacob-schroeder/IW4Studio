namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 material vertex-declaration destination. Values are RSX vertex input
/// indices, not the D3D destination ordinals used by the PC asset layout.
/// </summary>
public enum MaterialStreamDestination : byte
{
    Position = 0x0,
    Weight = 0x1,
    Normal = 0x2,
    Color0 = 0x3,
    Color1 = 0x4,
    Fog = 0x5,
    TexCoord0 = 0x8,
    TexCoord1 = 0x9,
    TexCoord2 = 0xA,
    TexCoord3 = 0xB,
    TexCoord4 = 0xC,
    TexCoord5 = 0xD,
    TexCoord6 = 0xE,
    TexCoord7 = 0xF,
    Count = 0x10
}
