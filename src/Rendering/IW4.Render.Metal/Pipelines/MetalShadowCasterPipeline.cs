using System.Runtime.Versioning;

using IW4.Render.Metal.Targets;
using IW4.Render.Scheduling.Shadows;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Native depth-only pipelines for the exact slot-2 caster payloads. World
/// vertices and static-model instances use separate entry points so world
/// draws never bind a dummy instance stream. Alpha-tested variants preserve
/// the authored route-01 color and route-02 texture test.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalShadowCasterPipeline : IDisposable
{
    private const string Source = """
        #include <metal_stdlib>
        using namespace metal;

        constant uint iw4OpaqueVertexFloatStride = 3;
        constant uint iw4CutoutVertexFloatStride = 9;
        constant uint iw4InstanceFloat4Stride = 3;
        constant float iw4CutoutAlphaReference = 128.0 / 255.0;

        struct Iw4ShadowFrame
        {
            float4x4 worldViewProjection;
        };

        struct Iw4ShadowVertexOut
        {
            float4 position [[position]];
        };

        struct Iw4ShadowCutoutVertexOut
        {
            float4 position [[position]];
            float4 color;
            float2 texCoord;
        };

        struct Iw4ShadowDepth24Out
        {
            float depth [[depth(any)]];
        };

        inline float iw4QuantizeDepth24(float depth)
        {
            constexpr float maxDepth24 = 16777215.0;
            return floor(clamp(depth, 0.0, 1.0) * maxDepth24 + 0.5) /
                maxDepth24;
        }

        inline float3 iw4ReadOpaquePosition(
            device const float* vertices,
            uint vertexId)
        {
            uint first = vertexId * iw4OpaqueVertexFloatStride;
            return float3(
                vertices[first],
                vertices[first + 1],
                vertices[first + 2]);
        }

        inline float3 iw4ReadCutoutPosition(
            device const float* vertices,
            uint vertexId)
        {
            uint first = vertexId * iw4CutoutVertexFloatStride;
            return float3(
                vertices[first],
                vertices[first + 1],
                vertices[first + 2]);
        }

        inline float3 iw4TransformInstance(
            device const float4* instances,
            uint instanceId,
            float3 localPosition)
        {
            uint firstRow = instanceId * iw4InstanceFloat4Stride;
            float4 local = float4(localPosition, 1.0);
            return float3(
                dot(instances[firstRow], local),
                dot(instances[firstRow + 1], local),
                dot(instances[firstRow + 2], local));
        }

        vertex Iw4ShadowVertexOut iw4ShadowOpaqueWorldVertex(
            device const float* vertices [[buffer(0)]],
            constant Iw4ShadowFrame& frame [[buffer(2)]],
            uint vertexId [[vertex_id]])
        {
            Iw4ShadowVertexOut result;
            result.position = frame.worldViewProjection * float4(
                iw4ReadOpaquePosition(vertices, vertexId),
                1.0);
            return result;
        }

        vertex Iw4ShadowVertexOut iw4ShadowOpaqueStaticVertex(
            device const float* vertices [[buffer(0)]],
            device const float4* instances [[buffer(1)]],
            constant Iw4ShadowFrame& frame [[buffer(2)]],
            uint vertexId [[vertex_id]],
            uint instanceId [[instance_id]])
        {
            float3 local = iw4ReadOpaquePosition(vertices, vertexId);
            Iw4ShadowVertexOut result;
            result.position = frame.worldViewProjection * float4(
                iw4TransformInstance(instances, instanceId, local),
                1.0);
            return result;
        }

        vertex Iw4ShadowCutoutVertexOut iw4ShadowCutoutWorldVertex(
            device const float* vertices [[buffer(0)]],
            constant Iw4ShadowFrame& frame [[buffer(2)]],
            uint vertexId [[vertex_id]])
        {
            uint first = vertexId * iw4CutoutVertexFloatStride;
            Iw4ShadowCutoutVertexOut result;
            result.position = frame.worldViewProjection * float4(
                iw4ReadCutoutPosition(vertices, vertexId),
                1.0);
            result.color = float4(
                vertices[first + 3],
                vertices[first + 4],
                vertices[first + 5],
                vertices[first + 6]);
            result.texCoord = float2(
                vertices[first + 7],
                vertices[first + 8]);
            return result;
        }

        vertex Iw4ShadowCutoutVertexOut iw4ShadowCutoutStaticVertex(
            device const float* vertices [[buffer(0)]],
            device const float4* instances [[buffer(1)]],
            constant Iw4ShadowFrame& frame [[buffer(2)]],
            uint vertexId [[vertex_id]],
            uint instanceId [[instance_id]])
        {
            uint first = vertexId * iw4CutoutVertexFloatStride;
            float3 local = iw4ReadCutoutPosition(vertices, vertexId);
            Iw4ShadowCutoutVertexOut result;
            result.position = frame.worldViewProjection * float4(
                iw4TransformInstance(instances, instanceId, local),
                1.0);
            result.color = float4(
                vertices[first + 3],
                vertices[first + 4],
                vertices[first + 5],
                vertices[first + 6]);
            result.texCoord = float2(
                vertices[first + 7],
                vertices[first + 8]);
            return result;
        }

        fragment void iw4ShadowCutoutFragment(
            Iw4ShadowCutoutVertexOut input [[stage_in]],
            texture2d<float> colorTexture [[texture(0)]],
            sampler colorSampler [[sampler(0)]])
        {
            float alpha = colorTexture.sample(
                colorSampler,
                input.texCoord).a * input.color.a;
            if (alpha < iw4CutoutAlphaReference)
                discard_fragment();
        }

        fragment Iw4ShadowDepth24Out iw4ShadowOpaqueDepth24Fragment(
            Iw4ShadowVertexOut input [[stage_in]],
            constant float2& depthBias [[buffer(0)]])
        {
            float rasterDepth = input.position.z;
            float slope = max(
                abs(dfdx(rasterDepth)),
                abs(dfdy(rasterDepth)));
            Iw4ShadowDepth24Out result;
            result.depth = iw4QuantizeDepth24(clamp(
                rasterDepth + depthBias.x + depthBias.y * slope,
                0.0,
                1.0));
            return result;
        }

        fragment Iw4ShadowDepth24Out iw4ShadowCutoutDepth24Fragment(
            Iw4ShadowCutoutVertexOut input [[stage_in]],
            texture2d<float> colorTexture [[texture(0)]],
            sampler colorSampler [[sampler(0)]],
            constant float2& depthBias [[buffer(0)]])
        {
            float rasterDepth = input.position.z;
            float slope = max(
                abs(dfdx(rasterDepth)),
                abs(dfdy(rasterDepth)));
            float alpha = colorTexture.sample(
                colorSampler,
                input.texCoord).a * input.color.a;
            if (alpha < iw4CutoutAlphaReference)
                discard_fragment();

            Iw4ShadowDepth24Out result;
            result.depth = iw4QuantizeDepth24(clamp(
                rasterDepth + depthBias.x + depthBias.y * slope,
                0.0,
                1.0));
            return result;
        }
        """;

    private MTLRenderPipelineState _opaqueWorld;
    private MTLRenderPipelineState _opaqueStatic;
    private MTLRenderPipelineState _cutoutWorld;
    private MTLRenderPipelineState _cutoutStatic;
    private bool _disposed;

    internal MetalShadowCasterPipeline(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);

        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        MTLLibrary library = default;
        MTLFunction opaqueWorldVertex = default;
        MTLFunction opaqueStaticVertex = default;
        MTLFunction cutoutWorldVertex = default;
        MTLFunction cutoutStaticVertex = default;
        MTLFunction opaqueFragment = default;
        MTLFunction cutoutFragment = default;
        try
        {
            NSError libraryError = default;
            library = device.NewLibrary(Source, options, ref libraryError);
            if (library.NativePtr == 0 || libraryError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Metal shadow shader compilation failed: {Describe(libraryError)}");
            }

            opaqueWorldVertex = library.NewFunction(
                "iw4ShadowOpaqueWorldVertex");
            opaqueStaticVertex = library.NewFunction(
                "iw4ShadowOpaqueStaticVertex");
            cutoutWorldVertex = library.NewFunction(
                "iw4ShadowCutoutWorldVertex");
            cutoutStaticVertex = library.NewFunction(
                "iw4ShadowCutoutStaticVertex");
            if (depthStencilFormat.EmulatesDepth24)
            {
                opaqueFragment = library.NewFunction(
                    "iw4ShadowOpaqueDepth24Fragment");
                cutoutFragment = library.NewFunction(
                    "iw4ShadowCutoutDepth24Fragment");
            }
            else
            {
                cutoutFragment = library.NewFunction(
                    "iw4ShadowCutoutFragment");
            }
            if (opaqueWorldVertex.NativePtr == 0 ||
                opaqueStaticVertex.NativePtr == 0 ||
                cutoutWorldVertex.NativePtr == 0 ||
                cutoutStaticVertex.NativePtr == 0 ||
                (depthStencilFormat.EmulatesDepth24 &&
                 opaqueFragment.NativePtr == 0) ||
                cutoutFragment.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal shadow shader entry points are incomplete.");
            }

            _opaqueWorld = CreatePipeline(
                device,
                opaqueWorldVertex,
                opaqueFragment,
                depthStencilFormat.PixelFormat,
                "opaque world");
            _opaqueStatic = CreatePipeline(
                device,
                opaqueStaticVertex,
                opaqueFragment,
                depthStencilFormat.PixelFormat,
                "opaque static-model");
            _cutoutWorld = CreatePipeline(
                device,
                cutoutWorldVertex,
                cutoutFragment,
                depthStencilFormat.PixelFormat,
                "cutout world");
            _cutoutStatic = CreatePipeline(
                device,
                cutoutStaticVertex,
                cutoutFragment,
                depthStencilFormat.PixelFormat,
                "cutout static-model");
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Dispose(ref cutoutFragment);
            Dispose(ref opaqueFragment);
            Dispose(ref cutoutStaticVertex);
            Dispose(ref cutoutWorldVertex);
            Dispose(ref opaqueStaticVertex);
            Dispose(ref opaqueWorldVertex);
            if (library.NativePtr != 0)
                library.Dispose();
        }
    }

    internal MTLRenderPipelineState Resolve(
        MapRenderSunShadowCasterMaterialKind materialKind,
        bool instanced) => (materialKind, instanced) switch
    {
        (MapRenderSunShadowCasterMaterialKind.Opaque, false) =>
            Require(_opaqueWorld),
        (MapRenderSunShadowCasterMaterialKind.Opaque, true) =>
            Require(_opaqueStatic),
        (MapRenderSunShadowCasterMaterialKind.Cutout, false) =>
            Require(_cutoutWorld),
        (MapRenderSunShadowCasterMaterialKind.Cutout, true) =>
            Require(_cutoutStatic),
        _ => throw new ArgumentOutOfRangeException(nameof(materialKind))
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(ref _cutoutStatic);
        Dispose(ref _cutoutWorld);
        Dispose(ref _opaqueStatic);
        Dispose(ref _opaqueWorld);
    }

    private static MTLRenderPipelineState CreatePipeline(
        MTLDevice device,
        MTLFunction vertex,
        MTLFunction fragment,
        MTLPixelFormat depthStencilFormat,
        string role)
    {
        var descriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertex,
            RasterSampleCount = 1,
            InputPrimitiveTopology = MTLPrimitiveTopologyClass.Triangle,
            DepthAttachmentPixelFormat =
                depthStencilFormat,
            StencilAttachmentPixelFormat =
                depthStencilFormat
        };
        if (fragment.NativePtr != 0)
            descriptor.FragmentFunction = fragment;
        try
        {
            NSError error = default;
            MTLRenderPipelineState pipeline =
                device.NewRenderPipelineState(descriptor, ref error);
            if (pipeline.NativePtr == 0 || error.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Metal could not create the {role} shadow pipeline: " +
                    Describe(error));
            }
            return pipeline;
        }
        finally
        {
            descriptor.Dispose();
        }
    }

    private static MTLRenderPipelineState Require(
        MTLRenderPipelineState state) => state.NativePtr != 0
            ? state
            : throw new ObjectDisposedException(
                nameof(MetalShadowCasterPipeline));

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ??
                "unknown Metal error";

    private static void Dispose(ref MTLFunction function)
    {
        if (function.NativePtr == 0)
            return;
        function.Dispose();
        function = default;
    }

    private static void Dispose(ref MTLRenderPipelineState state)
    {
        if (state.NativePtr == 0)
            return;
        state.Dispose();
        state = default;
    }
}

internal static class MetalShadowCasterShaderAbi
{
    internal const ulong DepthBiasBufferIndex = 0;
}
