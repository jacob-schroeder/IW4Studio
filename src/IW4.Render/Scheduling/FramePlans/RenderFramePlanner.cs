using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

using IW4.Render.Preview;
using IW4.Render.Resources;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.Scheduling.FramePlans;

/// <summary>
/// Pure planner for backend-neutral frame intent. Inputs contain no clock,
/// graphics context, device, or backend resource state.
/// </summary>
public static class RenderFramePlanner
{
    public static RenderAttachmentIdentity NormalCameraSceneColorAttachment
        { get; } = new("normal-camera.scene.color");

    public static RenderAttachmentIdentity
        NormalCameraSceneDepthStencilAttachment { get; } =
            new("normal-camera.scene.depth-stencil");

    private static readonly ImmutableArray<RenderAttachmentPixelFormat>
        NormalCameraColorAttachmentFormats =
            ImmutableArray.Create(
                RenderAttachmentPixelFormat.Rgba8Unorm);

    private static readonly RenderColorAttachmentPlan
        PreservingNormalCameraColorAttachment = new(
            NormalCameraSceneColorAttachment,
            RenderAttachmentLoadRequirement.Preserve,
            RenderAttachmentStoreRequirement.Preserve,
            clearValue: null);

    private static readonly ImmutableArray<RenderColorAttachmentPlan>
        PreservingNormalCameraColorAttachments =
            ImmutableArray.Create(
                PreservingNormalCameraColorAttachment);

    private static readonly RenderDepthStencilAttachmentPlan
        PreservingNormalCameraDepthStencilAttachment = new(
            NormalCameraSceneDepthStencilAttachment,
            RenderAttachmentLoadRequirement.Preserve,
            RenderAttachmentStoreRequirement.Preserve,
            clearDepth: null,
            RenderAttachmentLoadRequirement.Preserve,
            RenderAttachmentStoreRequirement.Preserve,
            clearStencil: null);

    public static RenderPassIdentity NormalCameraScenePass { get; } =
        new("normal-camera.scene");

    public static RenderPassIdentity NormalCameraSkyPass { get; } =
        new("normal-camera.sky");

    public static RenderPassIdentity NormalCameraDiagnosticsPass { get; } =
        new("normal-camera.diagnostics");

    public static RenderPassIdentity NormalCameraWorldOpaquePass { get; } =
        new("normal-camera.world-opaque");

    public static RenderPassIdentity NormalCameraWireframePass { get; } =
        new("normal-camera.wireframe");

    public static RenderSemanticIdentity WorldOpaquePipelineIdentity
        { get; } = new(
            RenderSemanticResourceKind.Pipeline,
            "builtin.normal-camera.world-opaque.pipeline.v1");

    public static RenderShaderProgramDescriptor WorldOpaqueShaderProgram =>
        RenderMaterialPreviewFramePlanFactory.ShaderProgram;

    public static RenderFixedStateDescriptor WorldOpaqueFixedState =>
        RenderMaterialPreviewFramePlanFactory.FixedState;

    public static RenderShaderBindingPoint
        LoadedCameraColorCompatibilityTextureBindingPoint { get; } =
            new(RenderShaderStage.Fragment, destination: 0);

    public static RenderShaderBindingPoint
        LoadedCameraColorCompatibilityWorldViewProjectionBindingPoint { get; } =
            new(RenderShaderStage.Vertex, destination: 0);

    public static RenderShaderAbiDescriptor
        LoadedCameraColorCompatibilityShaderAbi { get; } = new(
            new RenderShaderAbiIdentity(
                "compatibility.loaded-camera-color-base-texture.shader-abi.v1"),
            [
                new RenderShaderBindingRequirement(
                    LoadedCameraColorCompatibilityTextureBindingPoint,
                    RenderTextureDimension.Texture2D),
                new RenderShaderBindingRequirement(
                    LoadedCameraColorCompatibilityWorldViewProjectionBindingPoint,
                    RenderDynamicConstantEncoding.Matrix4x4Rows,
                    RenderShaderCoordinateSpace.Ps3Native,
                    expectedVectorCount: 4)
            ]);

