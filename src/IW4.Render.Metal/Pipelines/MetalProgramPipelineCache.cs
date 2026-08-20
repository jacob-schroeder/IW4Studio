using System.Collections.Immutable;
using System.Runtime.Versioning;

using IW4.Render.Execution;
using IW4.Render.Metal.Shaders;
using IW4.Render.Metal.Targets;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Device-local cache for directly lowered RSX render pipelines. Failed
/// lowerings and native compilations are cached as well, so scene admission
/// can preflight every authored pass without retrying a known-bad program on
/// later groups or frames.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalProgramPipelineCache : IDisposable
{
    private readonly MTLDevice _device;
    private readonly MTLPixelFormat _depthStencilFormat;
    private readonly bool _emulateDepth24;
    private readonly Dictionary<PipelineKey, MetalProgramPipeline>
        _pipelines = [];
    private readonly Dictionary<PipelineKey, string> _failures = [];
    private bool _disposed;

    internal MetalProgramPipelineCache(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);
        _device = device;
        _depthStencilFormat = depthStencilFormat.PixelFormat;
        _emulateDepth24 = depthStencilFormat.EmulatesDepth24;
    }

    internal bool TryGetOrCreate(
        RenderNormalCameraPreparedPassSnapshot pass,
        TranslatedProgramVertexConstantBindingPlan vertexConstantPlan,
        out MetalProgramPipeline? pipeline,
        out string blocker)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(vertexConstantPlan);

        RenderWorldShaderProvenanceSnapshot shader = pass.ShaderProvenance;
        bool usesStaticModelInstancing =
            pass.SourceKind == RenderNormalCameraDrawSourceKind.StaticModel;
        RsxVertexMslLoweringResult vertex = shader.VertexProgramIr is { } vertexIr
            ? RsxVertexMslLowerer.Lower(
                vertexIr,
                shader.VertexInputs,
                vertexConstantPlan,
                usesStaticModelInstancing)
            : new RsxVertexMslLoweringResult(
                null,
                false,
                ImmutableArray.Create("vertexProgramIr=missing"));
        RsxFragmentMslLoweringResult fragment =
            shader.FragmentProgramIr is { } fragmentIr
                ? RsxFragmentMslLowerer.Lower(
                    fragmentIr,
                    pass.SourceState,
                    suppressShaderPackerForDiagnosticOutput: false,
                    MetalFrameTargets.SceneTargetOutputs,
                    emulateDepth24: _emulateDepth24)
                : new RsxFragmentMslLoweringResult(
                    null,
                    false,
                    ImmutableArray.Create("fragmentProgramIr=missing"));

        string staticIdentity = vertex.StaticCompositionIdentity ?? "world";
        ImmutableArray<int> colorAttachments =
            fragment.ColorAttachmentIndices.IsDefault
                ? ImmutableArray<int>.Empty
                : fragment.ColorAttachmentIndices;
        string attachmentIdentity = string.Join(',', colorAttachments);
        var key = new PipelineKey(
            shader.ProgramCacheKey,
            pass.SourceState,
            usesStaticModelInstancing,
            staticIdentity,
            fragment.AlphaTestMode,
            fragment.ShaderPackerMode,
            attachmentIdentity,
            fragment.ExportsDepth,
            pass.Geometry.Topology);
        if (_pipelines.TryGetValue(key, out MetalProgramPipeline? cached))
        {
            pipeline = cached;
            blocker = string.Empty;
            return true;
        }
        if (_failures.TryGetValue(key, out string? cachedFailure))
        {
            pipeline = null;
            blocker = cachedFailure;
            return false;
        }

        if (!shader.ProgramIrReady || !shader.VertexInputPayloadReady)
        {
            return Fail(
                key,
                "shaderProgram=IR_OR_VERTEX_INPUT_PAYLOAD_NOT_READY",
                out pipeline,
                out blocker);
        }
        if (!vertex.IsReady || vertex.Msl is null)
        {
            return Fail(
                key,
                $"vertexMsl={string.Join('|', vertex.Blockers)}",
                out pipeline,
                out blocker);
        }
        if (!fragment.IsReady || fragment.Msl is null)
        {
            return Fail(
                key,
                $"fragmentMsl={string.Join('|', fragment.Blockers)}",
                out pipeline,
                out blocker);
        }
        if (colorAttachments.Any(index => index != 0))
        {
            return Fail(
                key,
                $"fragmentAttachments={attachmentIdentity}:SCENE_SURFACE_A_TOPOLOGY_MISMATCH",
                out pipeline,
                out blocker);
        }

        try
        {
            MTLRenderPipelineState state = Compile(
                vertex.Msl,
                fragment.Msl,
                pass.SourceState,
                pass.Geometry.Topology,
                colorAttachments);
            pipeline = new MetalProgramPipeline(
                state,
                fragment.SampledDestinations.IsDefault
                    ? ImmutableArray<int>.Empty
                    : fragment.SampledDestinations,
                vertex.UsesStaticModelInstancing,
                vertex.StaticInstanceFloat4Stride,
                vertex.StaticPlacementFloat4Offset,
                vertex.StaticLightingPayload);
            _pipelines.Add(key, pipeline);
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            OverflowException)
        {
            return Fail(
                key,
                $"metalPipeline={exception.Message}",
                out pipeline,
                out blocker);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (MetalProgramPipeline pipeline in _pipelines.Values)
            pipeline.Dispose();
        _pipelines.Clear();
        _failures.Clear();
    }

    private MTLRenderPipelineState Compile(
        string vertexSource,
        string fragmentSource,
        RenderState renderState,
        RenderPrimitiveTopology topology,
        ImmutableArray<int> colorAttachments)
    {
        using var options = new MTLCompileOptions();
        MTLLibrary vertexLibrary = default;
        MTLLibrary fragmentLibrary = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        try
        {
            NSError vertexError = default;
            vertexLibrary = _device.NewLibrary(
                vertexSource,
                options,
                ref vertexError);
            if (vertexLibrary.NativePtr == 0 || vertexError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"vertex compilation failed: {Describe(vertexError)}");
            }

            NSError fragmentError = default;
            fragmentLibrary = _device.NewLibrary(
                fragmentSource,
                options,
                ref fragmentError);
            if (fragmentLibrary.NativePtr == 0 || fragmentError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"fragment compilation failed: {Describe(fragmentError)}");
            }

            vertexFunction = vertexLibrary.NewFunction(
                MetalRsxShaderAbi.VertexEntryPoint);
            fragmentFunction = fragmentLibrary.NewFunction(
                MetalRsxShaderAbi.FragmentEntryPoint);
            if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "direct RSX Metal entry points are missing");
            }

            var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertexFunction,
                FragmentFunction = fragmentFunction,
                RasterSampleCount = MetalFrameTargets.SceneSampleCount,
                InputPrimitiveTopology = ToTopologyClass(topology),
                DepthAttachmentPixelFormat = _depthStencilFormat,
                StencilAttachmentPixelFormat = _depthStencilFormat
            };
            try
            {
                if (colorAttachments.Contains(0))
                {
                    MetalRenderStateCache.ConfigureColorAttachment(
                        descriptor.ColorAttachments.Object(0),
                        renderState,
                        MetalFrameTargets.SceneColorFormat);
                }

                NSError pipelineError = default;
                MTLRenderPipelineState result = _device.NewRenderPipelineState(
                    descriptor,
                    ref pipelineError);
                if (result.NativePtr == 0 || pipelineError.NativePtr != 0)
                {
                    throw new InvalidOperationException(
                        $"state creation failed: {Describe(pipelineError)}");
                }
                return result;
            }
            finally
            {
                descriptor.Dispose();
            }
        }
        finally
        {
            if (fragmentFunction.NativePtr != 0)
                fragmentFunction.Dispose();
            if (vertexFunction.NativePtr != 0)
                vertexFunction.Dispose();
            if (fragmentLibrary.NativePtr != 0)
                fragmentLibrary.Dispose();
            if (vertexLibrary.NativePtr != 0)
                vertexLibrary.Dispose();
        }
    }

    private bool Fail(
        PipelineKey key,
        string reason,
        out MetalProgramPipeline? pipeline,
        out string blocker)
    {
        _failures.TryAdd(key, reason);
        pipeline = null;
        blocker = reason;
        return false;
    }

    private static MTLPrimitiveTopologyClass ToTopologyClass(
        RenderPrimitiveTopology topology) => topology switch
    {
        RenderPrimitiveTopology.TriangleList or
        RenderPrimitiveTopology.TriangleStrip =>
            MTLPrimitiveTopologyClass.Triangle,
        RenderPrimitiveTopology.LineList => MTLPrimitiveTopologyClass.Line,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";

    private readonly record struct PipelineKey(
        string ProgramCacheKey,
        RenderState RenderState,
        bool UsesStaticModelInstancing,
        string StaticCompositionIdentity,
        AlphaTestMode AlphaTestMode,
        MetalRsxShaderPackerMode ShaderPackerMode,
        string ColorAttachments,
        bool ExportsDepth,
        RenderPrimitiveTopology Topology);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalProgramPipeline : IDisposable
{
    private MTLRenderPipelineState _state;

    internal MetalProgramPipeline(
        MTLRenderPipelineState state,
        ImmutableArray<int> sampledDestinations,
        bool usesStaticModelInstancing,
        int staticInstanceFloat4Stride,
        int staticPlacementFloat4Offset,
        string? staticLightingPayload)
    {
        if (state.NativePtr == 0)
            throw new ArgumentException("A Metal pipeline state is required.", nameof(state));
        _state = state;
        SampledDestinations = sampledDestinations;
        UsesStaticModelInstancing = usesStaticModelInstancing;
        StaticInstanceFloat4Stride = staticInstanceFloat4Stride;
        StaticPlacementFloat4Offset = staticPlacementFloat4Offset;
        StaticLightingPayload = staticLightingPayload;
    }

    internal MTLRenderPipelineState State => _state.NativePtr != 0
        ? _state
        : throw new ObjectDisposedException(nameof(MetalProgramPipeline));

    internal ImmutableArray<int> SampledDestinations { get; }

    internal bool UsesStaticModelInstancing { get; }

    internal int StaticInstanceFloat4Stride { get; }

    internal int StaticPlacementFloat4Offset { get; }

    internal string? StaticLightingPayload { get; }

    public void Dispose()
    {
        if (_state.NativePtr == 0)
            return;
        _state.Dispose();
        _state = default;
    }
}
