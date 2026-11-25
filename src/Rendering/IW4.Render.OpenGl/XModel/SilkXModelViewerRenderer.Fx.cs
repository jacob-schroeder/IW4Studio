using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Transforms;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.XModel;

public sealed unsafe partial class SilkXModelViewerRenderer
{
    private readonly Dictionary<string, MapRenderEditorDrawGroup<AuthoredDraw>>
        _fxGroupsByMaterial = new(StringComparer.Ordinal);
    private readonly Dictionary<AuthoredDraw, FxStagingBuffers>
        _fxStagingByDraw = new(ReferenceEqualityComparer.Instance);
    private readonly List<FxPreparedBatch> _fxPreparedBatches = [];
    private readonly HashSet<string> _fxSeenMaterials = new(StringComparer.Ordinal);
    private readonly List<string> _fxFrameDiagnostics = [];

    public XModelViewerUploadResult UploadFxMaterials(
        XModelRenderScene? scene)
    {
        XModelViewerUploadResult result =
            Upload(scene?.Lods.FirstOrDefault(), scene,
                allowFloatZFailure: true);
        foreach (MapRenderEditorDrawGroup<AuthoredDraw> group in _drawGroups)
        {
            _fxGroupsByMaterial.Add(
                group.AuthoredPasses[0].Surface.MaterialName,
                group);
            foreach (AuthoredDraw draw in group.AuthoredPasses)
                _fxStagingByDraw.Add(draw, new FxStagingBuffers());
        }
        return new XModelViewerUploadResult(
            result.ExecutableGroupCount,
            result.BlockedGroupCount,
            result.Diagnostics.Where(diagnostic =>
                !diagnostic.Contains("viewerRuntimeInputs=",
                    StringComparison.Ordinal)).ToArray());
    }

    /// <summary>
    /// Draws the current FX sample through the retained authored material
    /// groups. Sample positions and RSX vertex inputs remain in game space;
    /// the camera is in the host preview coordinate space.
    /// </summary>
    public IReadOnlyList<string> RenderFx(
        int framebuffer,
        int width,
        int height,
        RenderCamera camera,
        float materialTimeSeconds,
        IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> samples)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(samples);
        _fxFrameDiagnostics.Clear();
        if (width <= 0 || height <= 0)
            return [];
        if (!float.IsFinite(materialTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(materialTimeSeconds));

        float aspect = width / (float)height;
        DerivedMatrixState matrices =
            OpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                camera, aspect);
        ClipSpaceLookupCodeConstants clipSpaceLookup =
            FrameDirectCodeConstants.ProduceClipSpaceLookup(
                width, height, 0, 0, width, height);
        ShaderConstantValue zNearConstant =
            FrameDirectCodeConstants.ProduceZNearValue(camera.NearPlane);
        ApplyViewerReflectionEnvironment(studioEnabled: true);
        EstablishFrameState(
            framebuffer,
            width,
            height,
            useCheckerboard: false,
            background: new Vector3(0.07f, 0.08f, 0.095f));
        _currentProcessedFloatZFrame = null;

