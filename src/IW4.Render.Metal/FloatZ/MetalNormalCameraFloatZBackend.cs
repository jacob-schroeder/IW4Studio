using System.Numerics;
using System.Runtime.Versioning;

using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Diagnostics;
using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Shaders;
using IW4.Render.Metal.Telemetry;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.FloatZ;

/// <summary>
/// Native target-2 raw D24 view followed by IW4's authored target-5 FloatZ
/// and target-8 ProcessedFloatZ passes. It is scene-owned but allocates its
/// target textures only after a visible executable consumer demands them.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalNormalCameraFloatZBackend : IDisposable
{
    private const int VectorByteCount = sizeof(float) * 4;
    private const int VertexCount = 4;
    private const int IndexCount = 6;
    private const int BufferAlignment = 256;
    private const int VertexInputVectorCount =
        VertexCount * MetalRsxShaderAbi.VertexInputFloat4Count;
    private static readonly RenderState FullscreenNoDepthState =
        RenderState.Default with
        {
            HasState = true,
            DepthTestEnabled = false,
            DepthWriteEnabled = false,
            Stencil = StencilState.Disabled,
            BlendEnabled = false,
            CullEnabled = false,
            PolygonOffsetMode = RenderPolygonOffsetMode.Disabled,
            ColorMask = RsxColorMask.Rgba
        };
    private static readonly ushort[] Indices = [3, 0, 2, 2, 0, 1];

    // The PS3 exposes target 2's two D24 samples as a doubled-width
    // A8R8G8B8 byte view. Quantizing here makes the input to $floatz exactly
    // D24 even when the native attachment is D32S8 on Apple GPUs.
    private const string RawDepthMsl = """
        #include <metal_stdlib>
        using namespace metal;
        struct RawDepthVertexOut { float4 position [[position]]; };
        vertex RawDepthVertexOut iw4_raw_depth_vertex(uint vertexId [[vertex_id]])
        {
            constexpr float2 positions[3] = {
                float2(-1.0, -1.0), float2(3.0, -1.0), float2(-1.0, 3.0) };
            RawDepthVertexOut output;
            output.position = float4(positions[vertexId], 0.0, 1.0);
            return output;
        }
        fragment float4 iw4_raw_depth_fragment(
            RawDepthVertexOut input [[stage_in]],
            depth2d_ms<float, access::read> sceneDepth [[texture(0)]])
        {
            uint2 rawPixel = uint2(input.position.xy);
            uint2 scenePixel = uint2(rawPixel.x >> 1u, rawPixel.y);
            uint storedSample = rawPixel.x & 1u;
            float depth = sceneDepth.read(scenePixel, storedSample);
            uint z24 = uint(round(clamp(depth, 0.0, 1.0) * 16777215.0));
            float highByte = float((z24 >> 16u) & 255u) / 255.0;
            float middleByte = float((z24 >> 8u) & 255u) / 255.0;
            float lowByte = float(z24 & 255u) / 255.0;
            return float4(middleByte, lowByte, 0.0, highByte);
        }
        """;

    private readonly MTLDevice _device;
    private readonly EmbeddedVertexConstant[] _floatZEmbeddedVertexConstants;
    private readonly EmbeddedVertexConstant[] _processedEmbeddedVertexConstants;
    private readonly StaticFragmentConstantPatch[] _floatZStaticPatches;
    private readonly StaticFragmentConstantPatch[] _processedStaticPatches;
    private readonly int _floatZStaticPixelRowCount;
    private readonly int _processedStaticPixelRowCount;
    private MTLRenderPipelineState _rawDepthPipeline;
    private MTLRenderPipelineState _floatZPipeline;
    private MTLRenderPipelineState _processedPipeline;
    private MTLSamplerState _pointClampSampler;
    private MTLTexture _rawDepthView;
    private MTLTexture _floatZ;
    private MTLTexture _processedFloatZ;
    private MTLBuffer _floatZSlab;
    private readonly MTLBuffer[] _processedSlabs = new MTLBuffer[3];
    private int _sceneWidth;
    private int _sceneHeight;
    private bool _disposed;

    internal MetalNormalCameraFloatZBackend(
        MTLDevice device,
        MapRenderWorldSceneSource source)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(source);
        _device = device;

        MapRenderNormalCameraFloatZRecipe recipe =
            MapRenderNormalCameraFloatZRecipe.Current;
        MapRenderNormalCameraMaterialProgramResolution floatZ =
            MapRenderNormalCameraMaterialProgramResolver.ResolveExact(
                source.AssetLookup,
                source.AssetPoolRevisionAtConstruction,
                recipe.FloatZ,
                expectedVertexInputDestinations: [0],
                expectedCodePixelSourceRows: [],
                expectedVertexConstantDestinations: [0, 1, 2, 3, 17, 18]);
        MapRenderNormalCameraMaterialProgramResolution processed =
            MapRenderNormalCameraMaterialProgramResolver.ResolveExact(
                source.AssetLookup,
                source.AssetPoolRevisionAtConstruction,
                recipe.ProcessedFloatZ,
                expectedVertexInputDestinations: [0],
                expectedCodePixelSourceRows: [(ushort)MaterialConstantSource.ZNear],
                expectedVertexConstantDestinations: [0, 1, 2, 3, 17, 18]);
        RequireFullscreenState(floatZ.RenderState, recipe.FloatZ.MaterialName);
        RequireFullscreenState(processed.RenderState, recipe.ProcessedFloatZ.MaterialName);
        _floatZEmbeddedVertexConstants = floatZ.Translation.EmbeddedVertexConstants.ToArray();
        _processedEmbeddedVertexConstants = processed.Translation.EmbeddedVertexConstants.ToArray();
        _floatZStaticPatches = floatZ.Translation.FragmentProgramIr.StaticConstantPatches.ToArray();
        _processedStaticPatches = processed.Translation.FragmentProgramIr.StaticConstantPatches.ToArray();
        _floatZStaticPixelRowCount = StaticRowCount(_floatZStaticPatches);
        _processedStaticPixelRowCount = StaticRowCount(_processedStaticPatches);

        try
        {
            _rawDepthPipeline = CompileRawDepthPipeline(device);
            _floatZPipeline = CompileAuthoredPipeline(
                device, floatZ, MTLPixelFormat.R32Float, recipe.FloatZ.MaterialName);
            _processedPipeline = CompileAuthoredPipeline(
                device, processed, MTLPixelFormat.R32Float, recipe.ProcessedFloatZ.MaterialName);
            _pointClampSampler = CreatePointClampSampler(device);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool IsSizedFor(int sceneWidth, int sceneHeight) =>
        _sceneWidth == sceneWidth && _sceneHeight == sceneHeight &&
        _rawDepthView.NativePtr != 0 && _floatZ.NativePtr != 0 &&
        _processedFloatZ.NativePtr != 0;

    internal void Resize(int sceneWidth, int sceneHeight)
    {
        ThrowIfDisposed();
        if (sceneWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneWidth));
        if (sceneHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHeight));
        if (IsSizedFor(sceneWidth, sceneHeight))
            return;

        MapRenderNormalCameraFloatZRecipe recipe =
            MapRenderNormalCameraFloatZRecipe.Current;
        MapRenderNormalCameraTargetExtent floatZExtent =
            recipe.FloatZTarget.ResolveExtent(sceneWidth, sceneHeight);
        MapRenderNormalCameraTargetExtent processedExtent =
            recipe.ProcessedFloatZTarget.ResolveExtent(sceneWidth, sceneHeight);
        if (floatZExtent != processedExtent ||
            floatZExtent.LogicalWidth <= 0 ||
            floatZExtent.LogicalHeight <= 0)
        {
            throw new InvalidOperationException(
                "FloatZ targets 5 and 8 must share one valid half-display extent.");
        }

        MTLTexture raw = CreateTexture(
            MTLPixelFormat.RGBA8Unorm,
            checked(sceneWidth * 2), sceneHeight);
        MTLTexture floatZ = CreateTexture(
            MTLPixelFormat.R32Float,
            floatZExtent.LogicalWidth, floatZExtent.LogicalHeight);
        MTLTexture processed = CreateTexture(
            MTLPixelFormat.R32Float,
            processedExtent.LogicalWidth, processedExtent.LogicalHeight);
        MTLBuffer floatZSlab = CreateSlab(
            floatZExtent.LogicalWidth, floatZExtent.LogicalHeight,
            _floatZEmbeddedVertexConstants, _floatZStaticPatches,
            _floatZStaticPixelRowCount, zNear: null);
        var processedSlabs = new MTLBuffer[_processedSlabs.Length];
        try
        {
            for (int index = 0; index < processedSlabs.Length; index++)
            {
                processedSlabs[index] = CreateSlab(
                    processedExtent.LogicalWidth, processedExtent.LogicalHeight,
                    _processedEmbeddedVertexConstants, _processedStaticPatches,
                    _processedStaticPixelRowCount, zNear: null);
            }
        }
        catch
        {
            foreach (MTLBuffer slab in processedSlabs)
            {
                if (slab.NativePtr != 0)
                    slab.Dispose();
            }
            raw.Dispose();
            floatZ.Dispose();
            processed.Dispose();
            floatZSlab.Dispose();
            throw;
        }
        DeleteTargets();
        _rawDepthView = raw;
        _floatZ = floatZ;
        _processedFloatZ = processed;
        _floatZSlab = floatZSlab;
        processedSlabs.CopyTo(_processedSlabs, 0);
        _sceneWidth = sceneWidth;
        _sceneHeight = sceneHeight;
    }

    internal MetalProcessedFloatZFrame Encode(
        MTLCommandBuffer commandBuffer,
        MTLTexture sceneDepthStencil,
        long frameRevision,
        float zNear,
        MetalRenderStateCache renderStates,
        MetalGpuPassTimer gpuTimer)
    {
        ThrowIfDisposed();
        if (commandBuffer.NativePtr == 0)
            throw new ArgumentException("A command buffer is required.", nameof(commandBuffer));
        if (sceneDepthStencil.NativePtr == 0)
            throw new ArgumentException("Target-2 depth is required.", nameof(sceneDepthStencil));
        ArgumentNullException.ThrowIfNull(renderStates);
        ArgumentNullException.ThrowIfNull(gpuTimer);
        if (!IsSizedFor(checked((int)sceneDepthStencil.Width), checked((int)sceneDepthStencil.Height)))
            throw new InvalidOperationException("FloatZ targets do not match target 2.");

        EncodeRawDepthView(commandBuffer, sceneDepthStencil, gpuTimer);
        EncodeAuthoredPass(commandBuffer, _floatZ, _floatZPipeline, _floatZSlab,
            _floatZStaticPixelRowCount, zNear: null, _rawDepthView,
            renderStates, gpuTimer);
        int processedSlabIndex = checked((int)(frameRevision % _processedSlabs.Length));
        EncodeAuthoredPass(commandBuffer, _processedFloatZ, _processedPipeline,
            _processedSlabs[processedSlabIndex], _processedStaticPixelRowCount,
            FrameDirectCodeConstants.ProduceZNearValue(zNear), _floatZ,
            renderStates, gpuTimer);
        return new MetalProcessedFloatZFrame(
            frameRevision, _processedFloatZ, _pointClampSampler);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DeleteTargets();
        Dispose(ref _pointClampSampler);
        Dispose(ref _processedPipeline);
        Dispose(ref _floatZPipeline);
        Dispose(ref _rawDepthPipeline);
    }

    private void EncodeRawDepthView(
        MTLCommandBuffer commandBuffer,
        MTLTexture depth,
        MetalGpuPassTimer gpuTimer)
    {
        using var pass = CreateColorPass(_rawDepthView);
        gpuTimer.AttachPass(pass, MapRenderGpuPhase.ProcessedFloatZ);
        MTLRenderCommandEncoder encoder = commandBuffer.RenderCommandEncoder(pass);
        if (encoder.NativePtr == 0)
            throw new InvalidOperationException("Metal could not begin the raw target-2 depth view.");
        try
        {
            SetViewport(encoder, checked(_sceneWidth * 2), _sceneHeight);
            encoder.SetRenderPipelineState(_rawDepthPipeline);
            encoder.SetFragmentTexture(depth, 0);
            encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
        }
        finally
        {
            encoder.EndEncoding();
        }
    }

    private void EncodeAuthoredPass(
        MTLCommandBuffer commandBuffer,
        MTLTexture target,
        MTLRenderPipelineState pipeline,
        MTLBuffer slab,
        int staticPixelRowCount,
        ShaderConstantValue? zNear,
        MTLTexture source,
        MetalRenderStateCache renderStates,
        MetalGpuPassTimer gpuTimer)
    {
        using var pass = CreateColorPass(target);
        gpuTimer.AttachPass(pass, MapRenderGpuPhase.ProcessedFloatZ);
        MTLRenderCommandEncoder encoder = commandBuffer.RenderCommandEncoder(pass);
        if (encoder.NativePtr == 0)
            throw new InvalidOperationException("Metal could not begin an authored FloatZ target.");
        try
        {
            SetViewport(encoder, checked((int)target.Width), checked((int)target.Height));
            encoder.SetRenderPipelineState(pipeline);
            renderStates.ApplyRasterState(encoder, FullscreenNoDepthState);
            if (zNear is { } value)
                WriteZNear(slab, staticPixelRowCount, value);
            int indexOffset = VertexInputVectorCount * VectorByteCount;
            int vertexConstantOffset = Align(indexOffset + IndexCount * sizeof(ushort));
            int codePixelOffset = Align(vertexConstantOffset +
                RsxVertexConstantLayout.Count * VectorByteCount);
            int staticPixelOffset = Align(codePixelOffset +
                CodeConstantLayout.Float4Count * VectorByteCount);
            encoder.SetVertexBuffer(slab, 0, MetalRsxShaderAbi.VertexInputBufferIndex);
            encoder.SetVertexBuffer(slab, checked((ulong)vertexConstantOffset),
                MetalRsxShaderAbi.VertexConstantBufferIndex);
            encoder.SetFragmentBuffer(slab, checked((ulong)codePixelOffset),
                MetalRsxShaderAbi.FragmentCodeConstantBufferIndex);
            encoder.SetFragmentBuffer(slab, checked((ulong)staticPixelOffset),
                MetalRsxShaderAbi.FragmentStaticConstantBufferIndex);
            encoder.SetFragmentTexture(source, 0);
            encoder.SetFragmentSamplerState(_pointClampSampler, 0);
            encoder.DrawIndexedPrimitives(MTLPrimitiveType.Triangle, IndexCount,
                MTLIndexType.UInt16, slab, checked((ulong)indexOffset));
        }
        finally
        {
            encoder.EndEncoding();
        }
    }

    private static MTLRenderPassDescriptor CreateColorPass(MTLTexture target)
    {
        var descriptor = new MTLRenderPassDescriptor
        {
            RenderTargetWidth = target.Width,
            RenderTargetHeight = target.Height,
            DefaultRasterSampleCount = 1
        };
        MTLRenderPassColorAttachmentDescriptor color = descriptor.ColorAttachments.Object(0);
        color.Texture = target;
        color.LoadAction = MTLLoadAction.DontCare;
        color.StoreAction = MTLStoreAction.Store;
        return descriptor;
    }

    private MTLTexture CreateTexture(MTLPixelFormat format, int width, int height)
    {
        using var descriptor = new MTLTextureDescriptor
        {
            TextureType = MTLTextureType.Type2D,
            PixelFormat = format,
            Width = checked((ulong)width),
            Height = checked((ulong)height),
            Depth = 1,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = 1,
            StorageMode = MTLStorageMode.Private,
            Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead
        };
        MTLTexture texture = _device.NewTexture(descriptor);
        if (texture.NativePtr == 0)
            throw new InvalidOperationException($"Metal failed to create {format} FloatZ target {width}x{height}.");
        return texture;
    }

    private MTLBuffer CreateSlab(
        int width,
        int height,
        IReadOnlyList<EmbeddedVertexConstant> embedded,
        IReadOnlyList<StaticFragmentConstantPatch> staticPatches,
        int staticPixelRowCount,
        ShaderConstantValue? zNear)
    {
        int indexOffset = VertexInputVectorCount * VectorByteCount;
        int vertexConstantOffset = Align(indexOffset + IndexCount * sizeof(ushort));
        int codePixelOffset = Align(vertexConstantOffset +
            RsxVertexConstantLayout.Count * VectorByteCount);
        int staticPixelOffset = Align(codePixelOffset +
            CodeConstantLayout.Float4Count * VectorByteCount);
        int byteCount = checked(staticPixelOffset + staticPixelRowCount * VectorByteCount);
        MTLBuffer slab = _device.NewBuffer(checked((ulong)byteCount),
            MTLResourceOptions.ResourceStorageModeShared |
            MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
        if (slab.NativePtr == 0 || slab.Contents == 0)
        {
            if (slab.NativePtr != 0)
                slab.Dispose();
            throw new InvalidOperationException("Metal failed to allocate the FloatZ fullscreen slab.");
        }
        try
        {
            new Span<byte>((void*)slab.Contents, byteCount).Clear();
            Span<Vector4> inputs = new((void*)slab.Contents, VertexInputVectorCount);
            inputs.Fill(new Vector4(0f, 0f, 0f, 1f));
            SetVertex(inputs, 0, 0f, 0f);
            SetVertex(inputs, 1, width, 0f);
            SetVertex(inputs, 2, width, height);
            SetVertex(inputs, 3, 0f, height);
            Indices.CopyTo(new Span<ushort>((void*)(slab.Contents + indexOffset), IndexCount));
            Span<Vector4> vertexConstants = new((void*)(slab.Contents + vertexConstantOffset),
                RsxVertexConstantLayout.Count);
            foreach (EmbeddedVertexConstant embeddedConstant in embedded)
            {
                if (!embeddedConstant.IsOperationallyResolved || embeddedConstant.Destination >= vertexConstants.Length)
                    throw new InvalidOperationException($"FloatZ embedded vertex constant c{embeddedConstant.Destination} is unresolved.");
                vertexConstants[embeddedConstant.Destination] = ToVector4(embeddedConstant.Value);
            }
            vertexConstants[0] = new Vector4(2f / width, 0f, 0f, 0f);
            vertexConstants[1] = new Vector4(0f, -2f / height, 0f, 0f);
            vertexConstants[2] = new Vector4(0f, 0f, 1f, 0f);
            vertexConstants[3] = new Vector4(-1f, 1f, 0f, 1f);
            var lookup = FrameDirectCodeConstants.ProduceClipSpaceLookup(
                width, height, 0, 0, width, height);
            vertexConstants[17] = ToVector4(lookup.Scale);
            vertexConstants[18] = ToVector4(lookup.Offset);
            Span<Vector4> staticPixels = new((void*)(slab.Contents + staticPixelOffset), staticPixelRowCount);
            foreach (StaticFragmentConstantPatch patch in staticPatches)
            {
                if ((uint)patch.ArgumentOrdinal >= (uint)staticPixels.Length)
                    throw new InvalidOperationException("FloatZ static fragment constant is outside its exact argument slab.");
                staticPixels[patch.ArgumentOrdinal] = ToVector4(patch.Value);
            }
            if (zNear is { } value)
                WriteZNear(slab, staticPixelRowCount, value);
            return slab;
        }
        catch
        {
            slab.Dispose();
            throw;
        }
    }

    private static void WriteZNear(MTLBuffer slab, int staticPixelRowCount, ShaderConstantValue value)
    {
        int indexOffset = VertexInputVectorCount * VectorByteCount;
        int vertexConstantOffset = Align(indexOffset + IndexCount * sizeof(ushort));
        int codePixelOffset = Align(vertexConstantOffset + RsxVertexConstantLayout.Count * VectorByteCount);
        if (staticPixelRowCount < 0)
            throw new ArgumentOutOfRangeException(nameof(staticPixelRowCount));
        Span<Vector4> codePixels = new((void*)(slab.Contents + codePixelOffset), CodeConstantLayout.Float4Count);
        codePixels[FrameDirectCodeConstants.ZNearRowIndex] = ToVector4(value);
    }

    private static MTLRenderPipelineState CompileRawDepthPipeline(MTLDevice device)
    {
        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        NSError error = default;
        using MTLLibrary library = device.NewLibrary(RawDepthMsl, options, ref error);
        if (library.NativePtr == 0 || error.NativePtr != 0)
            throw new InvalidOperationException($"Metal raw D24 FloatZ shader compilation failed: {Describe(error)}");
        using MTLFunction vertex = library.NewFunction("iw4_raw_depth_vertex");
        using MTLFunction fragment = library.NewFunction("iw4_raw_depth_fragment");
        if (vertex.NativePtr == 0 || fragment.NativePtr == 0)
            throw new InvalidOperationException("Metal raw D24 FloatZ entry points are missing.");
        using var descriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertex,
            FragmentFunction = fragment,
            RasterSampleCount = 1,
            InputPrimitiveTopology = MTLPrimitiveTopologyClass.Triangle
        };
        MTLRenderPipelineColorAttachmentDescriptor color =
            descriptor.ColorAttachments.Object(0);
        color.PixelFormat = MTLPixelFormat.RGBA8Unorm;
        NSError pipelineError = default;
        MTLRenderPipelineState pipeline = device.NewRenderPipelineState(descriptor, ref pipelineError);
        if (pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
            throw new InvalidOperationException($"Metal raw D24 FloatZ pipeline creation failed: {Describe(pipelineError)}");
        return pipeline;
    }

    private static MTLRenderPipelineState CompileAuthoredPipeline(
        MTLDevice device,
        MapRenderNormalCameraMaterialProgramResolution resolution,
        MTLPixelFormat targetFormat,
        string materialName)
    {
        RsxVertexMslLoweringResult vertex = RsxVertexMslLowerer.Lower(resolution.Translation.VertexProgramIr);
        RsxFragmentMslLoweringResult fragment = RsxFragmentMslLowerer.Lower(
            resolution.Translation.FragmentProgramIr, resolution.RenderState,
            suppressShaderPackerForDiagnosticOutput: false);
        RequireExactLowering(vertex, fragment, materialName);
        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        NSError vertexError = default;
        using MTLLibrary vertexLibrary = device.NewLibrary(vertex.Msl!, options, ref vertexError);
        if (vertexLibrary.NativePtr == 0 || vertexError.NativePtr != 0)
            throw new InvalidOperationException($"{materialName} Metal vertex compilation failed: {Describe(vertexError)}");
        NSError fragmentError = default;
        using MTLLibrary fragmentLibrary = device.NewLibrary(fragment.Msl!, options, ref fragmentError);
        if (fragmentLibrary.NativePtr == 0 || fragmentError.NativePtr != 0)
            throw new InvalidOperationException($"{materialName} Metal fragment compilation failed: {Describe(fragmentError)}");
        using MTLFunction vertexFunction = vertexLibrary.NewFunction(MetalRsxShaderAbi.VertexEntryPoint);
        using MTLFunction fragmentFunction = fragmentLibrary.NewFunction(MetalRsxShaderAbi.FragmentEntryPoint);
        if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            throw new InvalidOperationException($"{materialName} Metal entry points are missing.");
        using var descriptor = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertexFunction,
            FragmentFunction = fragmentFunction,
            RasterSampleCount = 1,
            InputPrimitiveTopology = MTLPrimitiveTopologyClass.Triangle
        };
        MetalRenderStateCache.ConfigureColorAttachment(
            descriptor.ColorAttachments.Object(0),
            FullscreenNoDepthState,
            targetFormat);
        NSError pipelineError = default;
        MTLRenderPipelineState pipeline = device.NewRenderPipelineState(descriptor, ref pipelineError);
        if (pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
            throw new InvalidOperationException($"{materialName} Metal pipeline creation failed: {Describe(pipelineError)}");
        return pipeline;
    }

    private static MTLSamplerState CreatePointClampSampler(MTLDevice device)
    {
        using var descriptor = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Nearest,
            MagFilter = MTLSamplerMinMagFilter.Nearest,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge
        };
        MTLSamplerState sampler = device.NewSamplerState(descriptor);
        if (sampler.NativePtr == 0)
            throw new InvalidOperationException("Metal failed to create the FloatZ point-clamp sampler.");
        return sampler;
    }

    private void DeleteTargets()
    {
        for (int index = 0; index < _processedSlabs.Length; index++)
            Dispose(ref _processedSlabs[index]);
        Dispose(ref _floatZSlab);
        Dispose(ref _processedFloatZ);
        Dispose(ref _floatZ);
        Dispose(ref _rawDepthView);
        _sceneWidth = 0;
        _sceneHeight = 0;
    }

    private static void Dispose<T>(ref T resource) where T : struct, IDisposable
    {
        if (resource is { } value)
            value.Dispose();
        resource = default;
    }

    private static void SetViewport(MTLRenderCommandEncoder encoder, int width, int height)
    {
        encoder.SetViewport(new MTLViewport { originX = 0, originY = 0, width = width, height = height, znear = 0, zfar = 1 });
        encoder.SetScissorRect(new MTLScissorRect { x = 0, y = 0, width = checked((ulong)width), height = checked((ulong)height) });
    }

    private static void SetVertex(Span<Vector4> inputs, int index, float x, float y)
    {
        int offset = index * MetalRsxShaderAbi.VertexInputFloat4Count;
        inputs[offset] = new Vector4(x, y, 0f, 1f);
    }

    private static int StaticRowCount(IReadOnlyList<StaticFragmentConstantPatch> patches) =>
        Math.Max(1, patches.Count == 0 ? 0 : checked(patches.Max(patch => patch.ArgumentOrdinal) + 1));

    private static int Align(int value) => checked((value + BufferAlignment - 1) & ~(BufferAlignment - 1));

    private static Vector4 ToVector4(ShaderConstantValue value) => new(value.X, value.Y, value.Z, value.W);

    private static void RequireFullscreenState(RenderState state, string materialName)
    {
        if (state.DepthTestEnabled || state.DepthWriteEnabled ||
            state.Stencil.Enabled || state.BlendEnabled ||
            state.ColorMask != RsxColorMask.Rgba)
        {
            throw new InvalidOperationException(
                $"{materialName} is not an exact no-depth FloatZ fullscreen state.");
        }
    }

    private static void RequireExactLowering(RsxVertexMslLoweringResult vertex, RsxFragmentMslLoweringResult fragment, string materialName)
    {
        if (!vertex.IsReady || vertex.Msl is null)
            throw new InvalidOperationException($"{materialName} Metal vertex lowering failed: {string.Join('|', vertex.Blockers)}");
        if (!fragment.IsReady || fragment.Msl is null)
            throw new InvalidOperationException($"{materialName} Metal fragment lowering failed: {string.Join('|', fragment.Blockers)}");
        if (!fragment.SampledDestinations.SequenceEqual([0]) || !fragment.ColorAttachmentIndices.SequenceEqual([0]) || fragment.ExportsDepth)
            throw new InvalidOperationException($"{materialName} is outside the exact FloatZ attachment ABI.");
    }

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "unknown native error"
            : error.LocalizedDescription.ToString() ?? "unknown native error";

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
