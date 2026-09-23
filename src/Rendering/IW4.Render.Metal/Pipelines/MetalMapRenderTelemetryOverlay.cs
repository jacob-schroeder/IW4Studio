using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.Metal.Telemetry;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Owns the native resources used to draw backend-neutral pixel HUD geometry
/// over the retained Metal host output.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalMapRenderTelemetryOverlay : IDisposable
{
    internal const int DrawCallCount = 2;
    internal const int PassCount = 0;
    internal const int ProgramChangeCount = 1;
    internal const int RenderStateChangeCount = 1;
    internal const int BufferChangeCount = 1;
    internal const int UniformUpdateCount = 3;

    private const ulong PositionBufferIndex = 0;
    private const ulong FramebufferSizeBufferIndex = 1;
    private const ulong ColorBufferIndex = 0;

    private static readonly Vector4 BackgroundColor =
        new(0f, 0f, 0f, 0.65f);
    private static readonly Vector4 TextColor =
        new(0.93f, 0.97f, 1f, 1f);

    private readonly MTLDevice _device;
    private readonly List<float> _geometryVertices = [];
    private readonly MTLBuffer[] _vertexBuffers =
        new MTLBuffer[MetalCommandBufferRing.SlotCount];
    private readonly int[] _vertexBufferCapacities =
        new int[MetalCommandBufferRing.SlotCount];
    private readonly long[] _uploadedCpuGeometryRevisions =
        new long[MetalCommandBufferRing.SlotCount];
    private MTLRenderPipelineState _pipeline;
    private int _backgroundVertexCount;
    private int _textVertexCount;
    private long _cpuGeometryRevision;
    private bool _disposed;

    internal MetalMapRenderTelemetryOverlay(MTLDevice device)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));

        _device = device;
        Array.Fill(_uploadedCpuGeometryRevisions, -1);
        _pipeline = CreatePipeline(device);
    }

    /// <summary>
    /// Rebuilds the CPU-side geometry for one displayed telemetry snapshot.
    /// Each command slot uploads this revision only after its preceding
    /// command buffer has retired.
    /// </summary>
    internal void UpdateCpuGeometry(string text, float renderScaling)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(text);

        int glyphPixelSize =
            MapRenderTelemetryOverlayGeometry.GetGlyphPixelSize(renderScaling);
        (_backgroundVertexCount, _textVertexCount) =
            MapRenderTelemetryOverlayGeometry.Write(
                text,
                glyphPixelSize,
                _geometryVertices);
        if (_textVertexCount == 0)
        {
            throw new InvalidOperationException(
                "The Metal telemetry overlay text produced no drawable glyphs.");
        }

        _cpuGeometryRevision = checked(_cpuGeometryRevision + 1);
    }

    /// <summary>
    /// Encodes into the final host-output render pass and a command slot already
    /// retired by <see cref="MetalCommandBufferRing.Begin"/>. Rewriting that
    /// slot's shared buffer is therefore safe without separate synchronization.
    /// </summary>
    internal long EncodeInto(
        MTLRenderCommandEncoder encoder,
        MTLTexture target,
        int commandSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal render command encoder is required.",
                nameof(encoder));
        }
        if (target.NativePtr == 0 ||
            target.PixelFormat != MTLPixelFormat.BGRA8Unorm)
        {
            throw new ArgumentException(
                "The Metal telemetry overlay requires a BGRA8Unorm target.",
                nameof(target));
        }
        ValidateSlot(commandSlot);
        if (_cpuGeometryRevision == 0 || _textVertexCount == 0)
        {
            throw new InvalidOperationException(
                "The Metal telemetry overlay has no prepared geometry.");
        }

        MTLBuffer vertices = PrepareSlotBuffer(commandSlot);
        SetViewport(encoder, target.Width, target.Height);
        encoder.SetRenderPipelineState(_pipeline);
        encoder.SetCullMode(MTLCullMode.None);
        encoder.SetTriangleFillMode(MTLTriangleFillMode.Fill);
        encoder.SetVertexBuffer(vertices, 0, PositionBufferIndex);

        var framebufferSize = new Vector2(
            checked((float)target.Width),
            checked((float)target.Height));
        encoder.SetVertexBytes(
            (nint)(&framebufferSize),
            checked((ulong)sizeof(Vector2)),
            FramebufferSizeBufferIndex);

        Vector4 backgroundColor = BackgroundColor;
        encoder.SetFragmentBytes(
            (nint)(&backgroundColor),
            checked((ulong)sizeof(Vector4)),
            ColorBufferIndex);
        encoder.DrawPrimitives(
            MTLPrimitiveType.Triangle,
            0,
            checked((ulong)_backgroundVertexCount));

        Vector4 textColor = TextColor;
        encoder.SetFragmentBytes(
            (nint)(&textColor),
            checked((ulong)sizeof(Vector4)),
            ColorBufferIndex);
        encoder.DrawPrimitives(
            MTLPrimitiveType.Triangle,
            checked((ulong)_backgroundVertexCount),
            checked((ulong)_textVertexCount));

        return checked(
            (_backgroundVertexCount + (long)_textVertexCount) / 3L);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        for (int slot = 0; slot < _vertexBuffers.Length; slot++)
            DisposeSlotBuffer(slot);
        if (_pipeline.NativePtr != 0)
        {
            _pipeline.Dispose();
            _pipeline = default;
        }
    }

    private MTLBuffer PrepareSlotBuffer(int commandSlot)
    {
        int requiredByteCount = checked(
            _geometryVertices.Count * sizeof(float));
        MTLBuffer buffer = _vertexBuffers[commandSlot];
        if (buffer.NativePtr == 0 ||
            _vertexBufferCapacities[commandSlot] < requiredByteCount)
        {
            DisposeSlotBuffer(commandSlot);
            buffer = _device.NewBuffer(
                checked((ulong)requiredByteCount),
                MTLResourceOptions.ResourceStorageModeShared |
                MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
            if (buffer.NativePtr == 0 || buffer.Contents == 0)
            {
                if (buffer.NativePtr != 0)
                    buffer.Dispose();
                throw new InvalidOperationException(
                    $"Metal could not allocate the {requiredByteCount}-byte telemetry overlay vertex buffer for command slot {commandSlot}.");
            }
            _vertexBuffers[commandSlot] = buffer;
            _vertexBufferCapacities[commandSlot] = requiredByteCount;
            _uploadedCpuGeometryRevisions[commandSlot] = -1;
        }

        if (_uploadedCpuGeometryRevisions[commandSlot] !=
            _cpuGeometryRevision)
        {
            CollectionsMarshal.AsSpan(_geometryVertices).CopyTo(
                new Span<float>(
                    (void*)buffer.Contents,
                    _geometryVertices.Count));
            _uploadedCpuGeometryRevisions[commandSlot] =
                _cpuGeometryRevision;
        }
        return buffer;
    }

    private void DisposeSlotBuffer(int commandSlot)
    {
        MTLBuffer buffer = _vertexBuffers[commandSlot];
        if (buffer.NativePtr != 0)
            buffer.Dispose();
        _vertexBuffers[commandSlot] = default;
        _vertexBufferCapacities[commandSlot] = 0;
        _uploadedCpuGeometryRevisions[commandSlot] = -1;
    }

    private static MTLRenderPipelineState CreatePipeline(MTLDevice device)
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
            library = device.NewLibrary(Source, options, ref libraryError);
            if (library.NativePtr == 0 || libraryError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Metal telemetry overlay shader compilation failed: {Describe(libraryError)}");
            }

            vertexFunction = library.NewFunction("iw4TelemetryOverlayVertex");
            fragmentFunction = library.NewFunction(
                "iw4TelemetryOverlayFragment");
            if (vertexFunction.NativePtr == 0 ||
                fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The Metal telemetry overlay shader entry points are missing.");
            }

            var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertexFunction,
                FragmentFunction = fragmentFunction,
                RasterSampleCount = 1,
                InputPrimitiveTopology = MTLPrimitiveTopologyClass.Triangle
            };
            try
            {
                MTLRenderPipelineColorAttachmentDescriptor attachment =
                    descriptor.ColorAttachments.Object(0);
                attachment.PixelFormat = MTLPixelFormat.BGRA8Unorm;
                attachment.WriteMask =
                    MTLColorWriteMask.Red |
                    MTLColorWriteMask.Green |
                    MTLColorWriteMask.Blue |
                    MTLColorWriteMask.Alpha;
                attachment.IsBlendingEnabled = true;
                attachment.RgbBlendOperation = MTLBlendOperation.Add;
                attachment.AlphaBlendOperation = MTLBlendOperation.Add;
                attachment.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha;
                attachment.DestinationRGBBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;
                attachment.SourceAlphaBlendFactor = MTLBlendFactor.One;
                attachment.DestinationAlphaBlendFactor =
                    MTLBlendFactor.OneMinusSourceAlpha;

                NSError pipelineError = default;
                MTLRenderPipelineState pipeline =
                    device.NewRenderPipelineState(
                        descriptor,
                        ref pipelineError);
                if (pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
                {
                    throw new InvalidOperationException(
                        $"Metal telemetry overlay pipeline creation failed: {Describe(pipelineError)}");
                }
                return pipeline;
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

    private static void SetViewport(
        MTLRenderCommandEncoder encoder,
        ulong width,
        ulong height)
    {
        encoder.SetViewport(new MTLViewport
        {
            originX = 0,
            originY = 0,
            width = width,
            height = height,
            znear = 0,
            zfar = 1
        });
        encoder.SetScissorRect(new MTLScissorRect
        {
            x = 0,
            y = 0,
            width = width,
            height = height
        });
    }

    private static void ValidateSlot(int commandSlot)
    {
        if ((uint)commandSlot >= MetalCommandBufferRing.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(commandSlot));
    }

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";

    private const string Source = """
        #include <metal_stdlib>
        using namespace metal;

        struct TelemetryOverlayVertexOut
        {
            float4 position [[position]];
        };

        vertex TelemetryOverlayVertexOut iw4TelemetryOverlayVertex(
            const device float2* positions [[buffer(0)]],
            constant float2& framebufferSize [[buffer(1)]],
            uint vertexId [[vertex_id]])
        {
            float2 normalized = positions[vertexId] / framebufferSize;
            TelemetryOverlayVertexOut result;
            result.position = float4(
                normalized.x * 2.0 - 1.0,
                1.0 - normalized.y * 2.0,
                0.0,
                1.0);
            return result;
        }

        fragment float4 iw4TelemetryOverlayFragment(
            constant float4& color [[buffer(0)]])
        {
            return color;
        }
        """;
}
