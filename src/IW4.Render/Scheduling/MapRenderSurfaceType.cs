namespace IW4.Render.Scheduling;

/// <summary>
/// PS3 selector-page indices. Names follow the matching Xbox IW4
/// surface_type enum; numeric page ownership retains the PS3 values.
/// </summary>
public enum MapRenderSurfaceType : byte
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
    ParticleSparkCloud = 12
}
