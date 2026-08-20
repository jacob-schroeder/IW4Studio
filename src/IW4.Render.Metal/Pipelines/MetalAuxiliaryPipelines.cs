using System.Runtime.Versioning;

using IW4.Render.Metal.Targets;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Persistent native programs for the normal-camera sky and editor-only
/// geometry stages. These programs consume the immutable scene buffers
/// directly, so no vertex repacking or per-frame upload is required.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalAuxiliaryPipelines : IDisposable
{
    private const string Source = """
        #include <metal_stdlib>
        using namespace metal;

        constant uint iw4VertexFloatStride = 6;
        constant uint iw4InstanceFloat4Stride = 3;

        struct Iw4FrameConstants
        {
            float4x4 worldViewProjection;
        };

        struct Iw4SkyVertexOut
        {
            float4 position [[position]];
            float3 cubeDirection;
        };

        struct Iw4ColorVertexOut
        {
            float4 position [[position]];
            float3 color;
        };

        inline float3 iw4ReadPosition(
            device const float* vertices,
            uint vertexId)
        {
            uint first = vertexId * iw4VertexFloatStride;
            return float3(
                vertices[first],
                vertices[first + 1],
                vertices[first + 2]);
        }

        inline float3 iw4ReadColor(
            device const float* vertices,
            uint vertexId)
        {
            uint first = vertexId * iw4VertexFloatStride + 3;
            return float3(
                vertices[first],
                vertices[first + 1],
                vertices[first + 2]);
        }

        vertex Iw4SkyVertexOut iw4SkyVertex(
            device const float* vertices [[buffer(0)]],
            constant Iw4FrameConstants& frame [[buffer(2)]],
            uint vertexId [[vertex_id]])
        {
            float3 position = iw4ReadPosition(vertices, vertexId);
            float4 clip = frame.worldViewProjection * float4(position, 1.0);

            Iw4SkyVertexOut result;
            // Scene positions are (game.x, game.z, -game.y). wc_sky routes
            // the original game-space position to TEX0 for cube lookup.
            result.cubeDirection = float3(
                position.x,
                -position.z,
                position.y);
            // Place the sky at the far plane while preserving perspective.
            result.position = float4(clip.xy, clip.w, clip.w);
            return result;
        }

        fragment float4 iw4SkyFragment(
            Iw4SkyVertexOut input [[stage_in]],
            texturecube<float> skyTexture [[texture(0)]],
            sampler skySampler [[sampler(0)]])
        {
            return skyTexture.sample(
                skySampler,
                normalize(input.cubeDirection));
        }

        vertex Iw4ColorVertexOut iw4DiagnosticVertex(
            device const float* vertices [[buffer(0)]],
            constant Iw4FrameConstants& frame [[buffer(2)]],
            uint vertexId [[vertex_id]])
        {
            Iw4ColorVertexOut result;
            float3 position = iw4ReadPosition(vertices, vertexId);
            result.position =
                frame.worldViewProjection * float4(position, 1.0);
            result.color = iw4ReadColor(vertices, vertexId);
            return result;
        }

        vertex Iw4ColorVertexOut iw4InstancedDiagnosticVertex(
            device const float* vertices [[buffer(0)]],
            device const float4* instances [[buffer(1)]],
            constant Iw4FrameConstants& frame [[buffer(2)]],
            uint vertexId [[vertex_id]],
            uint instanceId [[instance_id]])
        {
            float4 localPosition = float4(
                iw4ReadPosition(vertices, vertexId),
                1.0);
            uint firstRow = instanceId * iw4InstanceFloat4Stride;
            float3 position = float3(
                dot(instances[firstRow], localPosition),
                dot(instances[firstRow + 1], localPosition),
                dot(instances[firstRow + 2], localPosition));

            Iw4ColorVertexOut result;
            result.position =
                frame.worldViewProjection * float4(position, 1.0);
            result.color = iw4ReadColor(vertices, vertexId);
            return result;
        }

        fragment float4 iw4ColorFragment(
            Iw4ColorVertexOut input [[stage_in]])
        {
            return float4(input.color, 1.0);
        }
        """;

    private MTLRenderPipelineState _sky;
    private MTLRenderPipelineState _diagnostic;
    private MTLRenderPipelineState _instancedDiagnostic;
    private MTLRenderPipelineState _wireframe;
    private bool _disposed;

    internal MetalAuxiliaryPipelines(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);

        using var options = new MTLCompileOptions();
        NSError libraryError = default;
        MTLLibrary library = default;
        MTLFunction skyVertex = default;
        MTLFunction skyFragment = default;
        MTLFunction colorVertex = default;
        MTLFunction instancedColorVertex = default;
        MTLFunction colorFragment = default;
        try
        {
            library = device.NewLibrary(Source, options, ref libraryError);
            if (library.NativePtr == 0 || libraryError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Metal auxiliary shader compilation failed: {Describe(libraryError)}");
            }

            skyVertex = library.NewFunction("iw4SkyVertex");
            skyFragment = library.NewFunction("iw4SkyFragment");
            colorVertex = library.NewFunction("iw4DiagnosticVertex");
            instancedColorVertex = library.NewFunction(
                "iw4InstancedDiagnosticVertex");
            colorFragment = library.NewFunction("iw4ColorFragment");
            if (skyVertex.NativePtr == 0 ||
                skyFragment.NativePtr == 0 ||
                colorVertex.NativePtr == 0 ||
                instancedColorVertex.NativePtr == 0 ||
                colorFragment.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal auxiliary shader entry points are missing.");
            }

            _sky = CreatePipeline(
                device,
                skyVertex,
                skyFragment,
                MTLPrimitiveTopologyClass.Triangle,
                SkyRenderState,
                depthStencilFormat.PixelFormat,
                "sky");
            _diagnostic = CreatePipeline(
                device,
                colorVertex,
                colorFragment,
                MTLPrimitiveTopologyClass.Triangle,
                DiagnosticRenderState,
                depthStencilFormat.PixelFormat,
                "diagnostic");
            _instancedDiagnostic = CreatePipeline(
                device,
                instancedColorVertex,
                colorFragment,
                MTLPrimitiveTopologyClass.Triangle,
                DiagnosticRenderState,
                depthStencilFormat.PixelFormat,
                "instanced diagnostic");
            _wireframe = CreatePipeline(
                device,
                colorVertex,
                colorFragment,
                MTLPrimitiveTopologyClass.Line,
                WireframeRenderState,
                depthStencilFormat.PixelFormat,
                "wireframe");
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (colorFragment.NativePtr != 0)
                colorFragment.Dispose();
            if (instancedColorVertex.NativePtr != 0)
                instancedColorVertex.Dispose();
            if (colorVertex.NativePtr != 0)
                colorVertex.Dispose();
            if (skyFragment.NativePtr != 0)
                skyFragment.Dispose();
            if (skyVertex.NativePtr != 0)
                skyVertex.Dispose();
            if (library.NativePtr != 0)
                library.Dispose();
        }
    }

    internal MTLRenderPipelineState Sky => Require(_sky);

    internal MTLRenderPipelineState Diagnostic => Require(_diagnostic);

    internal MTLRenderPipelineState InstancedDiagnostic =>
        Require(_instancedDiagnostic);

    internal MTLRenderPipelineState Wireframe => Require(_wireframe);

    internal static RenderState SkyRenderState { get; } =
        RenderState.Default with
        {
            HasState = true,
            DepthWriteEnabled = false
        };

    internal static RenderState DiagnosticRenderState { get; } =
        RenderState.Default with { HasState = true };

    internal static RenderState WireframeRenderState { get; } =
        RenderState.Default with
        {
            HasState = true,
            DepthTestEnabled = false,
            DepthWriteEnabled = false
        };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(ref _wireframe);
        Dispose(ref _instancedDiagnostic);
        Dispose(ref _diagnostic);
        Dispose(ref _sky);
    }

    private static MTLRenderPipelineState CreatePipeline(
        MTLDevice device,
        MTLFunction vertex,
        MTLFunction fragment,
        MTLPrimitiveTopologyClass topology,
        RenderState renderState,
        MTLPixelFormat depthStencilFormat,
        string label)
    {
        var descriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertex,
            FragmentFunction = fragment,
            RasterSampleCount = MetalFrameTargets.SceneSampleCount,
            InputPrimitiveTopology = topology,
            DepthAttachmentPixelFormat = depthStencilFormat,
            StencilAttachmentPixelFormat = depthStencilFormat
        };
        try
        {
            MetalRenderStateCache.ConfigureColorAttachment(
                descriptor.ColorAttachments.Object(0),
                renderState,
                MetalFrameTargets.SceneColorFormat);
            NSError error = default;
            MTLRenderPipelineState result = device.NewRenderPipelineState(
                descriptor,
                ref error);
            if (result.NativePtr == 0 || error.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Metal {label} pipeline creation failed: {Describe(error)}");
            }
            return result;
        }
        finally
        {
            descriptor.Dispose();
        }
    }

    private static MTLRenderPipelineState Require(MTLRenderPipelineState state)
    {
        if (state.NativePtr == 0)
            throw new ObjectDisposedException(nameof(MetalAuxiliaryPipelines));
        return state;
    }

    private static void Dispose(ref MTLRenderPipelineState state)
    {
        if (state.NativePtr == 0)
            return;
        state.Dispose();
        state = default;
    }

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";
}
