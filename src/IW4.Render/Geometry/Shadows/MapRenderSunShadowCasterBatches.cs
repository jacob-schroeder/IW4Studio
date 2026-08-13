using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.XModel;
using IW4.Render.Materials;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Textures;

namespace IW4.Render.Geometry.Shadows;

/// <summary>
/// Host-ready caster vertices. Opaque rows contain XYZ. Cutout rows contain
/// XYZRGBAUV: route 0x01 RGBA when consumed (otherwise one), followed by the
/// exact native route 0x02 UV.
/// </summary>
public sealed class MapRenderSunShadowCasterGeometry
{
    public const int OpaqueVertexFloatCount = 3;
    public const int CutoutVertexFloatCount = 9;
    public const int CutoutColorOffset = 3;
    public const int CutoutUvOffset = 7;

    private readonly float[] _vertices;
    private readonly uint[] _indices;

    internal MapRenderSunShadowCasterGeometry(
        bool hasCutoutUv,
        bool hasVertexColor,
        IReadOnlyList<float> vertices,
        IReadOnlyList<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        HasCutoutUv = hasCutoutUv;
        HasVertexColor = hasVertexColor;
        if (hasVertexColor && !hasCutoutUv)
        {
            throw new ArgumentException(
                "A caster vertex-color route is valid only for an alpha-tested cutout payload.",
                nameof(hasVertexColor));
        }
        VertexFloatCount = hasCutoutUv
            ? CutoutVertexFloatCount
            : OpaqueVertexFloatCount;
        if (vertices.Count == 0 ||
            vertices.Count % VertexFloatCount != 0)
        {
            throw new ArgumentException(
                "Caster vertex data does not match its exact XYZ/XYZRGBAUV layout.",
                nameof(vertices));
        }
        if (indices.Count == 0 || indices.Count % 3 != 0)
        {
            throw new ArgumentException(
                "Caster index data must contain complete triangles.",
                nameof(indices));
        }

        _vertices = vertices.ToArray();
        _indices = indices.ToArray();
        int vertexCount = _vertices.Length / VertexFloatCount;
        if (_indices.Any(index => index >= vertexCount))
        {
            throw new ArgumentException(
                "Caster indices address a vertex outside the retained payload.",
                nameof(indices));
        }
        if (_vertices.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "Caster geometry contains a non-finite component.",
                nameof(vertices));
        }

        Vertices = Array.AsReadOnly(_vertices);
        Indices = Array.AsReadOnly(_indices);
    }

    public bool HasCutoutUv { get; }

    public bool HasVertexColor { get; }

    public int VertexFloatCount { get; }

    public int VertexCount => _vertices.Length / VertexFloatCount;

    public int TriangleCount => _indices.Length / 3;

    public IReadOnlyList<float> Vertices { get; }

    public IReadOnlyList<uint> Indices { get; }
}

public enum MapRenderSunShadowWorldCasterRejectionKind : byte
{
    NativeNullTechniqueSlot = 0,
    MaterialUnavailable = 1,
    MaterialContractUnavailable = 2,
    PayloadUnavailable = 3
}

/// <summary>
/// One world surface for which scene construction emitted no caster payload.
/// Literal-null slot 2 is rejected by the native selector; every other
/// kind remains a backend coverage failure if DPVS admits the surface.
/// </summary>
public sealed record MapRenderSunShadowWorldCasterRejection(
    int SurfaceIndex,
    MapRenderSunShadowWorldCasterRejectionKind Kind,
    MapRenderSunShadowCasterMaterialFailure? MaterialFailure,
    string Detail)
{
    public bool IsNativeSelectorRejection =>
        Kind == MapRenderSunShadowWorldCasterRejectionKind
            .NativeNullTechniqueSlot;
}

