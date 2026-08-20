using System.Runtime.Versioning;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// GPU-resident geometry packed into the scene's immutable Metal buffer.
/// Vertex and index data intentionally share one allocation; their independent
/// offsets preserve the source index width without multiplying native objects.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalGeometryResource
{
    internal MetalGeometryResource(
        RenderGeometryDescriptor descriptor,
        MTLBuffer buffer,
        ulong vertexOffset,
        ulong indexOffset)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));

        Buffer = buffer;
        VertexOffset = vertexOffset;
        IndexOffset = indexOffset;
        PrimitiveType = descriptor.Topology switch
        {
            RenderPrimitiveTopology.TriangleList => MTLPrimitiveType.Triangle,
            RenderPrimitiveTopology.LineList => MTLPrimitiveType.Line,
            RenderPrimitiveTopology.TriangleStrip =>
                MTLPrimitiveType.TriangleStrip,
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.Topology,
                "Unsupported Metal primitive topology.")
        };
        IndexType = descriptor.IndexFormat switch
        {
            RenderIndexFormat.Unsigned16 => MTLIndexType.UInt16,
            RenderIndexFormat.Unsigned32 => MTLIndexType.UInt32,
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.IndexFormat,
                "Unsupported Metal index type.")
        };
    }

    internal RenderGeometryDescriptor Descriptor { get; }

    /// <summary>
    /// Borrowed scene-buffer handle. The resource cache owns its lifetime.
    /// </summary>
    internal MTLBuffer Buffer { get; }

    internal ulong VertexOffset { get; }

    internal ulong IndexOffset { get; }

    internal MTLPrimitiveType PrimitiveType { get; }

    internal MTLIndexType IndexType { get; }

    internal int IndexCount => Descriptor.IndexCount;

    internal int VertexStrideBytes => Descriptor.VertexStrideBytes;
}

/// <summary>
/// GPU-resident immutable instance records packed beside scene geometry.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalInstanceResource
{
    internal MetalInstanceResource(
        RenderInstanceDescriptor descriptor,
        MTLBuffer buffer,
        ulong offset)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));

        Buffer = buffer;
        Offset = offset;
    }

    internal RenderInstanceDescriptor Descriptor { get; }

    /// <summary>
    /// Borrowed scene-buffer handle. The resource cache owns its lifetime.
    /// </summary>
    internal MTLBuffer Buffer { get; }

    internal ulong Offset { get; }

    internal int InstanceCount => Descriptor.InstanceCount;

    internal int StrideBytes => Descriptor.StrideBytes;
}

/// <summary>
/// One immutable texture allocation and its linear/sRGB sampling views.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalTextureResource : IDisposable
{
    private MTLTexture _storage;
    private MTLTexture _linearView;
    private MTLTexture _srgbView;
    private bool _disposed;

    internal MetalTextureResource(
        RenderTextureDescriptor descriptor,
        MTLTexture storage,
        MTLTexture linearView,
        MTLTexture srgbView,
        MTLPixelFormat linearPixelFormat,
        MTLPixelFormat srgbPixelFormat,
        RenderTexturePayloadKind uploadKind,
        long uploadedByteCount)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if (storage.NativePtr == 0)
            throw new ArgumentException("Texture storage is required.", nameof(storage));
        if (linearView.NativePtr == 0)
            throw new ArgumentException("A linear texture view is required.", nameof(linearView));
        if (uploadedByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uploadedByteCount));
        }

        _storage = storage;
        _linearView = linearView;
        _srgbView = srgbView;
        LinearPixelFormat = linearPixelFormat;
        SrgbPixelFormat = srgbPixelFormat;
        UploadKind = uploadKind;
        UploadedByteCount = uploadedByteCount;
    }

    internal RenderTextureDescriptor Descriptor { get; }

    internal MTLPixelFormat LinearPixelFormat { get; }

    internal MTLPixelFormat SrgbPixelFormat { get; }

    internal RenderTexturePayloadKind UploadKind { get; }

    internal long UploadedByteCount { get; }

    /// <summary>
    /// Returns a borrowed view whose pixel format performs the requested
    /// gamma conversion. RG16F has no sRGB counterpart and always returns its
    /// linear view.
    /// </summary>
    internal MTLTexture ResolveSampledTexture(bool useSrgbReads)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return useSrgbReads && _srgbView.NativePtr != 0
            ? _srgbView
            : _linearView;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_srgbView.NativePtr != 0)
            _srgbView.Dispose();
        _srgbView = default;
        if (_linearView.NativePtr != 0)
            _linearView.Dispose();
        _linearView = default;
        if (_storage.NativePtr != 0)
            _storage.Dispose();
        _storage = default;
        _disposed = true;
    }
}

/// <summary>
/// Identity binding for an interned immutable Metal sampler state.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalSamplerResource
{
    internal MetalSamplerResource(
        RenderSamplerDescriptor descriptor,
        MTLSamplerState state)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if (state.NativePtr == 0)
            throw new ArgumentException("A Metal sampler state is required.", nameof(state));

        State = state;
    }

    internal RenderSamplerDescriptor Descriptor { get; }

    /// <summary>
    /// Borrowed sampler handle. The resource cache owns its lifetime.
    /// </summary>
    internal MTLSamplerState State { get; }

    internal bool UsesSrgbReads => (Descriptor.UseSrgbReads & 1) != 0;
}
