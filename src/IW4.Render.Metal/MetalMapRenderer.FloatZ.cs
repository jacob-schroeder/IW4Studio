using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Metal.FloatZ;
using IW4.Render.Resources;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.FramePlans;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer
{
    private MetalNormalCameraFloatZBackend? _normalCameraFloatZ;
    private MetalProcessedFloatZFrame? _currentProcessedFloatZFrame;

    /// <summary>
    /// Uses the same visible, execution-ready group predicate as color
    /// submission. An inactive receiver alternative, culled object, or
    /// rejected authored group cannot allocate target 5/8 or split target 2.
    /// </summary>
    private bool RequiresVisibleProcessedFloatZ(RenderCamera camera)
    {
        if (!ShowTexturedGeometry || _drawOrder is null)
            return false;

        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> groups =
            _drawOrder.Order(
                camera.Position,
                camera.Forward);
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            MapRenderEditorDrawGroup<RenderNormalCameraDrawSubmissionSnapshot>
                group = groups[groupIndex];
            if (!_normalCameraAuthorizedGroups.Contains(group) ||
                !IsNormalCameraGroupSelected(group))
            {
                continue;
            }
            if (PrepareNormalCameraVisibleRuns(
                    group,
                    out _,
                    out _) == 0)
            {
                continue;
            }
            ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot> passes =
                group.AuthoredPassSpan;
            for (int passIndex = 0; passIndex < passes.Length; passIndex++)
            {
                if (HasRuntimeSampler(
                        _normalCameraPasses[passes[passIndex].PreparedPass]
                            .RuntimeSamplerBindings,
                        ShaderRuntimeSamplerResourceKind.ProcessedFloatZ))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void EncodeProcessedFloatZ(
        MTLCommandBuffer commandBuffer,
        RenderCamera camera)
    {
        if (_normalCameraWorldSource is null)
        {
            throw new InvalidOperationException(
                "Processed FloatZ requires the active canonical world source.");
        }
        _normalCameraFloatZ ??= new MetalNormalCameraFloatZBackend(
            _surface.Device,
            _normalCameraWorldSource);
        _normalCameraFloatZ.Resize(
            _surfaceExtents.SceneTarget.Width,
            _surfaceExtents.SceneTarget.Height);
        using MapRenderCpuPhaseScope cpuTiming =
            _telemetry.BeginCpuPhase(MapRenderCpuPhase.ProcessedFloatZ);
        _currentProcessedFloatZFrame = _normalCameraFloatZ.Encode(
            commandBuffer,
            _targets.SceneDepthStencil,
            _frameIndex,
            camera.NearPlane,
            _renderStates);
        // Raw target-2 view, target 5, then target 8.
        _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls, 3);
        _telemetry.AddCounter(MapRenderFrameCounter.LogicalDrawCommands, 3);
        _telemetry.AddCounter(MapRenderFrameCounter.Triangles, 3 * 2);
        _telemetry.AddCounter(MapRenderFrameCounter.Passes, 3);
        _telemetry.AddCounter(MapRenderFrameCounter.ProgramChanges, 3);
        _telemetry.AddCounter(MapRenderFrameCounter.TextureChanges, 3);
        _telemetry.AddCounter(MapRenderFrameCounter.SamplerChanges, 2);
        _telemetry.AddGpuPhaseWork(
            MapRenderGpuPhase.ProcessedFloatZ,
            drawCalls: 3,
            triangles: 6);
    }

    private void ResetProcessedFloatZFrame() =>
        _currentProcessedFloatZFrame = null;

    private void DeleteNormalCameraFloatZResources()
    {
        _currentProcessedFloatZFrame = null;
        _normalCameraFloatZ?.Dispose();
        _normalCameraFloatZ = null;
    }

    private void RequireCurrentProcessedFloatZBinding(
        out MTLTexture texture,
        out MTLSamplerState sampler)
    {
        MetalProcessedFloatZFrame publication =
            _currentProcessedFloatZFrame ??
            throw new InvalidOperationException(
                "A processed-FloatZ draw reached binding without the current target-8 publication.");
        if (publication.FrameRevision != _frameIndex)
        {
            throw new InvalidOperationException(
                "A processed-FloatZ draw reached binding with a stale target-8 publication.");
        }
        texture = publication.Texture;
        sampler = publication.Sampler;
    }
}
