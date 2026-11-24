namespace IW4.Game.Assets.XAnim;

/// <summary>
/// Asset type values found in stock PS3 XAnimParts.
/// </summary>
public enum XAnimAssetType : byte
{
    Absolute = 0,
    Relative = 1,
    Delta = 2,
    Additive = 6,
    /// <summary>Delta animation that supports pitch, roll, and yaw at the root.</summary>
    Delta3D = 7
}
