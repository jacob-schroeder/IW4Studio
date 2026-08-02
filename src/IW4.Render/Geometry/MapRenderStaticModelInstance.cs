using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Geometry;

public readonly record struct MapRenderStaticModelInstance(
    Vector4 TransformRow0,
    Vector4 TransformRow1,
    Vector4 TransformRow2,
    int ObjectIndex,
    int SurfaceIndex,
    string Name,
    string AuthoredMaterialName,
    byte CameraRegion,
    int PrimaryLightIndex)
{
    /// <summary>
    /// Authored <see cref="GfxStaticModelDrawInst.ReflectionProbeIndex"/>.
    /// A translated static pass that requests custom sampler destination 1 is
    /// batched by this identity so one instanced draw never aliases two native
    /// reflection-probe resources.
    /// </summary>
    public byte ReflectionProbeIndex { get; init; }

    /// <summary>
    /// Lossless static-lighting lookup identity consumed by the immutable
    /// native model-lighting atlas builder.
    /// </summary>
    public MapRenderStaticModelLightingIdentity? AuthoredLightingIdentity
    {
        get;
        init;
    }

    /// <summary>
    /// Per-instance direct-code row 0x39 consumed by the native model-lighting
    /// vertex programs. The scene value is unassigned; the renderer packs the
    /// center of the object's current working-set tile before submission.
    /// </summary>
    public Vector4 BaseLightingCoords { get; init; }

    /// <summary>
    /// Per-instance direct-code row 0x3A consumed by native light-probe
    /// ambient vertex programs. The scene builder resolves this from the
    /// object's authored or light-grid-derived drawInst + 0x28 color.
    /// </summary>
    public Vector4 LightProbeAmbient { get; init; }
}