        _fxPreparedBatches.Clear();
        _fxSeenMaterials.Clear();
        foreach ((string material, FxPreviewVertex[] vertices) in samples)
        {
            if (!_fxSeenMaterials.Add(material) ||
                vertices is null || vertices.Length == 0 ||
                vertices.Length % 3 != 0 ||
                !_fxGroupsByMaterial.TryGetValue(material, out var group))
            {
                if (vertices is null || vertices.Length % 3 != 0)
                    _fxFrameDiagnostics.Add($"{material}: incomplete FX triangle list");
                continue;
            }

            FxPreviewVertex[] drawableVertices = vertices;
            if (GroupRequiresTangent(group))
            {
                int completeTriangles = 0;
                for (int index = 0; index < vertices.Length; index += 3)
                {
                    if (vertices[index].HasTangent &&
                        vertices[index + 1].HasTangent &&
                        vertices[index + 2].HasTangent)
                    {
                        completeTriangles++;
                    }
                }
                int skippedTriangles = vertices.Length / 3 -
                    completeTriangles;
                if (skippedTriangles != 0)
                {
                    _fxFrameDiagnostics.Add(
                        $"{material}: skipped {skippedTriangles} FX triangle(s) without authored tangent data");
                    if (completeTriangles == 0)
                        continue;
                    drawableVertices = new FxPreviewVertex[
                        checked(completeTriangles * 3)];
                    int nextVertex = 0;
                    for (int index = 0; index < vertices.Length; index += 3)
                    {
                        if (!vertices[index].HasTangent ||
                            !vertices[index + 1].HasTangent ||
                            !vertices[index + 2].HasTangent)
                        {
                            continue;
                        }
                        Array.Copy(vertices, index,
                            drawableVertices, nextVertex, 3);
                        nextVertex += 3;
                    }
                }
            }

            bool ready = true;
            foreach (AuthoredDraw draw in group.AuthoredPasses)
            {
                FxStagingBuffers staging = _fxStagingByDraw[draw];
                int sourceCount = checked(
                    drawableVertices.Length * OpenGlPackedRsxVertexLayout
                        .SourceFloatStride);
                int packedCount = draw.Layout.PackedFloatCount(sourceCount);
                staging.EnsureCapacity(sourceCount, packedCount);
                if (!FxRenderVertexPayload.Project(
                        drawableVertices,
                        draw.VertexInputs,
                        staging.Source.AsSpan(0, sourceCount),
                        out string blocker))
                {
                    _fxFrameDiagnostics.Add($"{material}: {blocker}");
                    ready = false;
                    break;
                }
                draw.Layout.Pack(
                    staging.Source.AsSpan(0, sourceCount),
                    staging.Packed.AsSpan(0, packedCount));
            }
            if (!ready)
                continue;

            foreach (AuthoredDraw draw in group.AuthoredPasses)
            {
                FxStagingBuffers staging = _fxStagingByDraw[draw];
                int sourceCount = checked(
                    drawableVertices.Length * OpenGlPackedRsxVertexLayout
                        .SourceFloatStride);
                int packedCount = draw.Layout.PackedFloatCount(sourceCount);
                _state.BindArrayBuffer(draw.VertexBuffer);
                fixed (float* data = staging.Packed)
                {
                    _gl.BufferData(
                        BufferTargetARB.ArrayBuffer,
                        checked((nuint)(packedCount * sizeof(float))),
                        data,
                        BufferUsageARB.DynamicDraw);
                }
            }
            _fxPreparedBatches.Add(new FxPreparedBatch(
                group,
                checked((uint)drawableVertices.Length)));
        }

        long frameRevision = checked(++_frameRevision);
        bool needsFloatZ = false;
        foreach (FxPreparedBatch batch in _fxPreparedBatches)
        {
            if (!BatchRequiresProcessedFloatZ(batch))
                continue;
            needsFloatZ = true;
            break;
        }
        bool floatZReady = false;
        if (needsFloatZ && _floatZ is not null)
        {
            try
            {
                BuildProcessedFloatZ(
                    framebuffer,
                    width,
                    height,
                    camera.NearPlane,
                    frameRevision,
                    () =>
                    {
                        foreach (FxPreparedBatch batch in _fxPreparedBatches)
                        {
                            if (batch.Group.Bucket ==
                                MapRenderEditorDrawBucket.Translucent)
                            {
                                continue;
                            }
                            foreach (AuthoredDraw draw in batch.Group.AuthoredPasses)
                            {
                                if (!draw.State.DepthTestEnabled ||
                                    !draw.State.DepthWriteEnabled ||
                                    draw.State.BlendEnabled ||
                                    RequiresProcessedFloatZ(draw))
                                {
                                    continue;
                                }
                                BindAuthoredPass(
                                    draw,
                                    matrices,
                                    materialTimeSeconds,
                                    clipSpaceLookup,
                                    zNearConstant);
                                _state.ColorMask(false, false, false, false);
                                _state.BindVertexArray(draw.VertexArray);
                                _gl.DrawArrays(
                                    PrimitiveType.Triangles,
                                    0,
                                    batch.VertexCount);
                            }
                        }
                    });
                floatZReady = true;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                _currentProcessedFloatZFrame = null;
                foreach (FxPreparedBatch batch in _fxPreparedBatches)
                {
                    if (BatchRequiresProcessedFloatZ(batch))
                    {
                        _fxFrameDiagnostics.Add(
                            $"{batch.Group.AuthoredPasses[0].Surface.MaterialName}: " +
                            $"processedFloatZ unavailable: {exception.Message}");
                    }
                }
                EstablishFrameState(
                    framebuffer,
                    width,
                    height,
                    useCheckerboard: false,
                    background: new Vector3(0.07f, 0.08f, 0.095f));
            }
        }
        else if (needsFloatZ)
        {
            foreach (FxPreparedBatch batch in _fxPreparedBatches)
            {
                if (BatchRequiresProcessedFloatZ(batch))
                {
                    _fxFrameDiagnostics.Add(
                        $"{batch.Group.AuthoredPasses[0].Surface.MaterialName}: " +
                        "processedFloatZ backend is unavailable");
                }
            }
        }

