namespace IW4.Assets.Assets.Material;

/// <summary>
/// Surface-family selector packed into <see cref="GfxDrawSurf"/>. Values are
/// the PS3 draw-method page indices and match the console
/// <c>surfaceType_t</c> domain.
/// </summary>
public enum GfxDrawSurfSurfaceType : byte
{
    Triangles = 0,
    TrianglesNoSunShadow = 1,
    StaticModelRigid = 2,
    StaticModelRigidNoSunShadow = 3,
    BrushModel = 4,
    XModelRigid = 5,
    XModelSkinned = 6,
    Code = 7,
    Glass = 8,
    Mark = 9,
    Spark = 10,
    ParticleCloud = 11,
    ParticleSparkCloud = 12,
    Count = 13
}