/// <summary>
/// One exact slot-2 world caster submission. Admission remains a frame-time
/// DPVS/surfaceCastsSunShadow decision, so scene construction retains every
/// successfully materialized surface independently.
/// </summary>
public sealed class MapRenderWorldSunShadowCasterBatch
{
    internal MapRenderWorldSunShadowCasterBatch(
        int surfaceIndex,
        GfxSurface surface,
        MapRenderSunShadowCasterMaterialPlan material,
        MapRenderSunShadowCasterGeometry geometry,
        UvRoute? cutoutUvRoute,
        Texture? cutoutTexture)
    {
        if (surfaceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(geometry);

        ValidateMaterialPayload(
            material,
            geometry,
            cutoutUvRoute,
            cutoutTexture);
        SurfaceIndex = surfaceIndex;
        Surface = surface;
        Material = material;
        Geometry = geometry;
        CutoutUvRoute = cutoutUvRoute;
        CutoutTexture = cutoutTexture;
    }

    public int SurfaceIndex { get; }

    public GfxSurface Surface { get; }

    public MapRenderSunShadowCasterMaterialPlan Material { get; }

    public MapRenderSunShadowCasterGeometry Geometry { get; }

    public UvRoute? CutoutUvRoute { get; }

    public Texture? CutoutTexture { get; }

    internal static void ValidateMaterialPayload(
        MapRenderSunShadowCasterMaterialPlan material,
        MapRenderSunShadowCasterGeometry geometry,
        UvRoute? cutoutUvRoute,
        Texture? cutoutTexture)
    {
        bool cutout = material.Kind ==
            MapRenderSunShadowCasterMaterialKind.Cutout;
        bool usesVertexColor = material is
            MapRenderSunShadowCutoutCasterMaterialPlan
            {
                UsesVertexColor: true
            };
        if (geometry.HasCutoutUv != cutout ||
            geometry.HasVertexColor != usesVertexColor ||
            (cutoutUvRoute is not null) != cutout ||
            (cutoutTexture is not null) != cutout)
        {
            throw new ArgumentException(
                "Opaque caster batches require XYZ only; cutout caster batches require exact route-01/default-white RGBA, route-02 UV, and texture payloads.");
        }
        if (cutout &&
            cutoutUvRoute!.TexCoordSource !=
                MapRenderSunShadowCasterMaterialPlanner
                    .CutoutEngineRouteSource)
        {
            throw new ArgumentException(
                "A cutout caster UV payload must retain native engine route 0x02.",
                nameof(cutoutUvRoute));
        }
        if (cutout &&
            material is MapRenderSunShadowCutoutCasterMaterialPlan cutoutPlan &&
            cutoutTexture!.SamplerState !=
                (byte)cutoutPlan.Sampler.RawSamplerState)
        {
            throw new ArgumentException(
                "The decoded cutout texture lost its authored sampler state.",
                nameof(cutoutTexture));
        }
    }
}

/// <summary>
/// Per-draw-instance inputs retained in game coordinates for the native
/// CullDist and camera-distance LOD selector, plus the render-space instance
/// transform consumed by a host backend.
/// </summary>
public sealed record MapRenderSunShadowStaticCasterInstance(
    MapRenderStaticModelInstance Instance,
    Vector3 GameOrigin,
    float PlacementScale,
    ushort CullDistance)
{
    public int ObjectIndex => Instance.ObjectIndex;
}

/// <summary>
/// One native-eligible static caster surface before slot-2 material and
/// geometry materialization. Keeping this expectation separate from emitted
/// batches makes a missing backend mapping observable instead of silently
/// turning the surface into a non-caster.
/// </summary>
public sealed record MapRenderSunShadowStaticCasterExpectation(
    int ObjectIndex,
    int LodIndex,
    int MaterialSurfaceIndex,
    Vector3 GameOrigin,
    float PlacementScale,
    ushort CullDistance,
    MapRenderSunShadowStaticMaterialEligibility MaterialEligibility);

/// <summary>
/// One exact slot-2 static caster geometry/material at one canonical loaded
/// XModel LOD. All owning draw instances are retained; current-frame DPVS,
/// CullDist, and LOD selection happen after scene construction.
/// </summary>
public sealed class MapRenderStaticSunShadowCasterBatch
{
    private readonly MapRenderSunShadowStaticCasterInstance[] _instances;

    internal MapRenderStaticSunShadowCasterBatch(
        XModelAsset model,
        XModelLodInfo lod,
        int lodIndex,
        XSurface surface,
        int surfaceOffset,
        int materialSurfaceIndex,
        MapRenderSunShadowStaticMaterialEligibility materialEligibility,
        MapRenderSunShadowCasterMaterialPlan material,
        MapRenderSunShadowCasterGeometry geometry,
        UvRoute? cutoutUvRoute,
        Texture? cutoutTexture,
        IReadOnlyList<MapRenderSunShadowStaticCasterInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(lod);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(instances);
        if (lodIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(lodIndex));
        if (surfaceOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceOffset));
        if (materialSurfaceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(materialSurfaceIndex));
        if (!materialEligibility.IsEligible)
        {
            throw new ArgumentException(
                "Static caster batches require nonzero MaterialInfo.gameFlags & 0xC0.",
                nameof(materialEligibility));
        }
        if (instances.Count == 0)
            throw new ArgumentException("A static caster batch requires an owning draw instance.", nameof(instances));

        MapRenderWorldSunShadowCasterBatch.ValidateMaterialPayload(
            material,
            geometry,
            cutoutUvRoute,
            cutoutTexture);
        Model = model;
        Lod = lod;
        LodIndex = lodIndex;
        Surface = surface;
        SurfaceOffset = surfaceOffset;
        MaterialSurfaceIndex = materialSurfaceIndex;
        MaterialEligibility = materialEligibility;
        Material = material;
        Geometry = geometry;
        CutoutUvRoute = cutoutUvRoute;
        CutoutTexture = cutoutTexture;
        _instances = instances.ToArray();
        Instances = Array.AsReadOnly(_instances);
    }

    public XModelAsset Model { get; }

    public XModelLodInfo Lod { get; }

    public int LodIndex { get; }

    public XSurface Surface { get; }

    public int SurfaceOffset { get; }

    public int MaterialSurfaceIndex { get; }

    public MapRenderSunShadowStaticMaterialEligibility MaterialEligibility
        { get; }

    public MapRenderSunShadowCasterMaterialPlan Material { get; }

    public MapRenderSunShadowCasterGeometry Geometry { get; }

    public UvRoute? CutoutUvRoute { get; }

    public Texture? CutoutTexture { get; }

    public IReadOnlyList<MapRenderSunShadowStaticCasterInstance> Instances
        { get; }
}
