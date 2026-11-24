namespace IW4.Game.Assets.Physics;

/// <summary>Primitive codes observed in serialized PS3 PhysGeomInfo rows. A brush wrapper takes precedence.</summary>
public enum PhysGeomPrimitive : int
{
    None = 0,
    Box = 1,
    Cylinder = 5
}
