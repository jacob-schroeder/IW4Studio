namespace IW4.Game.Assets.GameMap;

/// <summary>
/// PC-correlated interpretation of the path-node minimum-use-distance/error
/// union. Its use and values on PS3 have not been independently confirmed.
/// </summary>
/// <remarks>
/// Source: OpenAssetTools commit 30d2d498bd8e3f2401037b24f3db6f96d39554c4,
/// src/Common/Game/IW4/IW4_Assets.h, PathNodeErrorCode.
/// </remarks>
public enum PathNodeErrorCode
{
    None = 0x00,
    InSolid = 0x01,
    Floating = 0x02,
    NoLink = 0x03,
    Duplicate = 0x04,
    NoStance = 0x05,
    InvalidDoor = 0x06,
    NoAngles = 0x07,
    BadPlacement = 0x08,
    Count = 0x09
}
