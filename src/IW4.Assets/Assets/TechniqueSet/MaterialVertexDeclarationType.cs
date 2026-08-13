namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 backend material vertex-declaration rows. Rows 2 through 4 are named
/// for their observed PS3 consumers; rows 5 through 16 correspond exactly to
/// <see cref="MaterialWorldVertexFormat"/> values plus five.
/// </summary>
public enum MaterialVertexDeclarationType : byte
{
    Generic = 0x00,
    Packed = 0x01,
    StaticModelCache = 0x02,
    World = 0x03,
    WorldPositionOnly = 0x04,
    WorldTex1Nrm1 = 0x05,
    WorldTex2Nrm1 = 0x06,
    WorldTex2Nrm2 = 0x07,
    WorldTex3Nrm1 = 0x08,
    WorldTex3Nrm2 = 0x09,
    WorldTex3Nrm3 = 0x0A,
    WorldTex4Nrm1 = 0x0B,
    WorldTex4Nrm2 = 0x0C,
    WorldTex4Nrm3 = 0x0D,
    WorldTex5Nrm1 = 0x0E,
    WorldTex5Nrm2 = 0x0F,
    WorldTex5Nrm3 = 0x10,
    Count = 0x11
}
