using System.Numerics;
using IW4.Assets.Assets.XModel;

namespace IW4.Render.Geometry;

/// <summary>
/// Immutable per-draw-inst inputs needed by camera visibility and LOD
/// scheduling. Bounds describe the geometry currently prepared by the scene;
/// invalid source rows are deliberately absent rather than assigned synthetic
/// bounds.
/// </summary>
public sealed record MapRenderStaticModelSchedulingInfo(
    int ObjectIndex,
    Vector3 Origin,
    float PlacementScale,
    ushort CullDistance,
    XModelAsset Model,
    int PreparedLodIndex,
    MapRenderBounds Bounds)
{
    /// <summary>
    /// LODs for which every authored surface produced one complete selected
    /// pass group. The prepared LOD remains the fallback even
    /// when the current preview cannot prepare every one of its surfaces.
    /// </summary>
    public uint RenderableLodMask { get; init; }

    public bool IsLodRenderReady(int lodIndex) =>
        (uint)lodIndex < 32u &&
        (RenderableLodMask & (1u << lodIndex)) != 0;
}
