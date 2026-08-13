namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 material vertex-declaration source slot.
/// </summary>
public enum MaterialStreamSource : byte
{
    Position = 0x0,
    Color = 0x1,
    TexCoord0 = 0x2,
    Normal = 0x3,

    PreOptionalBegin = 0x4,
    Tangent = PreOptionalBegin,

    OptionalBegin = 0x5,
    TexCoord1 = OptionalBegin,
    TexCoord2 = 0x6,
    NormalTransform0 = 0x7,
    NormalTransform1 = 0x8,

    Count = 0x9
}
