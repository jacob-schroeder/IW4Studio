using IW4.Game.Assets.Material;

namespace IW4.Game.Assets.ColMap;

// Named masks from the PS3 SP/MP surface-parameter tables. The material category
// is an ordinal in bits 20..24; see MaterialSurfaceType. Unlisted bits are retained.
[Flags]
public enum CollisionSurfaceFlags : int
{
    None = 0,
    NoDamage = 0x00000001,
    Slick = 0x00000002,
    Sky = 0x00000004,
    Ladder = 0x00000008,
    NoImpact = 0x00000010,
    NoMarks = 0x00000020,
    NoDraw = 0x00000080,
    NoPenetrate = 0x00000100,
    Stairs = 0x00000200,
    NoLightmap = 0x00000400,
    Soft = 0x00001000,
    NoSteps = 0x00002000,
    NonSolid = 0x00004000,
    NoDlight = 0x00020000,
    NoCastShadow = 0x00040000,
    SurfaceTypeMask = 0x01f00000,
    MantleOn = 0x02000000,
    MantleOver = 0x04000000,
    Portal = unchecked((int)0x80000000)
}

public static class CollisionSurfaceFlagExtensions
{
    public static MaterialSurfaceType GetMaterialSurfaceType(this CollisionSurfaceFlags flags) =>
        (MaterialSurfaceType)((int)(flags & CollisionSurfaceFlags.SurfaceTypeMask) >> 20);
}
