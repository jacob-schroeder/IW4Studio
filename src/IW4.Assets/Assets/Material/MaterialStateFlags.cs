namespace IW4.Assets.Assets.Material;

/// <summary>
/// Serialized console IW4 material-state flags.
/// </summary>
[Flags]
public enum MaterialStateFlags : byte
{
    None = 0,
    CullBack = 0x01,
    CullFront = 0x02,
    Decal = 0x04,
    WritesDepth = 0x08,
    UsesDepthBuffer = 0x10,
    UsesStencilBuffer = 0x20,
    CullBackShadow = 0x40,
    CullFrontShadow = 0x80
}
