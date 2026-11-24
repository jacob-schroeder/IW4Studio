namespace IW4.Game.Assets.GameMap;

/// <summary>Runtime base-node mask. PS3 SP 0x1B1F78 sets the category bit
/// and adds bit 20 for a node with the DontLink spawn flag.</summary>
[Flags]
public enum PathNodeTypeMask : uint
{
    None = 0,
    Error = 1u << (int)PathNodeType.Error,
    PathNode = 1u << (int)PathNodeType.PathNode,
    CoverStand = 1u << (int)PathNodeType.CoverStand,
    CoverCrouch = 1u << (int)PathNodeType.CoverCrouch,
    CoverCrouchWindow = 1u << (int)PathNodeType.CoverCrouchWindow,
    CoverProne = 1u << (int)PathNodeType.CoverProne,
    CoverRight = 1u << (int)PathNodeType.CoverRight,
    CoverLeft = 1u << (int)PathNodeType.CoverLeft,
    Ambush = 1u << (int)PathNodeType.Ambush,
    Exposed = 1u << (int)PathNodeType.Exposed,
    ConcealmentStand = 1u << (int)PathNodeType.ConcealmentStand,
    ConcealmentCrouch = 1u << (int)PathNodeType.ConcealmentCrouch,
    ConcealmentProne = 1u << (int)PathNodeType.ConcealmentProne,
    Door = 1u << (int)PathNodeType.Door,
    DoorInterior = 1u << (int)PathNodeType.DoorInterior,
    Scripted = 1u << (int)PathNodeType.Scripted,
    NegotiationBegin = 1u << (int)PathNodeType.NegotiationBegin,
    NegotiationEnd = 1u << (int)PathNodeType.NegotiationEnd,
    Turret = 1u << (int)PathNodeType.Turret,
    Guard = 1u << (int)PathNodeType.Guard,
    DontLink = 1u << (int)PathNodeType.DontLink
}
