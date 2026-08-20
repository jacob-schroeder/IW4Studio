using System.Runtime.Versioning;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Geometry identity installed into one scene-lifetime immutable Metal buffer.
/// Vertex and index data intentionally share one allocation; their independent
/// offsets preserve the source index width without multiplying native objects.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalGeometryResource
{
    private MTLBuffer _buffer;
    private ulong _vertexOffset;
    private ulong _indexOffset;

    internal MetalGeometryResource(RenderGeometryDescriptor descriptor)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        (PrimitiveType, IndexType) = ResolveNativeShape(descriptor);
    }

    internal MetalGeometryResource(
        RenderGeometryDescriptor descriptor,
        MTLBuffer buffer,
        ulong vertexOffset,
        ulong indexOffset)
        : this(descriptor)
    {
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));
        Install(buffer, vertexOffset, indexOffset);
    }

    private static (MTLPrimitiveType Primitive, MTLIndexType Index)
        ResolveNativeShape(RenderGeometryDescriptor descriptor) =>
        (descriptor.Topology switch
        {
            RenderPrimitiveTopology.TriangleList => MTLPrimitiveType.Triangle,
            RenderPrimitiveTopology.LineList => MTLPrimitiveType.Line,
            RenderPrimitiveTopology.TriangleStrip =>
                MTLPrimitiveType.TriangleStrip,
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.Topology,
                "Unsupported Metal primitive topology.")
        }, descriptor.IndexFormat switch
        {
            RenderIndexFormat.Unsigned16 => MTLIndexType.UInt16,
            RenderIndexFormat.Unsigned32 => MTLIndexType.UInt32,
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.IndexFormat,
                "Unsupported Metal index type.")
        });

    internal void Install(
        MTLBuffer buffer,
        ulong vertexOffset,
        ulong indexOffset)
    {
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));
        if (_buffer.NativePtr != 0)
        {
            throw new InvalidOperationException(
                $"Metal geometry {Descriptor.Identity} is already resident.");
        }
        _buffer = buffer;
        _vertexOffset = vertexOffset;
        _indexOffset = indexOffset;
    }

    internal RenderGeometryDescriptor Descriptor { get; }

    /// <summary>
    /// Borrowed scene-buffer handle. The resource cache owns its lifetime.
    /// </summary>
    internal MTLBuffer Buffer => _buffer.NativePtr != 0
        ? _buffer
        : throw new InvalidOperationException(
            $"Metal geometry {Descriptor.Identity} is not resident.");

    internal ulong VertexOffset => _vertexOffset;

    internal ulong IndexOffset => _indexOffset;

    internal bool IsResident => _buffer.NativePtr != 0;

    internal MTLPrimitiveType PrimitiveType { get; }

    internal MTLIndexType IndexType { get; }

    internal int IndexCount => Descriptor.IndexCount;

    internal int VertexStrideBytes => Descriptor.VertexStrideBytes;
}

/// <summary>
/// Instance identity installed into one scene-lifetime immutable Metal buffer.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalInstanceResource
{
    private MTLBuffer _buffer;
    private ulong _offset;

    internal MetalInstanceResource(RenderInstanceDescriptor descriptor)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
    }

    internal MetalInstanceResource(
        RenderInstanceDescriptor descriptor,
        MTLBuffer buffer,
        ulong offset)
        : this(descriptor)
    {
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));
        Install(buffer, offset);
    }

    internal void Install(MTLBuffer buffer, ulong offset)
    {
        if (buffer.NativePtr == 0)
            throw new ArgumentException("A Metal buffer is required.", nameof(buffer));
        if (_buffer.NativePtr != 0)
        {
            throw new InvalidOperationException(
                $"Metal instances {Descriptor.Identity} are already resident.");
        }
        _buffer = buffer;
        _offset = offset;
    }

    internal RenderInstanceDescriptor Descriptor { get; }

    /// <summary>
    /// Borrowed scene-buffer handle. The resource cache owns its lifetime.
    /// </summary>
    internal MTLBuffer Buffer => _buffer.NativePtr != 0
        ? _buffer
        : throw new InvalidOperationException(
            $"Metal instances {Descriptor.Identity} are not resident.");

    internal ulong Offset => _offset;

    internal bool IsResident => _buffer.NativePtr != 0;

    internal int InstanceCount => Descriptor.InstanceCount;

    internal int StrideBytes => Descriptor.StrideBytes;
}

