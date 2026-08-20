using System.Collections.Immutable;
using System.Runtime.Versioning;

using IW4.Render.EditorPreview;
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
/// Device-local pipelines for the standard opaque IW4 depth owner. The
/// authored null pixel program is represented by a vertex-only Metal pipeline
/// when native D24 is available. Devices exposing only D32S8 execute the
/// smallest possible depth-only fragment to snap the candidate to the PS3
/// 24-bit fixed-point grid; no scene-color attachment is declared either way.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalDepthPrepassPipelineCache : IDisposable
{
    private const string Depth24FragmentSource = """

        struct Iw4Depth24FragmentOut
        {
            float depth [[depth(any)]];
        };

        fragment Iw4Depth24FragmentOut iw4Depth24Fragment(
            float4 position [[position]],
            constant float2& depthBias [[buffer(0)]],
            uint sampleId [[sample_id]])
        {
            (void)sampleId;
            constexpr float maximum = 16777215.0f;
            float rasterDepth = position.z;
            float slope = max(
                abs(dfdx(rasterDepth)),
                abs(dfdy(rasterDepth)));
            float biasedDepth = clamp(
                rasterDepth + depthBias.x + depthBias.y * slope,
                0.0f,
                1.0f);
            Iw4Depth24FragmentOut result;
            result.depth = floor(
                biasedDepth * maximum + 0.5f) / maximum;
            return result;
        }
        """;

    private readonly MTLDevice _device;
    private readonly MTLPixelFormat _depthStencilFormat;
    private readonly bool _emulateDepth24;
    private readonly Dictionary<PipelineKey, MetalDepthPrepassPipeline>
        _pipelines = [];
    private readonly Dictionary<PipelineKey, string> _failures = [];
    private bool _disposed;

    internal MetalDepthPrepassPipelineCache(
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
        MapRenderEditorDepthPrepassPlan plan,
        RenderWorldShaderProvenanceSnapshot shader,
        TranslatedProgramVertexConstantBindingPlan vertexConstantPlan,
        out MetalDepthPrepassPipeline? pipeline,
        out string blocker)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(vertexConstantPlan);

        bool usesStaticModelInstancing =
            pass.SourceKind == RenderNormalCameraDrawSourceKind.StaticModel;
        RsxVertexMslLoweringResult vertex = shader.VertexProgramIr is { } ir
            ? RsxVertexMslLowerer.Lower(
                ir,
                shader.VertexInputs,
                vertexConstantPlan,
                usesStaticModelInstancing)
            : new RsxVertexMslLoweringResult(
                null,
                false,
                ImmutableArray.Create("vertexProgramIr=missing"));
        string staticIdentity = vertex.StaticCompositionIdentity ?? "world";
        var key = new PipelineKey(
            shader.ProgramCacheKey,
            plan.State,
            usesStaticModelInstancing,
            staticIdentity,
            pass.Geometry.Topology);
        if (_pipelines.TryGetValue(key, out MetalDepthPrepassPipeline? cached))
        {
            pipeline = cached;
            blocker = string.Empty;
            return true;
        }
        if (_failures.TryGetValue(key, out string? failure))
        {
            pipeline = null;
            blocker = failure;
            return false;
        }

        if (plan.Program !=
                MapRenderEditorDepthPrepassProgram.TransformOnlyNull ||
            plan.State.ColorMask != RsxColorMask.None ||
            plan.State.AlphaTestEnabled ||
            plan.State.BlendEnabled ||
            !plan.State.DepthTestEnabled ||
            !plan.State.DepthWriteEnabled ||
            plan.State.DepthFunc != RsxCompareFunction.LessThanOrEqual ||
            plan.State.Stencil.Enabled)
        {
            return Fail(
                key,
                "depthPlan=STANDARD_TRANSFORM_ONLY_NULL_CONTRACT_MISMATCH",
                out pipeline,
                out blocker);
        }
        if (!shader.VertexInputPayloadReady ||
            shader.VertexProgramIr is null)
        {
            return Fail(
                key,
                "depthShader=VERTEX_IR_OR_INPUT_PAYLOAD_NOT_READY",
                out pipeline,
                out blocker);
        }
        // The canonical RSX null fragment still carries four register-export
        // descriptors. They are inert here: the exact depth plan above proves
        // ColorMask=None, native D24 uses a vertex-only pipeline, and D32 uses
        // our depth-only emulation fragment instead of the authored fragment.
        if (shader.FragmentDepthExportEnabled ||
            !shader.ProgramSamplerDestinations.IsEmpty)
        {
            return Fail(
                key,
                "depthShader=NULL_FRAGMENT_CONTRACT_MISMATCH",
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

        try
        {
            MTLRenderPipelineState state = Compile(
                vertex.Msl,
                pass.Geometry.Topology);
            pipeline = new MetalDepthPrepassPipeline(
                state,
                vertex.UsesStaticModelInstancing,
                vertex.StaticInstanceFloat4Stride,
                vertex.StaticPlacementFloat4Offset,
                staticIdentity);
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
                $"metalDepthPipeline={exception.Message}",
                out pipeline,
                out blocker);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (MetalDepthPrepassPipeline pipeline in _pipelines.Values)
            pipeline.Dispose();
        _pipelines.Clear();
        _failures.Clear();
    }

    private MTLRenderPipelineState Compile(
        string vertexSource,
        RenderPrimitiveTopology topology)
    {
        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        MTLLibrary library = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        try
        {
            NSError libraryError = default;
            library = _device.NewLibrary(
                _emulateDepth24
                    ? vertexSource + Depth24FragmentSource
                    : vertexSource,
                options,
                ref libraryError);
            if (library.NativePtr == 0 || libraryError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"vertex compilation failed: {Describe(libraryError)}");
            }

            vertexFunction = library.NewFunction(
                MetalRsxShaderAbi.VertexEntryPoint);
            if (vertexFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "the direct RSX depth vertex entry point is missing");
            }
            if (_emulateDepth24)
            {
                fragmentFunction = library.NewFunction(
                    "iw4Depth24Fragment");
                if (fragmentFunction.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "the D24 emulation fragment entry point is missing");
                }
            }

            var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertexFunction,
                FragmentFunction = fragmentFunction,
                RasterSampleCount = MetalFrameTargets.SceneSampleCount,
                InputPrimitiveTopology = ToTopologyClass(topology),
                DepthAttachmentPixelFormat =
                    _depthStencilFormat,
                StencilAttachmentPixelFormat =
                    _depthStencilFormat
            };
            try
            {
                NSError pipelineError = default;
                MTLRenderPipelineState result =
                    _device.NewRenderPipelineState(
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
            if (library.NativePtr != 0)
                library.Dispose();
        }
    }

    private bool Fail(
        PipelineKey key,
        string reason,
        out MetalDepthPrepassPipeline? pipeline,
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(topology),
            topology,
            "The opaque depth prepass requires triangle geometry.")
    };

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";

    private readonly record struct PipelineKey(
        string ProgramCacheKey,
        RenderState State,
        bool UsesStaticModelInstancing,
        string StaticCompositionIdentity,
        RenderPrimitiveTopology Topology);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalDepthPrepassPipeline : IDisposable
{
    private MTLRenderPipelineState _state;

    internal MetalDepthPrepassPipeline(
        MTLRenderPipelineState state,
        bool usesStaticModelInstancing,
        int staticInstanceFloat4Stride,
        int staticPlacementFloat4Offset,
        string staticCompositionIdentity)
    {
        if (state.NativePtr == 0)
            throw new ArgumentException("A Metal pipeline state is required.", nameof(state));
        _state = state;
        UsesStaticModelInstancing = usesStaticModelInstancing;
        StaticInstanceFloat4Stride = staticInstanceFloat4Stride;
        StaticPlacementFloat4Offset = staticPlacementFloat4Offset;
        StaticCompositionIdentity = staticCompositionIdentity;
    }

    internal MTLRenderPipelineState State => _state.NativePtr != 0
        ? _state
        : throw new ObjectDisposedException(nameof(MetalDepthPrepassPipeline));

    internal bool UsesStaticModelInstancing { get; }

    internal int StaticInstanceFloat4Stride { get; }

    internal int StaticPlacementFloat4Offset { get; }

    internal string StaticCompositionIdentity { get; }

    public void Dispose()
    {
        if (_state.NativePtr == 0)
            return;
        _state.Dispose();
        _state = default;
    }
}

internal static class MetalDepthPrepassShaderAbi
{
    internal const ulong DepthBiasBufferIndex = 0;
}
