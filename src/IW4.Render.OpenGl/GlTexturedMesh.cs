using IW4.Render.Techniques;
using System.Numerics;
using Silk.NET.OpenGL;

using IW4.Assets.Assets.Material;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;

namespace IW4.Render.OpenGl;

internal readonly record struct GlTexturedMesh(
    uint VertexArray,
    uint VertexBuffer,
    uint ElementBuffer,
    uint InstanceBuffer,
    uint[] ColorTextures,
    int[] BlendWeightComponents,
    uint LightmapTexture,
    uint[] NormalTextures,
    uint[] SpecularTextures,
    GlRsxProgram RsxProgram,
    GlRsxSamplerBinding[] RsxSamplerBindings,
    GlRsxConstantBinding[] RsxConstantBindings,
    uint IndexCount,
    uint InstanceCount,
    MapRenderEditorVegetationAnimationPlan? VegetationAnimation,
    float LocalMinimumHeight,
    float LocalHeightRange,
    bool ReceivesEditorLighting,
    RenderState State,
    nuint IndexOffsetBytes = 0,
    int BaseVertex = 0,
    bool OwnsGeometry = true,
    bool OwnsVertexArray = false,
    int WorldSurfaceIndex = -1,
    RenderBounds WorldBounds = default,
    int MultiDrawBatchGroupId = -1,
    int StaticModelLodIndex = -1)
{
    /// <summary>
    /// Retains the translated-program contract at draw time. Renderer-owned
    /// code samplers cannot be treated as statically ready: each publication
    /// contract must be satisfied before issuing this mesh.
    /// </summary>
    public ShaderExecutionContract? ShaderExecution { get; init; }

    /// <summary>
    /// Preserved selected-pass control word used by the RSX shader-packer
    /// suppression rule even when this mesh executes the generic fallback.
    /// </summary>
    public uint FragmentProgramControl { get; init; }

    /// <summary>
    /// Bit <c>n</c> is set only when the selected translated fragment program
    /// squares generic color input <c>n</c> before lighting.
    /// Unclassified inputs preserve their decoded texture values.
    /// </summary>
    public int ColorInputLinearizationMask { get; init; }

    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; init; }

    public GlRsxProgram DepthPrepassRsxProgram { get; init; }

    /// <summary>
    /// Map-only static-model bridge locations for the composed color program.
    /// </summary>
    public MapRenderOpenGlStaticModelProgramUniforms?
        StaticModelProgramUniforms { get; init; }

    /// <summary>
    /// Map-only static-model bridge locations for the composed depth program.
    /// </summary>
    public MapRenderOpenGlStaticModelProgramUniforms?
        DepthStaticModelProgramUniforms { get; init; }

    public GlRsxConstantBinding[] DepthPrepassRsxConstantBindings
    {
        get;
        init;
    } = [];

    /// <summary>
    /// Exact depth-only compatibility identity for single-pass world draws.
    /// It deliberately excludes color-program, texture, and receiver state
    /// that the standard transform-only depth owner never consumes.
    /// </summary>
    public int DepthMultiDrawBatchGroupId { get; init; } = -1;

    /// <summary>
    /// Static instance buffer width. Per-instance lighting consumers use 16
    /// floats with attribute-12 payload first, followed by three placement
    /// rows. Other generic preview buffers retain the compact 12-float layout.
    /// </summary>
    public int StaticInstanceFloatStride { get; init; } = 12;

    /// <summary>
    /// Semantic identity of attribute 12. Row 0x39 and row 0x3A have the same
    /// physical stride, but require different values during sparse compaction.
    /// </summary>
    internal MapRenderStaticInstanceLightingPayload
        StaticInstanceLightingPayload { get; init; }

    /// <summary>
    /// Uniform authored camera region for this immutable static instance
    /// buffer. A null value means the buffer is empty, mixed, or unresolved
    /// and therefore must retain the legacy fail-open camera path.
    /// </summary>
    public GfxCameraRegionType? StaticCameraRegion { get; init; }

    /// <summary>
    /// The generic fallback owns the selected program's row-0x39 /
    /// row-0x21 / modelLightingSampler contract for this static batch.
    /// </summary>
    public bool UsesGenericStaticModelLighting { get; init; }

    /// <summary>
    /// The selected static contract reads directional rows 0 and 1, and the
    /// batch is owned by the active directional scene-light identity.
    /// </summary>
    public bool GenericStaticModelLightingAddsDirectionalDiffuse { get; init; }

    /// <summary>
    /// The selected static contract reads directional rows 0 and 2, and the
    /// batch is owned by the active directional scene-light identity.
    /// </summary>
    public bool GenericStaticModelLightingAddsDirectionalSpecular { get; init; }
}
