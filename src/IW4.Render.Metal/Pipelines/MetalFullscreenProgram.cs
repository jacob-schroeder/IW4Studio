using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Execution;
using IW4.Render.Metal.Shaders;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// One immutable authored fullscreen pipeline shared by every concrete draw
/// of that material. Per-draw slabs remain separate because one glow chain can
/// execute the same tap-count program more than once with different rows.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalFullscreenProgram : IDisposable
{
    private readonly MTLDevice _device;
    private readonly EmbeddedVertexConstant[] _embeddedVertexConstants;
    private readonly StaticFragmentConstantPatch[] _staticFragmentPatches;
    private readonly HashSet<ushort> _codePixelSourceRows;
    private MTLRenderPipelineState _pipeline;
    private bool _disposed;

    internal MetalFullscreenProgram(
        MTLDevice device,
        string materialName,
        MapRenderNormalCameraMaterialProgramResolution resolution,
        IReadOnlyList<ushort> codePixelSourceRows,
        MTLPixelFormat targetFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(codePixelSourceRows);
        _device = device;
        MaterialName = materialName;
        RenderState = resolution.RenderState;
        _embeddedVertexConstants = resolution.Translation
            .EmbeddedVertexConstants.ToArray();
        _staticFragmentPatches = resolution.Translation.FragmentProgramIr
            .StaticConstantPatches.ToArray();
        _codePixelSourceRows = codePixelSourceRows.ToHashSet();
        StaticPixelRowCount = Math.Max(
            1,
            _staticFragmentPatches.Length == 0
                ? 0
                : checked(_staticFragmentPatches.Max(patch =>
                    patch.ArgumentOrdinal) + 1));
        _pipeline = CompilePipeline(
            device,
            materialName,
            resolution,
            targetFormat);
    }

    internal string MaterialName { get; }

    internal RenderState RenderState { get; }

    internal int StaticPixelRowCount { get; }

    internal IReadOnlyList<EmbeddedVertexConstant>
        EmbeddedVertexConstants => _embeddedVertexConstants;

    internal IReadOnlyList<StaticFragmentConstantPatch>
        StaticFragmentPatches => _staticFragmentPatches;

    internal MTLRenderPipelineState Pipeline =>
        !_disposed && _pipeline.NativePtr != 0
            ? _pipeline
            : throw new ObjectDisposedException(nameof(MetalFullscreenProgram));

    internal bool UsesCodePixelConstant(ushort sourceRow) =>
        _codePixelSourceRows.Contains(sourceRow);

    internal MetalFullscreenDraw CreateDraw(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MetalFullscreenDraw(_device, this, width, height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_pipeline.NativePtr != 0)
        {
            _pipeline.Dispose();
            _pipeline = default;
        }
    }

    private static MTLRenderPipelineState CompilePipeline(
        MTLDevice device,
        string materialName,
        MapRenderNormalCameraMaterialProgramResolution resolution,
        MTLPixelFormat targetFormat)
    {
        RsxVertexMslLoweringResult vertex =
            RsxVertexMslLowerer.Lower(
                resolution.Translation.VertexProgramIr);
        RsxFragmentMslLoweringResult fragment =
            RsxFragmentMslLowerer.Lower(
                resolution.Translation.FragmentProgramIr,
                resolution.RenderState,
                suppressShaderPackerForDiagnosticOutput: false,
                targetOutputs: TranslatedProgramCapability
                    .CreateSurfaceAOutputAvailability());
        RequireExactLowering(vertex, fragment, materialName);
        string vertexMsl = vertex.Msl ?? throw new InvalidOperationException(
            $"Fullscreen material '{materialName}' has no lowered Metal vertex source.");
        string fragmentMsl = fragment.Msl ??
            throw new InvalidOperationException(
                $"Fullscreen material '{materialName}' has no lowered Metal fragment source.");

        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        MTLLibrary vertexLibrary = default;
        MTLLibrary fragmentLibrary = default;
        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        try
        {
            NSError vertexError = default;
            vertexLibrary = device.NewLibrary(
                vertexMsl,
                options,
                ref vertexError);
            if (vertexLibrary.NativePtr == 0 || vertexError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Fullscreen material '{materialName}' Metal vertex compilation failed: {Describe(vertexError)}");
            }
            NSError fragmentError = default;
            fragmentLibrary = device.NewLibrary(
                fragmentMsl,
                options,
                ref fragmentError);
            if (fragmentLibrary.NativePtr == 0 ||
                fragmentError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Fullscreen material '{materialName}' Metal fragment compilation failed: {Describe(fragmentError)}");
            }

            vertexFunction = vertexLibrary.NewFunction(
                MetalRsxShaderAbi.VertexEntryPoint);
            fragmentFunction = fragmentLibrary.NewFunction(
                MetalRsxShaderAbi.FragmentEntryPoint);
            if (vertexFunction.NativePtr == 0 ||
                fragmentFunction.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"Fullscreen material '{materialName}' Metal entry points are missing.");
            }

            using var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertexFunction,
                FragmentFunction = fragmentFunction,
                RasterSampleCount = 1,
                InputPrimitiveTopology =
                    MTLPrimitiveTopologyClass.Triangle
            };
            MetalRenderStateCache.ConfigureColorAttachment(
                descriptor.ColorAttachments.Object(0),
                resolution.RenderState,
                targetFormat);
            NSError pipelineError = default;
            MTLRenderPipelineState pipeline =
                device.NewRenderPipelineState(
                    descriptor,
                    ref pipelineError);
            if (pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
            {
                throw new InvalidOperationException(
                    $"Fullscreen material '{materialName}' Metal pipeline creation failed: {Describe(pipelineError)}");
            }
            return pipeline;
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

    private static void RequireExactLowering(
        RsxVertexMslLoweringResult vertex,
        RsxFragmentMslLoweringResult fragment,
        string materialName)
    {
        if (!vertex.IsReady || vertex.Msl is null)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{materialName}' Metal vertex lowering failed: {string.Join('|', vertex.Blockers)}");
        }
        if (!fragment.IsReady || fragment.Msl is null)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{materialName}' Metal fragment lowering failed: {string.Join('|', fragment.Blockers)}");
        }
        if (!fragment.SampledDestinations.SequenceEqual([0]) ||
            !fragment.ColorAttachmentIndices.SequenceEqual([0]) ||
            fragment.ExportsDepth)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{materialName}' is outside the exact Metal post attachment ABI.");
        }
    }

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ??
              "unknown Metal error";
}