/// <summary>
/// Stable texture identity with permanent one-pixel fallback views and an
/// evictable full-resolution Metal allocation.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalTextureResource : IDisposable
{
    private MTLTexture _fallbackStorage;
    private MTLTexture _fallbackLinearView;
    private MTLTexture _fallbackSrgbView;
    private MTLTexture _residentStorage;
    private MTLTexture _residentLinearView;
    private MTLTexture _residentSrgbView;
    private bool _disposed;

    internal MetalTextureResource(
        RenderTextureDescriptor descriptor,
        MTLTexture fallbackStorage,
        MTLTexture fallbackLinearView,
        MTLTexture fallbackSrgbView,
        MTLPixelFormat linearPixelFormat,
        MTLPixelFormat srgbPixelFormat,
        RenderTexturePayloadKind uploadKind,
        long uploadedByteCount,
        long creationOrdinal)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if (fallbackStorage.NativePtr == 0)
        {
            throw new ArgumentException(
                "Fallback texture storage is required.",
                nameof(fallbackStorage));
        }
        if (fallbackLinearView.NativePtr == 0)
        {
            throw new ArgumentException(
                "A fallback linear texture view is required.",
                nameof(fallbackLinearView));
        }
        if (uploadedByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uploadedByteCount));
        }
        if (creationOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(creationOrdinal));

        _fallbackStorage = fallbackStorage;
        _fallbackLinearView = fallbackLinearView;
        _fallbackSrgbView = fallbackSrgbView;
        LinearPixelFormat = linearPixelFormat;
        SrgbPixelFormat = srgbPixelFormat;
        UploadKind = uploadKind;
        UploadedByteCount = uploadedByteCount;
        CreationOrdinal = creationOrdinal;
    }

    internal RenderTextureDescriptor Descriptor { get; }

    internal MTLPixelFormat LinearPixelFormat { get; }

    internal MTLPixelFormat SrgbPixelFormat { get; }

    internal RenderTexturePayloadKind UploadKind { get; }

    internal long UploadedByteCount { get; }

    internal long CreationOrdinal { get; }

    internal long LastVisibleFrame { get; private set; } = -1;

    internal long LastResidentFrame { get; private set; } = -1;

    internal bool IsResident => _residentStorage.NativePtr != 0;

    internal long ResidentByteCount => IsResident ? UploadedByteCount : 0;

    internal void MarkVisible(long frameIndex)
    {
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        LastVisibleFrame = frameIndex;
    }

    internal void InstallResidentAllocation(
        MTLTexture storage,
        MTLTexture linearView,
        MTLTexture srgbView,
        long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (storage.NativePtr == 0 || linearView.NativePtr == 0)
        {
            throw new ArgumentException(
                "A complete resident Metal texture allocation is required.",
                nameof(storage));
        }
        if (frameIndex < -1)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        ReleaseResidentAllocation();
        _residentStorage = storage;
        _residentLinearView = linearView;
        _residentSrgbView = srgbView;
        LastResidentFrame = frameIndex;
    }

    internal void Evict()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReleaseResidentAllocation();
    }

    /// <summary>
    /// Returns a borrowed view whose pixel format performs the requested
    /// gamma conversion. RG16F has no sRGB counterpart and always returns its
    /// linear view.
    /// </summary>
    internal MTLTexture ResolveSampledTexture(bool useSrgbReads)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsResident)
        {
            return useSrgbReads && _residentSrgbView.NativePtr != 0
                ? _residentSrgbView
                : _residentLinearView;
        }
        return useSrgbReads && _fallbackSrgbView.NativePtr != 0
            ? _fallbackSrgbView
            : _fallbackLinearView;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseResidentAllocation();
        if (_fallbackSrgbView.NativePtr != 0)
            _fallbackSrgbView.Dispose();
        _fallbackSrgbView = default;
        if (_fallbackLinearView.NativePtr != 0)
            _fallbackLinearView.Dispose();
        _fallbackLinearView = default;
        if (_fallbackStorage.NativePtr != 0)
            _fallbackStorage.Dispose();
        _fallbackStorage = default;
        _disposed = true;
    }

    private void ReleaseResidentAllocation()
    {
        if (_residentSrgbView.NativePtr != 0)
            _residentSrgbView.Dispose();
        _residentSrgbView = default;
        if (_residentLinearView.NativePtr != 0)
            _residentLinearView.Dispose();
        _residentLinearView = default;
        if (_residentStorage.NativePtr != 0)
            _residentStorage.Dispose();
        _residentStorage = default;
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
