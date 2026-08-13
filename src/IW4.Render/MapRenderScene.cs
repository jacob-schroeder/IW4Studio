using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Lighting;
using IW4.Render.Picking;
using IW4.Render.Execution.Fog;
using IW4.Render.SceneBuilding;

namespace IW4.Render;

public sealed record MapRenderScene(
    string Name,
    IReadOnlyList<MapRenderSky> Skies,
    float[] SolidVertices,
    uint[] SolidIndices,
    float[] FallbackSolidVertices,
    uint[] FallbackSolidIndices,
    IReadOnlyList<MapRenderTexturedBatch> TexturedBatches,
    IReadOnlyList<MapRenderInstancedSolidBatch> InstancedSolidBatches,
    IReadOnlyList<MapRenderInstancedTexturedBatch> InstancedTexturedBatches,
    float[] WireVertices,
    uint[] WireIndices,
    IReadOnlyList<MapRenderPickRange> SolidPickRanges,
    IReadOnlyList<MapRenderPickRange> FallbackSolidPickRanges,
    IReadOnlyList<MapRenderPickTriangle> CollisionPickTriangles,
    RenderBounds Bounds,
    RenderBounds CameraBounds,
    MapRenderWorldSceneSourceBuildResult WorldSourceBuildResult,
    MapRenderEditorPreviewLightingPlan? EditorPreviewLighting = null,
    MapRenderEditorPreviewAtmospherePlan? EditorPreviewAtmosphere = null,
    MapRenderActiveFogState? EditorPreviewActiveFog = null,
    MapRenderEditorPreviewCreateArtFogResolution?
        EditorPreviewCreateArtFog = null,
    MapRenderEditorPreviewVisionResolution?
        EditorPreviewVision = null,
    MapRenderEditorPreviewEffectivePostState?
        EditorPreviewEffectivePost = null)
{
    public MapRenderWorldSceneSourceBuildResult WorldSourceBuildResult
        { get; init; } = WorldSourceBuildResult ??
            throw new ArgumentNullException(nameof(WorldSourceBuildResult));

    public MapRenderWorldSceneSource? WorldSource =>
        WorldSourceBuildResult.Source;

    /// <summary>
    /// Exact immutable world-texture revision captured while this scene was
    /// constructed. The Studio scene cache uses it to reject a build if a
    /// runtime lightmap refresh overtook the captured bindings.
    /// </summary>
    public long WorldTextureRevisionAtConstruction { get; init; } = -1;

    /// <summary>
    /// Per-static-draw-inst bounds and native LOD inputs. This stays separate
    /// from material batches so camera scheduling can compact one instance
    /// set without rescanning or decoding authored placement rows.
    /// </summary>
    public IReadOnlyList<MapRenderStaticModelSchedulingInfo>
        StaticModelScheduling { get; init; } = [];

    /// <summary>
    /// Every render-prepared loaded static-model LOD. The
    /// InstancedTexturedBatches collection remains the first-loaded subset
    /// until the renderer opts into dynamic LOD selection.
    /// </summary>
    public IReadOnlyList<MapRenderInstancedTexturedBatch>
        StaticModelLodTexturedBatches { get; init; } = [];

    /// <summary>
    /// Complete authored normal-camera static-model technique groups for every
    /// render-prepared LOD. These immutable sidecars replace matching generic
    /// preview identities only after every pass in a group is executable.
    /// </summary>
    public IReadOnlyList<MapRenderInstancedTexturedBatch>
        ExactNormalCameraStaticModelTexturedBatches { get; init; } = [];

    /// <summary>
    /// Exact page/allocation slot variants retained by surface and static draw
    /// instance. This catalog contains no current-frame readiness inference.
    /// </summary>
    public MapRenderSceneTechniqueVariantCatalog? TechniqueVariants
        { get; init; }

    /// <summary>
    /// Immutable object-indexed PS3 model-lighting source tiles and their
    /// renderer-owned physical cache image.
    /// </summary>
    public MapRenderStaticModelLightingAtlas? StaticModelLightingAtlas
        { get; init; }

    /// <summary>
    /// Exact authored receiver submissions keyed independently by PS3 selector
    /// page and scene-light allocation. Empty channels are intentional and
    /// must never be replaced with another channel or a generic preview pass.
    /// </summary>
    public MapRenderSceneReceiverVariantCatalog? ReceiverVariants
        { get; init; }

    /// <summary>
    /// Prepared world receiver batches for the shadow-map-allocated selector
    /// column. They are a sidecar and are never submitted by the legacy
    /// all-clear batch path.
    /// </summary>
    public IReadOnlyList<MapRenderTexturedBatch>
        ShadowAllocatedWorldTexturedBatches { get; init; } = [];

    /// <summary>
    /// Prepared static-model receiver batches for the shadow-map-allocated
    /// selector column. Instance ObjectIndex values retain runtime ownership.
    /// </summary>
    public IReadOnlyList<MapRenderInstancedTexturedBatch>
        ShadowAllocatedStaticModelTexturedBatches { get; init; } = [];

    /// <summary>
    /// Exact native slot-2 world caster submissions. The scene retains every
    /// materialized surface; current-frame DPVS and the cached
    /// surfaceCastsSunShadow mask perform admission later.
    /// </summary>
    public IReadOnlyList<MapRenderWorldSunShadowCasterBatch>
        SunShadowWorldCasterBatches { get; init; } = [];

    /// <summary>
    /// Typed non-executable world caster rows. A literal-null slot 2 is a
    /// native selector rejection and is counted rather than substituted;
    /// every other row remains fatal when admitted by the current frame.
    /// </summary>
    public IReadOnlyList<MapRenderSunShadowWorldCasterRejection>
        SunShadowWorldCasterRejections { get; init; } = [];

    /// <summary>
    /// Exact native slot-2 static caster submissions for every canonical
    /// loaded LOD. ObjectIndex, CullDist, placement, and the exact 0xC0 material
    /// route bits remain available to the runtime selector.
    /// </summary>
    public IReadOnlyList<MapRenderStaticSunShadowCasterBatch>
        SunShadowStaticCasterBatches { get; init; } = [];

    /// <summary>
    /// Native-eligible static caster surfaces retained before materialization.
    /// The backend compares these expectations with executable batches before
    /// publishing a shadow atlas.
    /// </summary>
    public IReadOnlyList<MapRenderSunShadowStaticCasterExpectation>
        SunShadowStaticCasterExpectations { get; init; } = [];

    public const int VertexFloatCount = 6;
    public const int MaxColorLayerCount = 5;
    public const int TexturedPositionFloatCount = 3;
    public const int TexturedUvFloatCount = 2;
    public const int TexturedBlendWeightFloatCount = 4;
    public const int TexturedBlendWeightOffset = TexturedPositionFloatCount + MaxColorLayerCount * TexturedUvFloatCount;
    public const int TexturedLightmapUvOffset = TexturedBlendWeightOffset + TexturedBlendWeightFloatCount;
    public const int TexturedNormalFloatCount = 3;
    public const int TexturedNormalOffset = TexturedLightmapUvOffset + TexturedUvFloatCount;
    public const int TexturedVertexFloatCount = TexturedNormalOffset + TexturedNormalFloatCount;
}
