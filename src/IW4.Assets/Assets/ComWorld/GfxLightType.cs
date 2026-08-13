namespace IW4.Assets.Assets.ComWorld;

/// <summary>
/// Primary-light type serialized by console IW4 ComWorld assets.
/// Shadow-map versions are runtime selector columns and are intentionally not
/// represented as serialized primary-light types.
/// </summary>
public enum GfxLightType : byte
{
    None = 0,
    Directional = 1,
    Spot = 2,
    Omni = 3,
    Count = 4
}