[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalFullscreenDraw : IDisposable
{
    private const int VectorByteCount = sizeof(float) * 4;
    private const int VertexCount = 4;
    private const int VertexInputVectorCount =
        VertexCount * MetalRsxShaderAbi.VertexInputFloat4Count;
    private const int IndexCount = 6;
    private const int BufferAlignment = 256;

    private static readonly ushort[] Indices = [3, 0, 2, 2, 0, 1];

    private readonly MetalFullscreenProgram _program;
    private readonly int _indexOffset;
    private readonly int _vertexConstantOffset;
    private readonly int _codePixelConstantOffset;
    private readonly int _staticPixelConstantOffset;
    private readonly int _slabByteCount;
    private MTLBuffer _slab;
    private bool _disposed;

    internal MetalFullscreenDraw(
        MTLDevice device,
        MetalFullscreenProgram program,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        _program = program;
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
            program.StaticPixelRowCount * VectorByteCount);
        _slab = CreateSlab(device, width, height);
    }

    internal string MaterialName => _program.MaterialName;

    internal void SetVertexConstant(int destination, Vector4 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)destination >= RsxVertexConstantLayout.Count)
            throw new ArgumentOutOfRangeException(nameof(destination));
        VertexConstants[destination] = value;
    }

    internal void SetCodePixelConstant(ushort sourceRow, Vector4 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_program.UsesCodePixelConstant(sourceRow))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{MaterialName}' does not consume direct row 0x{sourceRow:X2}.");
        }
        if (sourceRow >= CodeConstantLayout.Float4Count)
            throw new ArgumentOutOfRangeException(nameof(sourceRow));
        CodePixelConstants[sourceRow] = value;
    }

    internal void Encode(
        MTLRenderCommandEncoder encoder,
        MTLTexture source,
        MTLSamplerState sampler,
        MetalRenderStateCache renderStates)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        encoder.SetRenderPipelineState(_program.Pipeline);
        renderStates.ApplyRasterState(
            encoder,
            _program.RenderState);
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
        encoder.SetFragmentTexture(source, 0);
        encoder.SetFragmentSamplerState(sampler, 0);
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
    }

    private Span<Vector4> VertexConstants => new(
        (void*)(_slab.Contents + _vertexConstantOffset),
        RsxVertexConstantLayout.Count);

    private Span<Vector4> CodePixelConstants => new(
        (void*)(_slab.Contents + _codePixelConstantOffset),
        CodeConstantLayout.Float4Count);

    private MTLBuffer CreateSlab(
        MTLDevice device,
        int width,
        int height)
    {
        MTLBuffer buffer = device.NewBuffer(
            checked((ulong)_slabByteCount),
            MTLResourceOptions.ResourceStorageModeShared |
            MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
        if (buffer.NativePtr == 0 || buffer.Contents == 0)
        {
            if (buffer.NativePtr != 0)
                buffer.Dispose();
            throw new InvalidOperationException(
                $"Metal failed to allocate the {_slabByteCount}-byte {MaterialName} fullscreen slab.");
        }

        try
        {
            new Span<byte>((void*)buffer.Contents, _slabByteCount).Clear();
            Span<Vector4> inputs = new(
                (void*)buffer.Contents,
                VertexInputVectorCount);
            inputs.Fill(new Vector4(0f, 0f, 0f, 1f));
            SetVertex(inputs, 0, 0f, 0f, 0f, 0f);
            SetVertex(inputs, 1, width, 0f, 1f, 0f);
            SetVertex(inputs, 2, width, height, 1f, 1f);
            SetVertex(inputs, 3, 0f, height, 0f, 1f);

            Span<ushort> indices = new(
                (void*)(buffer.Contents + _indexOffset),
                IndexCount);
            Indices.CopyTo(indices);

            Span<Vector4> vertexConstants = new(
                (void*)(buffer.Contents + _vertexConstantOffset),
                RsxVertexConstantLayout.Count);
            foreach (EmbeddedVertexConstant embedded in
                     _program.EmbeddedVertexConstants)
            {
                if (!embedded.IsOperationallyResolved ||
                    embedded.Destination >= vertexConstants.Length)
                {
                    throw new InvalidOperationException(
                        $"Fullscreen material '{MaterialName}' embedded vertex constant c{embedded.Destination} is unresolved.");
                }
                vertexConstants[embedded.Destination] = ToVector4(
                    embedded.Value);
            }
            vertexConstants[0] = new Vector4(
                2f / width,
                0f,
                0f,
                0f);
            vertexConstants[1] = new Vector4(
                0f,
                -2f / height,
                0f,
                0f);
            vertexConstants[2] = new Vector4(0f, 0f, 1f, 0f);
            vertexConstants[3] = new Vector4(-1f, 1f, 0f, 1f);

            Span<Vector4> staticPixels = new(
                (void*)(buffer.Contents + _staticPixelConstantOffset),
                _program.StaticPixelRowCount);
            foreach (StaticFragmentConstantPatch patch in
                     _program.StaticFragmentPatches)
            {
                if ((uint)patch.ArgumentOrdinal >=
                    (uint)staticPixels.Length)
                {
                    throw new InvalidOperationException(
                        $"Fullscreen material '{MaterialName}' static fragment constant is outside its exact argument slab.");
                }
                staticPixels[patch.ArgumentOrdinal] = ToVector4(
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

    private static int Align(int value) => checked(
        (value + BufferAlignment - 1) & ~(BufferAlignment - 1));

    private static Vector4 ToVector4(ShaderConstantValue value) =>
        new(value.X, value.Y, value.Z, value.W);
}
