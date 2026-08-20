using System.Numerics;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Metal.Pipelines;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

public sealed partial class MetalMapRenderer
{
    private const int SelectionOutlineVertexFloatCount = 6;

    private MapRenderEditorSelectionOutline? _editorSelectionOutline;

    /// <summary>
    /// Replaces the renderer-local editor selection visual. The projection is
    /// drawn into target 2 with depth testing and without depth writes, before
    /// the scene is resolved and postprocessed.
    /// </summary>
    public void SetEditorSelectionOutline(
        MapRenderEditorSelectionOutline? outline)
    {
        ThrowIfDisposed();
        if (!_loaded)
        {
            throw new InvalidOperationException(
                "A map scene must be loaded before setting an editor selection outline.");
        }
        if (outline is { IsValid: false })
        {
            throw new ArgumentException(
                "The editor selection outline must be a valid render-space projection.",
                nameof(outline));
        }

        _editorSelectionOutline = outline;
    }

    private unsafe void EncodeEditorSelectionOutline(
        ref MTLRenderCommandEncoder encoder,
        MetalSceneRenderPassTimingSplit? timingSplit,
        in Matrix4x4 worldViewProjection)
    {
        if (_editorSelectionOutline is not { } outline)
            return;
        MetalAuxiliaryPipelines pipelines = _auxiliaryPipelines ??
            throw new InvalidOperationException(
                "The Metal editor selection pipeline is unavailable.");

        timingSplit?.Transition(
            ref encoder,
            MapRenderGpuPhase.EditorOverlay);

        Span<Vector3> corners = stackalloc Vector3[
            MapRenderEditorSelectionOutlineGeometry.CornerCount];
        MapRenderEditorSelectionOutlineGeometry.WriteCorners(
            outline,
            corners);
        ReadOnlySpan<uint> lineIndices =
            MapRenderEditorSelectionOutlineGeometry.LineIndices;
        Span<float> vertices = stackalloc float[
            MapRenderEditorSelectionOutlineGeometry.EdgeIndexCount *
            SelectionOutlineVertexFloatCount];
        for (int index = 0; index < lineIndices.Length; index++)
        {
            int offset = index * SelectionOutlineVertexFloatCount;
            Vector3 position = corners[checked((int)lineIndices[index])];
            vertices[offset] = position.X;
            vertices[offset + 1] = position.Y;
            vertices[offset + 2] = position.Z;
            vertices[offset + 3] = outline.Color.X;
            vertices[offset + 4] = outline.Color.Y;
            vertices[offset + 5] = outline.Color.Z;
        }

        using var gpuTiming = _gpuPassTimer.BeginPhase(
            encoder,
            MapRenderGpuPhase.EditorOverlay);
        using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.EditorOverlay))
        fixed (float* vertexPointer = vertices)
        {
            encoder.SetRenderPipelineState(pipelines.SelectionOutline);
            _renderStates.ApplyRasterState(
                encoder,
                MetalAuxiliaryPipelines.SelectionOutlineRenderState);
            encoder.SetVertexBytes(
                (nint)vertexPointer,
                checked((ulong)(vertices.Length * sizeof(float))),
                0);
            SetWorldViewProjection(encoder, in worldViewProjection);
            encoder.DrawPrimitives(
                MTLPrimitiveType.Line,
                0,
                checked((ulong)lineIndices.Length));
        }

        _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls);
        _telemetry.AddCounter(MapRenderFrameCounter.LogicalDrawCommands);
        _telemetry.AddCounter(MapRenderFrameCounter.ProgramChanges);
        _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges, 2);
        _telemetry.AddCounter(MapRenderFrameCounter.RenderStateChanges);
        _telemetry.AddCounter(MapRenderFrameCounter.UniformUpdates, 2);
        _telemetry.AddCounter(MapRenderFrameCounter.Passes);
        _telemetry.AddGpuPhaseWork(
            MapRenderGpuPhase.EditorOverlay,
            drawCalls: 1,
            triangles: 0);
    }

    private void ResetEditorSelectionOutline() =>
        _editorSelectionOutline = null;
}
