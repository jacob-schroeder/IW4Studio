namespace IW4.Game.Assets.XAnim;

/// <summary>
/// Known stored XAnimParts asset type bytes. Other byte values remain valid
/// in source and zone data and must be preserved unchanged.
/// </summary>
public enum XAnimAssetType : byte
{
    Relative = 1,
    Delta = 2,
    Additive = 6
}
