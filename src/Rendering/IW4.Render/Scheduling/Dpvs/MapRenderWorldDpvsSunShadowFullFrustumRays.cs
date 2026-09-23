using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Semantic form of the PS3 GfxSunShadowFrustumRays fields used by the full
/// sun-shadow clip producer.
/// </summary>
internal sealed class MapRenderWorldDpvsSunShadowFullFrustumRays
{
    private readonly Vector3[] _worldRays;
    private readonly Vector2[] _shadowRays;
    private readonly float[] _sinInteriorAngles;
    private readonly int[] _interiorArcRays;

    public MapRenderWorldDpvsSunShadowFullFrustumRays(
        IReadOnlyList<Vector3> worldRays,
        IReadOnlyList<Vector2> shadowRays,
        IReadOnlyList<float> sinInteriorAngles,
        float sinMin,
        float sinMax,
        Vector2 mins,
        Vector2 maxs,
        int boundingArcRay0,
        int boundingArcRay1,
        IReadOnlyList<int> interiorArcRays)
    {
        _worldRays = worldRays.ToArray();
        _shadowRays = shadowRays.ToArray();
        _sinInteriorAngles = sinInteriorAngles.ToArray();
        _interiorArcRays = interiorArcRays.ToArray();
        WorldRays = Array.AsReadOnly(_worldRays);
        ShadowRays = Array.AsReadOnly(_shadowRays);
        SinInteriorAngles = Array.AsReadOnly(_sinInteriorAngles);
        InteriorArcRays = Array.AsReadOnly(_interiorArcRays);
        SinMin = sinMin;
        SinMax = sinMax;
        Mins = mins;
        Maxs = maxs;
        BoundingArcRay0 = boundingArcRay0;
        BoundingArcRay1 = boundingArcRay1;
    }

    public IReadOnlyList<Vector3> WorldRays { get; }

    public IReadOnlyList<Vector2> ShadowRays { get; }

    public IReadOnlyList<float> SinInteriorAngles { get; }

    public float SinMin { get; }

    public float SinMax { get; }

    public Vector2 Mins { get; }

    public Vector2 Maxs { get; }

    public int BoundingArcRay0 { get; }

    public int BoundingArcRay1 { get; }

    public IReadOnlyList<int> InteriorArcRays { get; }
}

