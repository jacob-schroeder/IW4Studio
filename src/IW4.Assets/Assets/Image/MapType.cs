namespace IW4.Assets.Assets.Image;

/// <summary>
/// Serialized PS3 GfxImage map shape.
/// </summary>
public enum MapType : byte
{
    None = 0x0,
    Invalid1 = 0x1,
    OneDimensional = 0x2,
    TwoDimensional = 0x3,
    ThreeDimensional = 0x4,
    Cube = 0x5,
    Count = 0x6
}
