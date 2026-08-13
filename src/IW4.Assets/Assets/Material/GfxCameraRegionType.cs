namespace IW4.Assets.Assets.Material;

/// <summary>
/// Camera-region routing stored by a console IW4 material.
/// </summary>
public enum GfxCameraRegionType : byte
{
    LitOpaque = 0,
    LitTrans = 1,
    Emissive = 2,
    DepthHack = 3,
    LightMapOpaque = 4,
    Count = 5,
    None = Count
}