        for (int bucket = 0; bucket < 2; bucket++)
        {
            foreach (FxPreparedBatch batch in _fxPreparedBatches)
            {
                if ((batch.Group.Bucket ==
                        MapRenderEditorDrawBucket.Translucent) !=
                    (bucket == 1) ||
                    (!floatZReady && BatchRequiresProcessedFloatZ(batch)))
                {
                    continue;
                }
                foreach (AuthoredDraw draw in batch.Group.AuthoredPasses)
                {
                    BindAuthoredPass(
                        draw,
                        matrices,
                        materialTimeSeconds,
                        clipSpaceLookup,
                        zNearConstant);
                    _state.BindVertexArray(draw.VertexArray);
                    _gl.DrawArrays(
                        PrimitiveType.Triangles,
                        0,
                        batch.VertexCount);
                }
            }
        }

        _state.ActiveTexture(0);
        _state.BindVertexArray(0);
        _state.UseProgram(0);
        return _fxFrameDiagnostics.Count == 0
            ? []
            : _fxFrameDiagnostics.ToArray();
    }

    private static bool BatchRequiresProcessedFloatZ(
        FxPreparedBatch batch)
    {
        foreach (AuthoredDraw draw in batch.Group.AuthoredPasses)
        {
            if (RequiresProcessedFloatZ(draw))
                return true;
        }
        return false;
    }

    private static bool RequiresProcessedFloatZ(AuthoredDraw draw)
    {
        foreach (ShaderRuntimeSamplerRequirement requirement in
                 draw.RuntimeSamplerRequirements)
        {
            if (requirement.ResourceKind ==
                ShaderRuntimeSamplerResourceKind.ProcessedFloatZ)
            {
                return true;
            }
        }
        return false;
    }

    private static bool GroupRequiresTangent(
        MapRenderEditorDrawGroup<AuthoredDraw> group)
    {
        foreach (AuthoredDraw draw in group.AuthoredPasses)
        {
            foreach (ShaderVertexInputBinding binding in draw.VertexInputs)
            {
                if (binding.Source == MaterialStreamSource.Tangent)
                    return true;
            }
        }
        return false;
    }

    private void ClearFxMaterialResources()
    {
        _fxGroupsByMaterial.Clear();
        _fxStagingByDraw.Clear();
        _fxPreparedBatches.Clear();
        _fxSeenMaterials.Clear();
        _fxFrameDiagnostics.Clear();
    }

    private sealed class FxStagingBuffers
    {
        internal float[] Source = [];
        internal float[] Packed = [];

        internal void EnsureCapacity(int sourceCount, int packedCount)
        {
            if (Source.Length < sourceCount)
                Source = new float[sourceCount];
            if (Packed.Length < packedCount)
                Packed = new float[packedCount];
        }
    }

    private sealed record FxPreparedBatch(
        MapRenderEditorDrawGroup<AuthoredDraw> Group,
        uint VertexCount);
}
