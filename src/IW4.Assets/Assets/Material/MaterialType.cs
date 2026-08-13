namespace IW4.Assets.Assets.Material;

/// <summary>
/// Material-template class selected by the canonical name prefix.
/// </summary>
public enum MaterialType : byte
{
    Default = 0x0,
    Model = 0x1,                 // m_
    ModelVertexColor = 0x2,      // mc_
    ModelVertexColorGrey = 0x3,  // mg_
    World = 0x4,                 // w_
    WorldVertexColor = 0x5,      // wc_
    Count = 0x6
}
