using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Metal.Shaders;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Scene-revision-owned execution of IW4's canonical <c>postfx</c> material
/// into the native Metal drawable. The Scene target is resolved by hardware;
/// this class owns only the exact indexed fullscreen draw and its program ABI.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalNormalCameraPresentation : IDisposable
{
    internal const MTLPixelFormat DrawableFormat = MTLPixelFormat.BGRA8Unorm;

    private const int VectorByteCount = sizeof(float) * 4;
    private const int VertexCount = 4;
    private const int VertexInputVectorCount =
        VertexCount * MetalRsxShaderAbi.VertexInputFloat4Count;
    private const int IndexCount = 6;
    private const int BufferAlignment = 256;

    private static readonly ushort[] Indices = [3, 0, 2, 2, 0, 1];

    private readonly MTLDevice _device;
    private readonly EmbeddedVertexConstant[] _embeddedVertexConstants;
    private readonly StaticFragmentConstantPatch[] _staticFragmentPatches;
    private readonly int _indexOffset;
    private readonly int _vertexConstantOffset;
    private readonly int _codePixelConstantOffset;
    private readonly int _staticPixelConstantOffset;
    private readonly int _staticPixelRowCount;
    private readonly int _slabByteCount;
    private MTLRenderPipelineState _pipeline;
    private MTLSamplerState _sampler;
    private MTLBuffer _slab;
    private int _hostWidth;
    private int _hostHeight;
    private bool _disposed;

    internal MetalNormalCameraPresentation(
        MTLDevice device,
        MapRenderWorldSceneSource source,
        int hostWidth,
        int hostHeight)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(source);
        if (hostWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostWidth));
        if (hostHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostHeight));

        _device = device;
        MapRenderNormalCameraMaterialAssetContract contract =
            MapRenderEditorPreviewNormalCameraRecipe.Current.PostFx;
        MapRenderNormalCameraMaterialProgramResolution resolution =
            MapRenderNormalCameraMaterialProgramResolver.ResolveExact(
                source.AssetLookup,
                source.AssetPoolRevisionAtConstruction,
                contract,
                expectedVertexInputDestinations: [0, 8],
                expectedCodePixelSourceRows: [],
                expectedVertexConstantDestinations: [0, 1, 2, 3]);
        RenderState = resolution.RenderState;
        _embeddedVertexConstants = resolution.Translation
            .EmbeddedVertexConstants.ToArray();
        _staticFragmentPatches = resolution.Translation.FragmentProgramIr
            .StaticConstantPatches.ToArray();
        _staticPixelRowCount = Math.Max(
            1,
            _staticFragmentPatches.Length == 0
                ? 0
                : checked(_staticFragmentPatches.Max(patch =>
                    patch.ArgumentOrdinal) + 1));

        _indexOffset = VertexInputVectorCount * VectorByteCount;
        _vertexConstantOffset = Align(
            checked(_indexOffset + IndexCount * sizeof(ushort)));
        _codePixelConstantOffset = Align(checked(
            _vertexConstantOffset +
            RsxVertexConstantLayout.Count * VectorByteCount));
        _staticPixelConstantOffset = Align(checked(
            _codePixelConstantOffset +
            CodeConstantLayout.Float4Count * VectorByteCount));
        _slabByteCount = checked(
            _staticPixelConstantOffset +
            _staticPixelRowCount * VectorByteCount);

        try
        {
            RsxVertexMslLoweringResult vertex =
                RsxVertexMslLowerer.Lower(
                    resolution.Translation.VertexProgramIr);
            RsxFragmentMslLoweringResult fragment =
                RsxFragmentMslLowerer.Lower(
                    resolution.Translation.FragmentProgramIr,
                    RenderState,
                    suppressShaderPackerForDiagnosticOutput: false);
            RequireExactLowering(vertex, fragment, contract.MaterialName);
            _pipeline = CompilePipeline(
                device,
                vertex.Msl!,
                fragment.Msl!,
                RenderState);
            _sampler = CreateSampler(device);
            Resize(hostWidth, hostHeight);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal RenderState RenderState { get; }

    internal void Resize(int hostWidth, int hostHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (hostWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostWidth));
        if (hostHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostHeight));
        if (_hostWidth == hostWidth && _hostHeight == hostHeight)
            return;

        MTLBuffer replacement = CreateSlab(hostWidth, hostHeight);
        MTLBuffer previous = _slab;
        _slab = replacement;
        _hostWidth = hostWidth;
        _hostHeight = hostHeight;
        if (previous.NativePtr != 0)
            previous.Dispose();
    }

    internal void Encode(
        MTLRenderCommandEncoder encoder,
        MTLTexture resolvedSceneColor,
        MetalRenderStateCache renderStates)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder.NativePtr == 0)
            throw new ArgumentException("A Metal render encoder is required.", nameof(encoder));
        if (resolvedSceneColor.NativePtr == 0)
            throw new ArgumentException("A resolved Scene texture is required.", nameof(resolvedSceneColor));
        ArgumentNullException.ThrowIfNull(renderStates);
        if (_slab.NativePtr == 0)
            throw new InvalidOperationException("Metal fullscreen resources are unavailable.");

        encoder.SetRenderPipelineState(_pipeline);
        renderStates.ApplyRasterState(encoder, RenderState);
        encoder.SetVertexBuffer(
            _slab,
            offset: 0,
            MetalRsxShaderAbi.VertexInputBufferIndex);
        encoder.SetVertexBuffer(
            _slab,
            checked((ulong)_vertexConstantOffset),
            MetalRsxShaderAbi.VertexConstantBufferIndex);
        encoder.SetFragmentBuffer(
            _slab,
            checked((ulong)_codePixelConstantOffset),
            MetalRsxShaderAbi.FragmentCodeConstantBufferIndex);
        encoder.SetFragmentBuffer(
            _slab,
            checked((ulong)_staticPixelConstantOffset),
            MetalRsxShaderAbi.FragmentStaticConstantBufferIndex);
        encoder.SetFragmentTexture(resolvedSceneColor, 0);
        encoder.SetFragmentSamplerState(_sampler, 0);
        encoder.DrawIndexedPrimitives(
            MTLPrimitiveType.Triangle,
            IndexCount,
            MTLIndexType.UInt16,
            _slab,
            checked((ulong)_indexOffset));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_slab.NativePtr != 0)
        {
            _slab.Dispose();
            _slab = default;
        }
        if (_sampler.NativePtr != 0)
        {
            _sampler.Dispose();
            _sampler = default;
        }
        if (_pipeline.NativePtr != 0)
        {
            _pipeline.Dispose();
            _pipeline = default;
        }
    }

    private MTLBuffer CreateSlab(int hostWidth, int hostHeight)
    {
        MTLBuffer buffer = _device.NewBuffer(
            checked((ulong)_slabByteCount),
            MTLResourceOptions.ResourceStorageModeShared |
            MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
        if (buffer.NativePtr == 0 || buffer.Contents == 0)
        {
            if (buffer.NativePtr != 0)
                buffer.Dispose();
            throw new InvalidOperationException(
                $"Metal failed to allocate the {_slabByteCount}-byte fullscreen slab.");
        }

        try
        {
            new Span<byte>((void*)buffer.Contents, _slabByteCount).Clear();

            Span<Vector4> inputs = new(
                (void*)buffer.Contents,
                VertexInputVectorCount);
            inputs.Fill(new Vector4(0f, 0f, 0f, 1f));
            SetVertex(inputs, 0, 0f, 0f, 0f, 0f);
            SetVertex(inputs, 1, hostWidth, 0f, 1f, 0f);
            SetVertex(inputs, 2, hostWidth, hostHeight, 1f, 1f);
            SetVertex(inputs, 3, 0f, hostHeight, 0f, 1f);

            Span<ushort> indices = new(
                (void*)(buffer.Contents + _indexOffset),
                IndexCount);
            Indices.CopyTo(indices);

            Span<Vector4> vertexConstants = new(
                (void*)(buffer.Contents + _vertexConstantOffset),
                RsxVertexConstantLayout.Count);
            foreach (EmbeddedVertexConstant embedded in
                     _embeddedVertexConstants)
            {
                if (!embedded.IsOperationallyResolved ||
                    embedded.Destination >= RsxVertexConstantLayout.Count)
                {
                    throw new InvalidOperationException(
                        $"Canonical postfx embedded vertex constant c{embedded.Destination} is unresolved.");
                }
                vertexConstants[embedded.Destination] = ToVector4(
                    embedded.Value);
            }
            vertexConstants[0] = new Vector4(
                2f / hostWidth,
                0f,
                0f,
                0f);
            vertexConstants[1] = new Vector4(
                0f,
                -2f / hostHeight,
                0f,
                0f);
            vertexConstants[2] = new Vector4(0f, 0f, 1f, 0f);
            vertexConstants[3] = new Vector4(-1f, 1f, 0f, 1f);

            Span<Vector4> staticPixelConstants = new(
                (void*)(buffer.Contents + _staticPixelConstantOffset),
                _staticPixelRowCount);
            foreach (StaticFragmentConstantPatch patch in
                     _staticFragmentPatches)
            {
                if ((uint)patch.ArgumentOrdinal >=
                    (uint)staticPixelConstants.Length)
                {
                    throw new InvalidOperationException(
                        "Canonical postfx static fragment constant is outside its exact argument slab.");
                }
                staticPixelConstants[patch.ArgumentOrdinal] = ToVector4(
                    patch.Value);
            }
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static void SetVertex(
        Span<Vector4> inputs,
        int vertexIndex,
        float x,
        float y,
        float u,
        float v)
    {
        int baseIndex = checked(
            vertexIndex * MetalRsxShaderAbi.VertexInputFloat4Count);
        inputs[baseIndex] = new Vector4(x, y, 0f, 1f);
        inputs[baseIndex + 8] = new Vector4(u, v, 0f, 1f);
    }

    private static void RequireExactLowering(
        RsxVertexMslLoweringResult vertex,
        RsxFragmentMslLoweringResult fragment,
        string materialName)
    {
        if (!vertex.IsReady || vertex.Msl is null)
        {
            throw new InvalidOperationException(
                $"Canonical fullscreen material '{materialName}' Metal vertex lowering failed: {string.Join('|', vertex.Blockers)}");
        }
        if (!fragment.IsReady || fragment.Msl is null)
        {
            throw new InvalidOperationException(
                $"Canonical fullscreen material '{materialName}' Metal fragment lowering failed: {string.Join('|', fragment.Blockers)}");
        }
        if (!fragment.SampledDestinations.SequenceEqual([0]) ||
            !fragment.ColorAttachmentIndices.SequenceEqual([0]) ||
            fragment.ExportsDepth)
        {
            throw new InvalidOperationException(
                $"Canonical fullscreen material '{materialName}' is outside the exact Metal presentation attachment ABI.");
        }
    }

    private static MTLRenderPipelineState CompilePipeline(
        MTLDevice device,
        string vertexSource,
        string fragmentSource,
        RenderState renderState)
    {
        using var options = new MTLCompileOptions();
        MTLLibrary vertexLibrary = default;
        MTLLibrary fragmentLibrary = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        try
        {
            NSError vertexError = default;
            vertexLibrary = device.NewLibrary(
                vertexSource,
                options,
                ref vertexError);
            if (vertexLibrary.NativePtr == 0 || vertexError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Canonical postfx Metal vertex compilation failed: {Describe(vertexError)}");
            }

            NSError fragmentError = default;
            fragmentLibrary = device.NewLibrary(
                fragmentSource,
                options,
                ref fragmentError);
            if (fragmentLibrary.NativePtr == 0 || fragmentError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Canonical postfx Metal fragment compilation failed: {Describe(fragmentError)}");
            }

            vertexFunction = vertexLibrary.NewFunction(
                MetalRsxShaderAbi.VertexEntryPoint);
            fragmentFunction = fragmentLibrary.NewFunction(
                MetalRsxShaderAbi.FragmentEntryPoint);
            if (vertexFunction.NativePtr == 0 || fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Canonical postfx Metal entry points are missing.");
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
                MetalRenderStateCache.ConfigureColorAttachment(
                    descriptor.ColorAttachments.Object(0),
                    renderState,
                    DrawableFormat);
                NSError pipelineError = default;
                MTLRenderPipelineState pipeline = device.NewRenderPipelineState(
                    descriptor,
                    ref pipelineError);
                if (pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
                {
                    throw new InvalidOperationException(
                        $"Canonical postfx Metal pipeline creation failed: {Describe(pipelineError)}");
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
            if (fragmentLibrary.NativePtr != 0)
                fragmentLibrary.Dispose();
            if (vertexLibrary.NativePtr != 0)
                vertexLibrary.Dispose();
        }
    }

    private static MTLSamplerState CreateSampler(MTLDevice device)
    {
        var descriptor = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            MipFilter = MTLSamplerMipFilter.NotMipmapped,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
            RAddressMode = MTLSamplerAddressMode.ClampToEdge,
            NormalizedCoordinates = true,
            MaxAnisotropy = 1,
            LodMinClamp = 0f,
            LodMaxClamp = 0f
        };
        try
        {
            MTLSamplerState sampler = device.NewSamplerState(descriptor);
            if (sampler.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal failed to create the canonical postfx sampler.");
            }
            return sampler;
        }
        finally
        {
            descriptor.Dispose();
        }
    }

    private static int Align(int value) => checked(
        (value + BufferAlignment - 1) & ~(BufferAlignment - 1));

    private static Vector4 ToVector4(ShaderConstantValue value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";
}
