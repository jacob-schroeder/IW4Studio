using System.Numerics;

using IW4.Render.Diagnostics;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;
using IW4.Render.Transforms;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

public sealed partial class MetalMapRenderer
{
    private const int AuxiliaryVertexStrideBytes = 6 * sizeof(float);

    private MetalAuxiliaryPipelines? _auxiliaryPipelines;
    private MetalSkyDraw[] _metalSkyDraws = [];
    private MetalDiagnosticDraw[] _metalDiagnosticDraws = [];
    private MetalGeometryResource? _metalWireframe;

    partial void CreateAuxiliarySceneResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);

        var skies = new MetalSkyDraw[snapshot.Skies.Length];
        var diagnostics = new MetalDiagnosticDraw[
            snapshot.Diagnostics.Length];
        MetalGeometryResource? wireframe = null;
        MetalAuxiliaryPipelines? pipelines = null;
        try
        {
            pipelines = new MetalAuxiliaryPipelines(
                _surface.Device,
                _depthStencilFormat);
            for (int index = 0; index < snapshot.Skies.Length; index++)
            {
                RenderSkySubmissionSnapshot submission =
                    snapshot.Skies[index];
                MetalGeometryResource geometry =
                    _auxiliaryResources.RequireGeometry(
                        submission.GeometryIdentity);
                RequireAuxiliaryGeometry(
                    geometry,
                    RenderPrimitiveTopology.TriangleList,
                    "sky");
                MetalTextureResource texture =
                    _auxiliaryResources.RequireTexture(
                        submission.TextureIdentity);
                if (texture.Descriptor.Dimension !=
                    RenderTextureDimension.TextureCube)
                {
                    throw new InvalidOperationException(
                        $"Sky source ordinal {submission.SceneOrdinal} " +
                        "does not own a cube texture.");
                }
                MetalSamplerResource sampler =
                    _auxiliaryResources.RequireSampler(
                        submission.SamplerIdentity);
                skies[index] = new MetalSkyDraw(
                    geometry,
                    texture,
                    sampler);
            }

            for (int index = 0;
                 index < snapshot.Diagnostics.Length;
                 index++)
            {
                RenderDiagnosticSubmissionSnapshot submission =
                    snapshot.Diagnostics[index];
                MetalGeometryResource geometry =
                    _auxiliaryResources.RequireGeometry(
                        submission.GeometryIdentity);
                RequireAuxiliaryGeometry(
                    geometry,
                    RenderPrimitiveTopology.TriangleList,
                    "diagnostic");
                MetalInstanceResource? instances =
                    submission.InstancesIdentity is { } identity
                        ? _auxiliaryResources.RequireInstances(identity)
                        : null;
                bool requiresInstances = submission.Kind ==
                    RenderDiagnosticSubmissionKind.InstancedSolid;
                if (requiresInstances != (instances is not null))
                {
                    throw new InvalidOperationException(
                        $"Diagnostic source ordinal {submission.SourceOrdinal} " +
                        "has inconsistent instance ownership.");
                }
                if (instances is not null &&
                    instances.StrideBytes !=
                        MapRenderStaticInstanceBufferPacker
                            .PlacementOnlyFloatStride * sizeof(float))
                {
                    throw new InvalidOperationException(
                        $"Diagnostic source ordinal {submission.SourceOrdinal} " +
                        "does not use the canonical placement-only instance layout.");
                }
                diagnostics[index] = new MetalDiagnosticDraw(
                    geometry,
                    instances);
            }

            if (snapshot.Wireframe is { } wireframeSubmission)
            {
                wireframe = _auxiliaryResources.RequireGeometry(
                    wireframeSubmission.GeometryIdentity);
                RequireAuxiliaryGeometry(
                    wireframe,
                    RenderPrimitiveTopology.LineList,
                    "wireframe");
            }

            _auxiliaryPipelines = pipelines;
            pipelines = null;
            _metalSkyDraws = skies;
            _metalDiagnosticDraws = diagnostics;
            _metalWireframe = wireframe;
        }
        finally
        {
            pipelines?.Dispose();
        }
    }

    partial void DeleteAuxiliarySceneResources()
    {
        ResetEditorSelectionOutline();
        _metalSkyDraws = [];
        _metalDiagnosticDraws = [];
        _metalWireframe = null;
        _auxiliaryPipelines?.Dispose();
        _auxiliaryPipelines = null;
    }

    partial void EncodeNormalCameraAuxiliaryPrelude(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera)
    {
        if (_auxiliaryPipelines is null)
            return;

        Matrix4x4 worldViewProjection =
            CreateAuxiliaryWorldViewProjection(camera);
        if (ShowSky &&
            _loadedIsolatedWorldSurfaceIndex is null &&
            _metalSkyDraws.Length != 0)
        {
            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.Sky))
                EncodeSkies(encoder, in worldViewProjection);
        }
        if (ShowDiagnosticGeometry &&
            _loadedIsolatedWorldSurfaceIndex is null &&
            _metalDiagnosticDraws.Length != 0)
        {
            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.EditorOverlay))
                EncodeDiagnostics(encoder, in worldViewProjection);
        }
    }

    partial void EncodeNormalCameraOverlays(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera)
    {
        Matrix4x4 worldViewProjection =
            CreateAuxiliaryWorldViewProjection(camera);
        if (ShowWireframe &&
            _loadedIsolatedWorldSurfaceIndex is null &&
            _auxiliaryPipelines is not null &&
            _metalWireframe is { } geometry)
        {
            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.EditorOverlay))
            using (_gpuPassTimer.BeginPhase(
                       encoder,
                       MapRenderGpuPhase.Wireframe))
            {
                encoder.SetRenderPipelineState(_auxiliaryPipelines.Wireframe);
                _renderStates.ApplyRasterState(
                    encoder,
                    MetalAuxiliaryPipelines.WireframeRenderState);
                SetWorldViewProjection(encoder, in worldViewProjection);
                encoder.SetVertexBuffer(geometry.Buffer, geometry.VertexOffset, 0);
                encoder.DrawIndexedPrimitives(
                    geometry.PrimitiveType,
                    checked((ulong)geometry.IndexCount),
                    geometry.IndexType,
                    geometry.Buffer,
                    geometry.IndexOffset);
            }

            _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls);
            _telemetry.AddCounter(MapRenderFrameCounter.LogicalDrawCommands);
            _telemetry.AddCounter(MapRenderFrameCounter.ProgramChanges);
            _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
            _telemetry.AddCounter(MapRenderFrameCounter.RenderStateChanges);
            _telemetry.AddCounter(MapRenderFrameCounter.UniformUpdates);
            _telemetry.AddCounter(MapRenderFrameCounter.Passes);
            _telemetry.AddGpuPhaseWork(
                MapRenderGpuPhase.Wireframe,
                drawCalls: 1,
                triangles: 0);
        }

        EncodeEditorSelectionOutline(
            ref encoder,
            in worldViewProjection);
    }

    private void EncodeSkies(
        MTLRenderCommandEncoder encoder,
        in Matrix4x4 worldViewProjection)
    {
        if (_metalSkyDraws.Length == 0)
            return;

        using var gpuTiming =
            _gpuPassTimer.BeginPhase(
                encoder,
                MapRenderGpuPhase.Sky);
        MetalAuxiliaryPipelines pipelines = _auxiliaryPipelines!;
        encoder.SetRenderPipelineState(pipelines.Sky);
        _renderStates.ApplyRasterState(
            encoder,
            MetalAuxiliaryPipelines.SkyRenderState);
        SetWorldViewProjection(encoder, in worldViewProjection);
        _telemetry.AddCounter(MapRenderFrameCounter.ProgramChanges);
        _telemetry.AddCounter(MapRenderFrameCounter.RenderStateChanges);
        _telemetry.AddCounter(MapRenderFrameCounter.UniformUpdates);

        long triangles = 0;
        foreach (MetalSkyDraw draw in _metalSkyDraws)
        {
            MetalGeometryResource geometry = draw.Geometry;
            encoder.SetVertexBuffer(
                geometry.Buffer,
                geometry.VertexOffset,
                0);
            encoder.SetFragmentTexture(
                draw.Texture.ResolveSampledTexture(
                    draw.Sampler.UsesSrgbReads),
                0);
            encoder.SetFragmentSamplerState(draw.Sampler.State, 0);
            encoder.DrawIndexedPrimitives(
                geometry.PrimitiveType,
                checked((ulong)geometry.IndexCount),
                geometry.IndexType,
                geometry.Buffer,
                geometry.IndexOffset);

            triangles = checked(triangles + geometry.IndexCount / 3);
            _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls);
            _telemetry.AddCounter(
                MapRenderFrameCounter.LogicalDrawCommands);
            _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
            _telemetry.AddCounter(MapRenderFrameCounter.TextureChanges);
            _telemetry.AddCounter(MapRenderFrameCounter.SamplerChanges);
        }
        _telemetry.AddCounter(MapRenderFrameCounter.Triangles, triangles);
        _telemetry.AddCounter(MapRenderFrameCounter.Passes);
        _telemetry.AddGpuPhaseWork(
            MapRenderGpuPhase.Sky,
            _metalSkyDraws.Length,
            triangles);
    }

    private void EncodeDiagnostics(
        MTLRenderCommandEncoder encoder,
        in Matrix4x4 worldViewProjection)
    {
        if (_metalDiagnosticDraws.Length == 0)
            return;

        using var gpuTiming =
            _gpuPassTimer.BeginPhase(
                encoder,
                MapRenderGpuPhase.Diagnostics);
        MetalAuxiliaryPipelines pipelines = _auxiliaryPipelines!;
        _renderStates.ApplyRasterState(
            encoder,
            MetalAuxiliaryPipelines.DiagnosticRenderState);
        SetWorldViewProjection(encoder, in worldViewProjection);
        _telemetry.AddCounter(MapRenderFrameCounter.RenderStateChanges);
        _telemetry.AddCounter(MapRenderFrameCounter.UniformUpdates);

        bool? instancedPipelineBound = null;
        long triangles = 0;
        foreach (MetalDiagnosticDraw draw in _metalDiagnosticDraws)
        {
            bool isInstanced = draw.Instances is not null;
            if (instancedPipelineBound != isInstanced)
            {
                encoder.SetRenderPipelineState(
                    isInstanced
                        ? pipelines.InstancedDiagnostic
                        : pipelines.Diagnostic);
                instancedPipelineBound = isInstanced;
                _telemetry.AddCounter(
                    MapRenderFrameCounter.ProgramChanges);
            }

            MetalGeometryResource geometry = draw.Geometry;
            encoder.SetVertexBuffer(
                geometry.Buffer,
                geometry.VertexOffset,
                0);
            ulong instanceCount = 1;
            if (draw.Instances is { } instances)
            {
                encoder.SetVertexBuffer(
                    instances.Buffer,
                    instances.Offset,
                    1);
                instanceCount = checked((ulong)instances.InstanceCount);
            }
            encoder.DrawIndexedPrimitives(
                geometry.PrimitiveType,
                checked((ulong)geometry.IndexCount),
                geometry.IndexType,
                geometry.Buffer,
                geometry.IndexOffset,
                instanceCount);

            triangles = checked(
                triangles +
                (long)(geometry.IndexCount / 3) *
                checked((long)instanceCount));
            _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls);
            _telemetry.AddCounter(
                MapRenderFrameCounter.LogicalDrawCommands);
            _telemetry.AddCounter(
                MapRenderFrameCounter.BufferChanges,
                isInstanced ? 2 : 1);
        }
        _telemetry.AddCounter(MapRenderFrameCounter.Triangles, triangles);
        _telemetry.AddCounter(MapRenderFrameCounter.Passes);
        _telemetry.AddGpuPhaseWork(
            MapRenderGpuPhase.Diagnostics,
            _metalDiagnosticDraws.Length,
            triangles);
    }

    private Matrix4x4 CreateAuxiliaryWorldViewProjection(
        RenderCamera camera)
    {
        float aspectRatio =
            (float)_surfaceExtents.SceneTarget.Width /
            _surfaceExtents.SceneTarget.Height;
        RenderNormalCameraMatrixCalculator.CalculatePs3Native(
            camera,
            aspectRatio,
            out _,
            out _,
            out Matrix4x4 viewProjection,
            out Vector3 eyeOffset);
        Matrix4x4 nativeWorldViewProjection =
            DerivedMatrixResolver.MultiplyWorldViewProjection0(
                DerivedMatrixResolver.CreateWorld0(eyeOffset),
                viewProjection);

        // Auxiliary geometry is frozen in render-space coordinates. Metal's
        // clip depth is already the RSX [0,w] range, so only the proven
        // render-to-game basis change is required; the OpenGL z remap and
        // lower-left origin compensation do not belong in this backend.
        return RenderCoordinateConverter.RenderToGameMatrix *
            nativeWorldViewProjection;
    }

    private static unsafe void SetWorldViewProjection(
        MTLRenderCommandEncoder encoder,
        in Matrix4x4 worldViewProjection)
    {
        Matrix4x4 value = worldViewProjection;
        encoder.SetVertexBytes(
            (nint)(&value),
            checked((ulong)sizeof(Matrix4x4)),
            2);
    }

    private static void RequireAuxiliaryGeometry(
        MetalGeometryResource geometry,
        RenderPrimitiveTopology topology,
        string stage)
    {
        RenderGeometryDescriptor descriptor = geometry.Descriptor;
        if (descriptor.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            descriptor.Topology != topology ||
            descriptor.VertexStrideBytes != AuxiliaryVertexStrideBytes)
        {
            throw new InvalidOperationException(
                $"Metal {stage} geometry does not match the canonical " +
                "render-space position/color resource contract.");
        }
    }

    private readonly record struct MetalSkyDraw(
        MetalGeometryResource Geometry,
        MetalTextureResource Texture,
        MetalSamplerResource Sampler);

    private readonly record struct MetalDiagnosticDraw(
        MetalGeometryResource Geometry,
        MetalInstanceResource? Instances);
}
