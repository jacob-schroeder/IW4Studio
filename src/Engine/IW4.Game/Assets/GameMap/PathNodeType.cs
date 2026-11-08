namespace IW4.Game.Assets.GameMap;

/// <summary>
/// Path-node categories correlated from the PC IW4 definitions. PS3 category
/// semantics have not been independently confirmed.
/// </summary>
/// <remarks>
/// Source: OpenAssetTools commit 30d2d498bd8e3f2401037b24f3db6f96d39554c4,
/// src/Common/Game/IW4/IW4_Assets.h, nodeType.
/// </remarks>
public enum PathNodeType
{
    Error = 0x00,
    PathNode = 0x01,
    CoverStand = 0x02,
    CoverCrouch = 0x03,
    CoverCrouchWindow = 0x04,
    CoverProne = 0x05,
    CoverRight = 0x06,
    CoverLeft = 0x07,
    Ambush = 0x08,
    Exposed = 0x09,
    ConcealmentStand = 0x0A,
    ConcealmentCrouch = 0x0B,
    ConcealmentProne = 0x0C,
    Door = 0x0D,
    DoorInterior = 0x0E,
    Scripted = 0x0F,
    NegotiationBegin = 0x10,
    NegotiationEnd = 0x11,
    Turret = 0x12,
    Guard = 0x13,
    Count = 0x14,
    DontLink = 0x14
}
