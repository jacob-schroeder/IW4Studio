namespace IW4.Game.Assets.GameMap;

/// <summary>Serialized PS3 node categories. The SP name table at 0x71516C has 20 entries.</summary>
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
    // Extra bit index in the runtime base-node mask.
    DontLink = Count
}
