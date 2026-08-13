using IW4.Render.Techniques;
using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.EditorPreview;
using IW4.Render.Textures;

namespace IW4.Render.Geometry;

public sealed record MapRenderInstancedTexturedBatch(
    MaterialPassIdentity Pass,
    MaterialSamplerIdentity PrimarySampler,
    Texture Texture,
    IReadOnlyList<MaterialColorLayer> ColorLayers,
    IReadOnlyList<MapRenderWorldMaterialSamplerBinding> MaterialSamplers,
    ShaderExecutionContract ShaderExecution,
    UvRoute UvRoute,
    RenderState State,
    int UnresolvedCodeSamplerCount,
    float[] Vertices,
    uint[] Indices,
    IReadOnlyList<MapRenderStaticModelInstance> Instances,
    int EditorDrawGroupId = -1,
    MapRenderEditorVegetationAnimationPlan? EditorVegetationAnimation = null,
    int LodIndex = -1)
{
    /// <summary>
    /// Exact primary-light identity shared by every instance in this batch.
    /// Static batch keys split on this value before translated execution.
    /// </summary>
    public byte SceneLightIndex { get; init; }

    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; init; }

    /// <summary>
    /// One 16-vec4 RSX input slab per emitted static-XSurface vertex. The
    /// payload is decoded from the native Verts0/Verts1 row-2 bindings and is
    /// intentionally separate from the host preview vertex layout.
    /// </summary>
    public float[] RsxVertexInputs { get; init; } = [];

    public ShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
        init;
    }

    /// <summary>
    /// True when exact authored resource buckets were compacted because this
    /// batch is consumed only by the generic preview program. Such a batch
    /// must never be authorized for translated authored execution; the exact
    /// normal-camera or receiver-variant sidecar owns that path.
    /// </summary>
    public bool IsGenericPreviewOnly { get; init; }
}
