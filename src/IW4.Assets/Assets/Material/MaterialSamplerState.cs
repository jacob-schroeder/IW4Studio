namespace IW4.Assets.Assets.Material;

/// <summary>
/// Encoded PS3 material sampler-state byte. Filter and mip-map members are
/// mutually exclusive subfield values; clamp members are independent bits.
/// </summary>
[Flags]
public enum MaterialSamplerState : byte
{
    None = 0x00,
    FilterDisabled = 0x00,
    FilterNearest = 0x01,
    FilterLinear = 0x02,
    FilterAnisotropic2X = 0x03,
    FilterAnisotropic4X = 0x04,
    FilterMask = 0x07,

    MipMapDisabled = 0x00,
    MipMapNearest = 0x08,
    MipMapLinear = 0x10,
    MipMapMask = 0x18,

    ClampU = 0x20,
    ClampV = 0x40,
    ClampW = 0x80,
    ClampMask = 0xE0
}