    /// <summary>
    /// Executable compatibility shader for the bounded loaded CameraColor
    /// profile. It is deliberately distinct from both the retained authored
    /// RSX IR and the synthetic material-preview program.
    /// </summary>
    public static RenderShaderProgramDescriptor
        LoadedCameraColorCompatibilityShaderProgram { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "compatibility.loaded-camera-color-base-texture.shader-program.v1"),
            "compatibility.loaded-camera-color-base-texture.vertex.render-position-uv0-ps3-native-wvp.v1",
            "compatibility.loaded-camera-color-base-texture.fragment.sample-rgba-alpha-gequal128-linear-to-srgb.v1",
            LoadedCameraColorCompatibilityShaderAbi);

    public static RenderSemanticIdentity
        LoadedCameraColorCompatibilityPipelineIdentity { get; } = new(
            RenderSemanticResourceKind.Pipeline,
            "compatibility.normal-camera.loaded-camera-color-base-texture.pipeline.v1");

    public static RenderFixedStateDescriptor
        LoadedCameraColorCompatibilityFixedState { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.FixedState,
                "compatibility.loaded-camera-color-base-texture.alpha-gequal128-cull-front-depth-lequal.fixed-state.v1"),
            new RenderRasterStateDescriptor(
                RenderCullMode.Front,
                RenderFrontFace.CounterClockwise,
                RenderPolygonMode.Fill,
                RenderDepthBiasDescriptor.Disabled),
            new RenderDepthStateDescriptor(
                testEnabled: true,
                writeEnabled: true,
                RenderCompareOperation.LessOrEqual),
            RenderStencilStateDescriptor.Disabled,
            RenderBlendStateDescriptor.Disabled,
            RenderColorWriteMask.Rgba);

    public static RenderPassIdentity NormalCameraStaticCutoutPass { get; } =
        new("normal-camera.static-cutout");

    public static RenderShaderBindingPoint
        LoadedStaticModelCutoutCompatibilityTextureBindingPoint { get; } =
            new(RenderShaderStage.Fragment, destination: 0);

    public static RenderShaderBindingPoint
        LoadedStaticModelCutoutCompatibilityWorldViewProjectionBindingPoint
        { get; } = new(RenderShaderStage.Vertex, destination: 0);

    public static RenderShaderAbiDescriptor
        LoadedStaticModelCutoutCompatibilityShaderAbi { get; } = new(
            new RenderShaderAbiIdentity(
                "compatibility.loaded-static-model-cutout.shader-abi.v1"),
            [
                new RenderShaderBindingRequirement(
                    LoadedStaticModelCutoutCompatibilityTextureBindingPoint,
                    RenderTextureDimension.Texture2D),
                new RenderShaderBindingRequirement(
                    LoadedStaticModelCutoutCompatibilityWorldViewProjectionBindingPoint,
                    RenderDynamicConstantEncoding.Matrix4x4Rows,
                    RenderShaderCoordinateSpace.Ps3Native,
                    expectedVectorCount: 4)
            ]);

    public static RenderShaderProgramDescriptor
        LoadedStaticModelCutoutCompatibilityShaderProgram { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "compatibility.loaded-static-model-cutout.shader-program.v1"),
            "compatibility.loaded-static-model-cutout.vertex.position-uv0-instance-transform-ps3-native-wvp.v1",
            "compatibility.loaded-static-model-cutout.fragment.sample-rgba-alpha-gequal128-linear-to-srgb.v1",
            LoadedStaticModelCutoutCompatibilityShaderAbi);

    public static RenderSemanticIdentity
        LoadedStaticModelCutoutCompatibilityPipelineIdentity { get; } = new(
            RenderSemanticResourceKind.Pipeline,
            "compatibility.normal-camera.loaded-static-model-cutout.pipeline.v1");

    public static RenderFixedStateDescriptor
        LoadedStaticModelCutoutCompatibilityFixedState { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.FixedState,
                "compatibility.loaded-static-model-cutout.alpha-gequal128-cull-none-depth-lequal.fixed-state.v1"),
            new RenderRasterStateDescriptor(
                RenderCullMode.None,
                RenderFrontFace.CounterClockwise,
                RenderPolygonMode.Fill,
                RenderDepthBiasDescriptor.Disabled),
            new RenderDepthStateDescriptor(
                testEnabled: true,
                writeEnabled: true,
                RenderCompareOperation.LessOrEqual),
            RenderStencilStateDescriptor.Disabled,
            RenderBlendStateDescriptor.Disabled,
            RenderColorWriteMask.Rgba);

    public static RenderShaderBindingPoint SkyTextureBindingPoint { get; } =
        new(RenderShaderStage.Fragment, destination: 0);

    public static RenderShaderBindingPoint
        SkyWorldViewProjectionBindingPoint { get; } =
            new(RenderShaderStage.Vertex, destination: 0);

    public static RenderShaderBindingPoint
        DiagnosticsWorldViewProjectionBindingPoint =>
            SkyWorldViewProjectionBindingPoint;

    public static RenderShaderAbiDescriptor SkyShaderAbi { get; } = new(
        new RenderShaderAbiIdentity("builtin.sky.shader-abi.v1"),
        [
            new RenderShaderBindingRequirement(
                SkyTextureBindingPoint,
                RenderTextureDimension.TextureCube),
            new RenderShaderBindingRequirement(
                SkyWorldViewProjectionBindingPoint,
                RenderDynamicConstantEncoding.Matrix4x4Rows,
                RenderShaderCoordinateSpace.Ps3Native,
                expectedVectorCount: 4)
        ]);

    public static RenderShaderProgramDescriptor SkyShaderProgram { get; } =
        new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "builtin.sky.shader-program.v1"),
            "builtin.sky.vertex.ps3-native-wvp.v1",
            "builtin.sky.fragment.cubemap.v1",
            SkyShaderAbi);

    public static RenderMaterialDescriptor SkyMaterial { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.Material,
            "builtin.sky.material.v1"),
        "builtin.sky",
        "wc_sky",
        "normal-camera.sky",
        passIndex: 0);

    public static RenderSemanticIdentity SkyPipelineIdentity { get; } = new(
        RenderSemanticResourceKind.Pipeline,
        "builtin.sky.pipeline.v1");

    public static RenderSemanticIdentity
        SkyWorldViewProjectionConstantIdentity { get; } = new(
            RenderSemanticResourceKind.DynamicConstant,
            "frame.normal-camera.world-view-projection0.ps3-native");

    public static RenderShaderAbiDescriptor DiagnosticsShaderAbi { get; } =
        new(
            new RenderShaderAbiIdentity(
                "builtin.diagnostics.shader-abi.v1"),
            [
                new RenderShaderBindingRequirement(
                    DiagnosticsWorldViewProjectionBindingPoint,
                    RenderDynamicConstantEncoding.Matrix4x4Rows,
                    RenderShaderCoordinateSpace.Ps3Native,
                    expectedVectorCount: 4)
            ]);

    public static RenderShaderProgramDescriptor
        DiagnosticsSolidShaderProgram { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "builtin.diagnostics.solid.shader-program.v1"),
            "builtin.diagnostics.solid.vertex.v1",
            "builtin.diagnostics.solid.fragment.v1",
            DiagnosticsShaderAbi);

    public static RenderShaderProgramDescriptor
        DiagnosticsInstancedSolidShaderProgram { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "builtin.diagnostics.instanced-solid.shader-program.v1"),
            "builtin.diagnostics.instanced-solid.vertex.v1",
            "builtin.diagnostics.solid.fragment.v1",
            DiagnosticsShaderAbi);

    public static RenderMaterialDescriptor DiagnosticsMaterial { get; } =
        new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Material,
                "builtin.diagnostics.material.v1"),
            "builtin.diagnostics",
            "solid-color",
            "normal-camera.diagnostics",
            passIndex: 0);

    public static RenderSemanticIdentity
        DiagnosticsSolidPipelineIdentity { get; } = new(
            RenderSemanticResourceKind.Pipeline,
            "builtin.diagnostics.solid.pipeline.v1");

    public static RenderSemanticIdentity
        DiagnosticsInstancedSolidPipelineIdentity { get; } = new(
            RenderSemanticResourceKind.Pipeline,
            "builtin.diagnostics.instanced-solid.pipeline.v1");

    public static RenderSemanticIdentity
        DiagnosticsWorldViewProjectionConstantIdentity =>
            SkyWorldViewProjectionConstantIdentity;

    public static RenderShaderBindingPoint
        WireframeWorldViewProjectionBindingPoint =>
            SkyWorldViewProjectionBindingPoint;

    public static RenderSemanticIdentity
        WireframeWorldViewProjectionConstantIdentity =>
            SkyWorldViewProjectionConstantIdentity;

    public static RenderShaderAbiDescriptor WireframeShaderAbi { get; } =
        new(
            new RenderShaderAbiIdentity(
                "builtin.wireframe.shader-abi.v1"),
            [
                new RenderShaderBindingRequirement(
                    WireframeWorldViewProjectionBindingPoint,
                    RenderDynamicConstantEncoding.Matrix4x4Rows,
                    RenderShaderCoordinateSpace.Ps3Native,
                    expectedVectorCount: 4)
            ]);

    public static RenderShaderProgramDescriptor
        WireframeShaderProgram { get; } = new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.ShaderProgram,
                "builtin.wireframe.shader-program.v1"),
            "builtin.wireframe.vertex.position-color.ps3-native-wvp.v1",
            "builtin.wireframe.fragment.vertex-color.v1",
            WireframeShaderAbi);

    public static RenderMaterialDescriptor WireframeMaterial { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.Material,
            "builtin.wireframe.material.v1"),
        "builtin.wireframe",
        "solid-color",
        "normal-camera.wireframe",
        passIndex: 0);

    public static RenderSemanticIdentity WireframePipelineIdentity { get; } =
        new(
            RenderSemanticResourceKind.Pipeline,
            "builtin.wireframe.pipeline.v1");

    /// <summary>
    /// Plans the target-2 entry and clear. This is the first production
    /// vertical slice of the full frame plan; later passes append to the same
    /// immutable contract without moving resource ownership into core.
    /// </summary>
    public static RenderFramePlan CreateNormalCameraSceneTarget(
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents,
        MapRenderNormalCameraClearColorResult clearColor)
    {
        CreateNormalCameraSceneTargetComponents(
            frameRevision,
            surfaceExtents,
            clearColor,
            out ImmutableArray<RenderAttachmentDescriptor> attachments,
            out RenderPassPlan scenePass);

        return new RenderFramePlan(
            frameRevision,
            surfaceExtents,
            attachments,
            ImmutableArray.Create(scenePass),
            RenderPreviewRequirements.Presentation,
            RenderPickingRequirements.None);
    }

    private static void CreateNormalCameraSceneTargetComponents(
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents,
        MapRenderNormalCameraClearColorResult clearColor,
        out ImmutableArray<RenderAttachmentDescriptor> attachments,
        out RenderPassPlan scenePass)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (!surfaceExtents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(surfaceExtents));
        ArgumentNullException.ThrowIfNull(clearColor);

        MapRenderSceneTargetClearPlan clear =
            MapRenderEditorPreviewNormalCameraRecipe.Current.SceneTargetClear;
        MapRenderNormalCameraTargetPlan target =
            MapRenderEditorPreviewNormalCameraRecipe.Current.GetTarget(
                MapRenderNormalCameraTargetKind.Scene);
        if (clear.TargetId != target.TargetId ||
            clear.SurfaceMask !=
                (MapRenderSceneClearSurfaceMask.Rgba |
                 MapRenderSceneClearSurfaceMask.Depth |
                 MapRenderSceneClearSurfaceMask.Stencil) ||
            clear.Depth != 1f ||
            clear.Stencil != 0 ||
            target.Ps3SurfaceSampleCount != 2)
        {
            throw new InvalidOperationException(
                "The normal-camera target-2 clear contract changed.");
        }

        MapRenderPixelExtent extent = surfaceExtents.SceneTarget;
        var color = new RenderAttachmentDescriptor(
            NormalCameraSceneColorAttachment,
            RenderAttachmentRole.Color,
            extent,
            RenderAttachmentPixelFormat.Rgba8Unorm,
            target.Ps3SurfaceSampleCount);
        var depthStencil = new RenderAttachmentDescriptor(
            NormalCameraSceneDepthStencilAttachment,
            RenderAttachmentRole.DepthStencil,
            extent,
            RenderAttachmentPixelFormat.Depth24Stencil8,
            target.Ps3SurfaceSampleCount);
        scenePass = new RenderPassPlan(
            NormalCameraScenePass,
            RenderPassPurpose.NormalCameraScene,
            new RenderViewport(0, 0, extent.Width, extent.Height),
            new RenderScissor(
                0,
                0,
                Math.Min(extent.Width, 0x1000),
                Math.Min(extent.Height, 0x1000)),
            ImmutableArray.Create(
                new RenderColorAttachmentPlan(
                    color.Identity,
                    RenderAttachmentLoadRequirement.Clear,
                    RenderAttachmentStoreRequirement.Preserve,
                    new RenderColorClearValue(
                        clearColor.Red,
                        clearColor.Green,
                        clearColor.Blue,
                        clearColor.Alpha))),
            new RenderDepthStencilAttachmentPlan(
                depthStencil.Identity,
                RenderAttachmentLoadRequirement.Clear,
                RenderAttachmentStoreRequirement.Preserve,
                clear.Depth,
                RenderAttachmentLoadRequirement.Clear,
                RenderAttachmentStoreRequirement.Preserve,
                clear.Stencil),
            ImmutableArray<RenderDrawPlan>.Empty);
        attachments = ImmutableArray.Create(color, depthStencil);
    }

    /// <summary>
    /// Plans the normal-camera scene target plus the currently migrated sky,
    /// diagnostic, generic world-opaque, and wireframe passes. Camera
    /// constants remain in the unmodified
    /// PS3-native convention, while geometry records its render/game position
    /// basis so each backend owns the required basis and clip-space lowering.
    /// </summary>
    public static RenderFramePlan CreateNormalCameraFrame(
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents,
        MapRenderNormalCameraClearColorResult clearColor,
        RenderSceneSnapshot scene,
        MapRenderCamera camera,
        RenderPreviewSettings previewSettings)
    {
        ArgumentNullException.ThrowIfNull(scene);

        CreateNormalCameraSceneTargetComponents(
            frameRevision,
            surfaceExtents,
            clearColor,
            out ImmutableArray<RenderAttachmentDescriptor> attachments,
            out RenderPassPlan scenePass);
        bool admitWireframe =
            previewSettings.ShowWireframe &&
            !previewSettings.IsolatedWorldSurfaceIndex.HasValue &&
            scene.Wireframe is not null;
        int passCount = 1 +
            (previewSettings.ShowSky ? 1 : 0) +
            (previewSettings.ShowDiagnosticGeometry ? 1 : 0) +
            (previewSettings.ShowTexturedGeometry ? 1 : 0) +
            (admitWireframe ? 1 : 0);
        var passes = ImmutableArray.CreateBuilder<RenderPassPlan>(passCount);
        passes.Add(scenePass);
        RenderDynamicConstantBinding? worldViewProjection = null;
        if (previewSettings.ShowSky)
        {
            ImmutableArray<RenderDrawPlan> skyDraws =
                previewSettings.IsolatedWorldSurfaceIndex.HasValue ||
                scene.Skies.IsEmpty
                    ? ImmutableArray<RenderDrawPlan>.Empty
                    : CreateSkyDraws(
                        scene,
                        worldViewProjection ??=
                            CreateWorldViewProjection(
                                camera,
                                surfaceExtents.SceneTarget));
            passes.Add(CreatePreservingPass(
                NormalCameraSkyPass,
                RenderPassPurpose.Sky,
                scenePass,
                skyDraws));
        }
        if (previewSettings.ShowDiagnosticGeometry)
        {
            ImmutableArray<RenderDrawPlan> diagnosticDraws =
                previewSettings.IsolatedWorldSurfaceIndex.HasValue ||
                scene.Diagnostics.IsEmpty
                    ? ImmutableArray<RenderDrawPlan>.Empty
                    : CreateDiagnosticDraws(
                        scene,
                        worldViewProjection ??=
                            CreateWorldViewProjection(
                                camera,
                                surfaceExtents.SceneTarget));
            passes.Add(CreatePreservingPass(
                NormalCameraDiagnosticsPass,
                RenderPassPurpose.Diagnostics,
                scenePass,
                diagnosticDraws));
        }
        if (previewSettings.ShowTexturedGeometry)
        {
            RenderDynamicConstantBinding? plannedWorldViewProjection = null;
            ImmutableArray<RenderDrawPlan> worldDraws;
            if (scene.LoadedCameraColorWorldDrawPacketAdmission.Packet is
                { } loadedPacket)
            {
                worldDraws = CreateLoadedCameraColorCompatibilityDraw(
                    loadedPacket,
                    previewSettings.IsolatedWorldSurfaceIndex,
                    plannedWorldViewProjection ??=
                        worldViewProjection ??=
                            CreateWorldViewProjection(
                                camera,
                                surfaceExtents.SceneTarget));
            }
            else
            {
                RenderWorldSurfaceAdmission admission =
                    scene.WorldSurfaceAdmission;
                RenderMaterialPickRangeSnapshot? surface =
                    admission.SurfaceRange;
                bool isolationMatches =
                    !previewSettings.IsolatedWorldSurfaceIndex.HasValue ||
                    (surface is not null &&
                     surface.SurfaceIndex ==
                        previewSettings.IsolatedWorldSurfaceIndex.Value);
                worldDraws = admission.IsAdmitted && isolationMatches
                    ? CreateWorldOpaqueDraw(
                        admission,
                        plannedWorldViewProjection ??=
                            worldViewProjection ??=
                                CreateWorldViewProjection(
                                    camera,
                                    surfaceExtents.SceneTarget))
                    : ImmutableArray<RenderDrawPlan>.Empty;
            }
            passes.Add(CreatePreservingPass(
                NormalCameraWorldOpaquePass,
                RenderPassPurpose.WorldOpaque,
                scenePass,
                worldDraws));
        }
        if (admitWireframe)
        {
            passes.Add(CreatePreservingPass(
                NormalCameraWireframePass,
                RenderPassPurpose.Wireframe,
                scenePass,
                CreateWireframeDraw(
                    scene,
                    worldViewProjection ??=
                        CreateWorldViewProjection(
                            camera,
                            surfaceExtents.SceneTarget),
                    WireframePipelineIdentity,
                    RenderAttachmentPixelFormat.Rgba8Unorm,
                    RenderMultisampleStateDescriptor.Ps3Target2)));
        }

        return new RenderFramePlan(
            frameRevision,
            surfaceExtents,
            scene.Resources,
            attachments,
            passes.MoveToImmutable(),
            RenderPreviewRequirements.Presentation,
            RenderPickingRequirements.None,
            previewSettings.AnimationTimeSeconds);
    }

    private static ImmutableArray<RenderDrawPlan> CreateSkyDraws(
        RenderSceneSnapshot scene,
        RenderDynamicConstantBinding worldViewProjection)
    {
        var draws = ImmutableArray.CreateBuilder<RenderDrawPlan>(
            scene.Skies.Length);
        foreach (RenderSkySubmissionSnapshot sky in scene.Skies)
        {
            RenderGeometryDescriptor geometry =
                scene.Resources.RequireGeometry(sky.GeometryIdentity);
            var pipeline = new RenderPipelineDescriptor(
                SkyPipelineIdentity,
                SkyShaderProgram,
                sky.VertexLayoutIdentity,
                RenderFixedStatePresets.SkyV1,
                RenderPrimitiveTopology.TriangleList,
                NormalCameraColorAttachmentFormats,
                RenderAttachmentPixelFormat.Depth24Stencil8,
                RenderMultisampleStateDescriptor.Ps3Target2);
            draws.Add(new RenderDrawPlan(
                sky.DrawIdentity,
                pipeline,
                SkyMaterial,
                new RenderGeometrySlice(
                    sky.GeometryIdentity,
                    sky.VertexLayoutIdentity,
                    geometry.Topology,
                    firstVertex: 0,
                    geometry.VertexCount,
                    firstIndex: 0,
                    geometry.IndexCount,
                    geometry.IndexFormat),
                instances: null,
                ImmutableArray.Create(
                    new RenderTextureSamplerBinding(
                        SkyTextureBindingPoint,
                        sky.TextureIdentity,
                        sky.SamplerIdentity,
                        RenderTextureDimension.TextureCube)),
                ImmutableArray.Create(worldViewProjection),
                new RenderDrawRange(
                    firstIndex: 0,
                    geometry.IndexCount,
                    baseVertex: 0,
                    firstInstance: 0,
                    instanceCount: 1),
                new RenderDrawSortKey(
                    Primary: 0,
                    Secondary: 0,
                    SourceOrdinal: sky.SceneOrdinal),
                pickingIdentity: null,
                RenderPreviewDrawRequirement.VisibleInPreview |
                RenderPreviewDrawRequirement.EligibleForScreenshot));
        }

        return draws.MoveToImmutable();
    }

    private static ImmutableArray<RenderDrawPlan> CreateWorldOpaqueDraw(
        RenderWorldSurfaceAdmission admission,
        RenderDynamicConstantBinding worldViewProjection)
    {
        RenderMaterialDrawPacketSnapshot packet =
            admission.MaterialPacket ??
            throw new InvalidOperationException(
                "An admitted world surface has no material packet.");
        RenderMaterialPickRangeSnapshot range =
            admission.SurfaceRange ??
            throw new InvalidOperationException(
                "An admitted world surface has no exact surface range.");
        var pipeline = new RenderPipelineDescriptor(
            WorldOpaquePipelineIdentity,
            WorldOpaqueShaderProgram,
            packet.VertexLayoutIdentity,
            WorldOpaqueFixedState,
            RenderPrimitiveTopology.TriangleList,
            NormalCameraColorAttachmentFormats,
            RenderAttachmentPixelFormat.Depth24Stencil8,
            RenderMultisampleStateDescriptor.Ps3Target2);
        var material = new RenderMaterialDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Material,
                string.Concat(
                    "builtin.normal-camera.world-opaque.material.",
                    packet.SourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture))),
            packet.SourcePass.MaterialName,
            "generic-opaque-texture2d",
            "normal-camera.world-opaque",
            passIndex: 0);
        var draw = new RenderDrawPlan(
            packet.DrawIdentity,
            pipeline,
            material,
            new RenderGeometrySlice(
                packet.GeometryIdentity,
                packet.VertexLayoutIdentity,
                RenderPrimitiveTopology.TriangleList,
                firstVertex: 0,
                packet.Geometry.VertexCount,
                firstIndex: 0,
                packet.Geometry.IndexCount,
                RenderIndexFormat.Unsigned32),
            instances: null,
            ImmutableArray.Create(
                new RenderTextureSamplerBinding(
                    RenderMaterialPreviewFramePlanFactory.TextureBindingPoint,
                    packet.TextureIdentity,
                    packet.SamplerIdentity,
                    RenderTextureDimension.Texture2D)),
            ImmutableArray.Create(worldViewProjection),
            new RenderDrawRange(
                range.FirstIndex,
                range.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1),
            new RenderDrawSortKey(
                Primary: 0,
                Secondary: 0,
                SourceOrdinal: packet.SourceOrdinal),
            pickingIdentity: null,
            RenderPreviewDrawRequirement.VisibleInPreview |
            RenderPreviewDrawRequirement.EligibleForScreenshot |
            RenderPreviewDrawRequirement.EligibleForIsolation);
        return ImmutableArray.Create(draw);
    }

    private static ImmutableArray<RenderDrawPlan>
        CreateLoadedCameraColorCompatibilityDraw(
            RenderWorldDrawPacketSnapshot packet,
            int? isolatedWorldSurfaceIndex,
            RenderDynamicConstantBinding worldViewProjection)
    {
        ArgumentNullException.ThrowIfNull(packet);
        RenderDrawRange range;
        RenderSemanticIdentity drawIdentity;
        if (isolatedWorldSurfaceIndex is int isolatedSurface)
        {
            RenderMaterialPickRangeSnapshot[] matches = packet.SurfaceRanges
                .Where(candidate =>
                    candidate.SurfaceIndex == isolatedSurface)
                .ToArray();
            if (matches.Length != 1)
                return ImmutableArray<RenderDrawPlan>.Empty;

            RenderMaterialPickRangeSnapshot selected = matches[0];
            range = new RenderDrawRange(
                selected.FirstIndex,
                selected.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1);
            drawIdentity = new RenderSemanticIdentity(
                RenderSemanticResourceKind.Draw,
                string.Concat(
                    "scene.loaded-camera-color-world-draw-packet.",
                    packet.SourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture),
                    ".draw.surface.",
                    isolatedSurface.ToString(
                        "D8",
                        CultureInfo.InvariantCulture)));
        }
        else
        {
            range = new RenderDrawRange(
                firstIndex: 0,
                packet.Geometry.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1);
            drawIdentity = packet.FullBatchDrawIdentity;
        }

        var pipeline = new RenderPipelineDescriptor(
            LoadedCameraColorCompatibilityPipelineIdentity,
            LoadedCameraColorCompatibilityShaderProgram,
            packet.VertexLayoutIdentity,
            LoadedCameraColorCompatibilityFixedState,
            RenderPrimitiveTopology.TriangleList,
            NormalCameraColorAttachmentFormats,
            RenderAttachmentPixelFormat.Depth24Stencil8,
            RenderMultisampleStateDescriptor.Ps3Target2);
        var material = new RenderMaterialDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Material,
                string.Concat(
                    "compatibility.normal-camera.loaded-camera-color-base-texture.material.",
                    packet.SourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture))),
            packet.SourcePass.MaterialName,
            "loaded-camera-color-base-texture-alpha-gequal128-linear-to-srgb",
            "normal-camera.loaded-camera-color-compatibility-no-dpvs",
            passIndex: 0);
        var draw = new RenderDrawPlan(
            drawIdentity,
            pipeline,
            material,
            new RenderGeometrySlice(
                packet.GeometryIdentity,
                packet.VertexLayoutIdentity,
                RenderPrimitiveTopology.TriangleList,
                firstVertex: 0,
                packet.Geometry.VertexCount,
                firstIndex: 0,
                packet.Geometry.IndexCount,
                RenderIndexFormat.Unsigned32),
            instances: null,
            ImmutableArray.Create(
                new RenderTextureSamplerBinding(
                    LoadedCameraColorCompatibilityTextureBindingPoint,
                    packet.TextureIdentity,
                    packet.SamplerIdentity,
                    RenderTextureDimension.Texture2D)),
            ImmutableArray.Create(worldViewProjection),
            range,
            new RenderDrawSortKey(
                Primary: 0,
                Secondary: 0,
                SourceOrdinal: packet.SourceOrdinal),
            pickingIdentity: null,
            RenderPreviewDrawRequirement.VisibleInPreview |
            RenderPreviewDrawRequirement.EligibleForScreenshot |
            RenderPreviewDrawRequirement.EligibleForIsolation);
        return ImmutableArray.Create(draw);
    }

    private static ImmutableArray<RenderDrawPlan> CreateDiagnosticDraws(
        RenderSceneSnapshot scene,
        RenderDynamicConstantBinding worldViewProjection)
    {
        RenderDiagnosticSubmissionSnapshot? firstNonInstanced =
            scene.Diagnostics.FirstOrDefault(submission =>
                submission.Kind !=
                    RenderDiagnosticSubmissionKind.InstancedSolid);
        RenderDiagnosticSubmissionSnapshot? firstInstanced =
            scene.Diagnostics.FirstOrDefault(submission =>
                submission.Kind ==
                    RenderDiagnosticSubmissionKind.InstancedSolid);
        RenderPipelineDescriptor? nonInstancedPipeline =
            firstNonInstanced is null
                ? null
                : CreateDiagnosticPipeline(
                    DiagnosticsSolidPipelineIdentity,
                    DiagnosticsSolidShaderProgram,
                    firstNonInstanced.VertexLayoutIdentity,
                    instanceLayout: null);
        RenderPipelineDescriptor? instancedPipeline =
            firstInstanced is null
                ? null
                : CreateDiagnosticPipeline(
                    DiagnosticsInstancedSolidPipelineIdentity,
                    DiagnosticsInstancedSolidShaderProgram,
                    firstInstanced.VertexLayoutIdentity,
                    firstInstanced.InstanceLayoutIdentity ??
                        throw new InvalidOperationException(
                            "An instanced diagnostic submission has no instance layout."));
        ImmutableArray<RenderDynamicConstantBinding> constants =
            ImmutableArray.Create(worldViewProjection);
        var draws = ImmutableArray.CreateBuilder<RenderDrawPlan>(
            scene.Diagnostics.Length);
        foreach (RenderDiagnosticSubmissionSnapshot submission in
                 scene.Diagnostics)
        {
            RenderGeometryDescriptor geometry =
                scene.Resources.RequireGeometry(
                    submission.GeometryIdentity);
            RenderInstanceSlice? instanceSlice = null;
            RenderDrawRange range;
            RenderPipelineDescriptor pipeline;
            if (submission.Kind ==
                RenderDiagnosticSubmissionKind.InstancedSolid)
            {
                RenderSemanticIdentity instancesIdentity =
                    submission.InstancesIdentity ??
                    throw new InvalidOperationException(
                        "An instanced diagnostic submission has no instance resource.");
                RenderInstanceDescriptor instanceResource =
                    scene.Resources.RequireInstances(instancesIdentity);
                instanceSlice = new RenderInstanceSlice(
                    instancesIdentity,
                    firstInstance: 0,
                    instanceResource.InstanceCount);
                range = new RenderDrawRange(
                    firstIndex: 0,
                    geometry.IndexCount,
                    baseVertex: 0,
                    firstInstance: 0,
                    instanceResource.InstanceCount);
                pipeline = instancedPipeline ??
                    throw new InvalidOperationException(
                        "An instanced diagnostics pipeline was not created.");
            }
            else
            {
                range = new RenderDrawRange(
                    firstIndex: 0,
                    geometry.IndexCount,
                    baseVertex: 0,
                    firstInstance: 0,
                    instanceCount: 1);
                pipeline = nonInstancedPipeline ??
                    throw new InvalidOperationException(
                        "A non-instanced diagnostics pipeline was not created.");
            }

            draws.Add(new RenderDrawPlan(
                submission.DrawIdentity,
                pipeline,
                DiagnosticsMaterial,
                new RenderGeometrySlice(
                    submission.GeometryIdentity,
                    submission.VertexLayoutIdentity,
                    geometry.Topology,
                    firstVertex: 0,
                    geometry.VertexCount,
                    firstIndex: 0,
                    geometry.IndexCount,
                    geometry.IndexFormat),
                instanceSlice,
                ImmutableArray<RenderTextureSamplerBinding>.Empty,
                constants,
                range,
                new RenderDrawSortKey(
                    Primary: 0,
                    Secondary: 0,
                    SourceOrdinal: submission.SourceOrdinal),
                pickingIdentity: null,
                RenderPreviewDrawRequirement.VisibleInPreview |
                RenderPreviewDrawRequirement.EligibleForScreenshot));
        }

        return draws.MoveToImmutable();
    }

    private static RenderPipelineDescriptor CreateDiagnosticPipeline(
        RenderSemanticIdentity identity,
        RenderShaderProgramDescriptor shaderProgram,
        RenderSemanticIdentity vertexLayout,
        RenderSemanticIdentity? instanceLayout) =>
        new(
            identity,
            shaderProgram,
            vertexLayout,
            RenderFixedStatePresets.DiagnosticsV1,
            RenderPrimitiveTopology.TriangleList,
            NormalCameraColorAttachmentFormats,
            RenderAttachmentPixelFormat.Depth24Stencil8,
            RenderMultisampleStateDescriptor.Ps3Target2,
            instanceLayout);

    internal static ImmutableArray<RenderDrawPlan> CreateWireframeDraw(
        RenderSceneSnapshot scene,
        RenderDynamicConstantBinding worldViewProjection,
        RenderSemanticIdentity pipelineIdentity,
        RenderAttachmentPixelFormat colorAttachmentFormat,
        RenderMultisampleStateDescriptor multisample)
    {
        RenderWireframeSubmissionSnapshot submission =
            scene.Wireframe ??
            throw new InvalidOperationException(
                "A wireframe draw requires a wireframe scene submission.");
        RenderGeometryDescriptor geometry =
            scene.Resources.RequireGeometry(submission.GeometryIdentity);
        var pipeline = new RenderPipelineDescriptor(
            pipelineIdentity,
            WireframeShaderProgram,
            submission.VertexLayoutIdentity,
            RenderFixedStatePresets.WireframeV1,
            RenderPrimitiveTopology.LineList,
            ImmutableArray.Create(colorAttachmentFormat),
            RenderAttachmentPixelFormat.Depth24Stencil8,
            multisample);
        var draw = new RenderDrawPlan(
            submission.DrawIdentity,
            pipeline,
            WireframeMaterial,
            new RenderGeometrySlice(
                submission.GeometryIdentity,
                submission.VertexLayoutIdentity,
                geometry.Topology,
                firstVertex: 0,
                geometry.VertexCount,
                firstIndex: 0,
                geometry.IndexCount,
                geometry.IndexFormat),
            instances: null,
            ImmutableArray<RenderTextureSamplerBinding>.Empty,
            ImmutableArray.Create(worldViewProjection),
            new RenderDrawRange(
                firstIndex: 0,
                geometry.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1),
            new RenderDrawSortKey(
                Primary: 0,
                Secondary: 0,
                SourceOrdinal: 0),
            pickingIdentity: null,
            RenderPreviewDrawRequirement.VisibleInPreview |
            RenderPreviewDrawRequirement.EligibleForScreenshot);
        return ImmutableArray.Create(draw);
    }

    private static RenderPassPlan CreatePreservingPass(
        RenderPassIdentity identity,
        RenderPassPurpose purpose,
        RenderPassPlan scenePass,
        ImmutableArray<RenderDrawPlan> draws) =>
        new(
            identity,
            purpose,
            scenePass.Viewport,
            scenePass.Scissor,
            PreservingNormalCameraColorAttachments,
            PreservingNormalCameraDepthStencilAttachment,
            draws);

    internal static RenderDynamicConstantBinding CreateWorldViewProjection(
        MapRenderCamera camera,
        MapRenderPixelExtent sceneTargetExtent)
    {
        float aspectRatio =
            (float)sceneTargetExtent.Width / sceneTargetExtent.Height;
        MapRenderNormalCameraMatrixCalculator.CalculatePs3Native(
            camera,
            aspectRatio,
            out _,
            out _,
            out Matrix4x4 viewProjection,
            out Vector3 eyeOffset);
        Matrix4x4 nativeWorldViewProjection0 =
            MapRenderDerivedMatrixResolver.MultiplyWorldViewProjection0(
                MapRenderDerivedMatrixResolver.CreateWorld0(eyeOffset),
                viewProjection);
        ImmutableArray<Vector4> nativeRows = ImmutableArray.Create(
            new Vector4(
                nativeWorldViewProjection0.M11,
                nativeWorldViewProjection0.M12,
                nativeWorldViewProjection0.M13,
                nativeWorldViewProjection0.M14),
            new Vector4(
                nativeWorldViewProjection0.M21,
                nativeWorldViewProjection0.M22,
                nativeWorldViewProjection0.M23,
                nativeWorldViewProjection0.M24),
            new Vector4(
                nativeWorldViewProjection0.M31,
                nativeWorldViewProjection0.M32,
                nativeWorldViewProjection0.M33,
                nativeWorldViewProjection0.M34),
            new Vector4(
                nativeWorldViewProjection0.M41,
                nativeWorldViewProjection0.M42,
                nativeWorldViewProjection0.M43,
                nativeWorldViewProjection0.M44));
        return new RenderDynamicConstantBinding(
            SkyWorldViewProjectionConstantIdentity,
            SkyWorldViewProjectionBindingPoint,
            RenderDynamicConstantEncoding.Matrix4x4Rows,
            RenderShaderCoordinateSpace.Ps3Native,
            nativeRows);
    }
}
