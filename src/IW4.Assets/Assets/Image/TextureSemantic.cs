namespace IW4.Assets.Assets.Image;

/// <summary>
/// Serialized IW4 texture usage. The numeric values are shared by material
/// texture rows and GfxImage headers on PS3.
/// </summary>
public enum TextureSemantic : byte
{
    TwoDimensional = 0x0,
    Function = 0x1,
    ColorMap = 0x2,
    DetailMap = 0x3,
    Unused2 = 0x4,
    NormalMap = 0x5,
    Unused3 = 0x6,
    Unused4 = 0x7,
    SpecularMap = 0x8,
    Unused5 = 0x9,
    Unused6 = 0xA,
    WaterMap = 0xB
}
