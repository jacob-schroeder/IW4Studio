namespace IW4.Game.Assets.GameMap;

/// <summary>Diagnostic index at node +0x30 when the HasError spawn flag is set.
/// PS3 SP 0x1B4E94 indexes the nine messages at 0x6D1F8C.</summary>
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
