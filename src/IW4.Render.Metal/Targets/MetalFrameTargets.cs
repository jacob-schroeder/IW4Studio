using System.Runtime.Versioning;

using IW4.Render.Execution;
using IW4.Render.Shaders;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Targets;

/// <summary>
/// Scene-lifetime owner for the normal-camera target-2 attachments and its
/// single-sample resolved color. Target recreation is resize-only; ordinary
/// frames only allocate their small render-pass descriptor.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalFrameTargets : IDisposable
{
    internal const ulong SceneSampleCount = 2;
    internal const MTLPixelFormat SceneColorFormat = MTLPixelFormat.RGBA8Unorm;

    internal static FragmentTargetOutputAvailability SceneTargetOutputs
        { get; } =
        TranslatedProgramCapability.CreateSurfaceAOutputAvailability();

    private readonly MTLDevice _device;
    private readonly MTLPixelFormat _depthStencilFormat;
    private MTLTexture _multisampleColor;
    private MTLTexture _resolvedColor;
    private MTLTexture _depthStencil;
    private int _width;
    private int _height;
    private bool _disposed;

    internal MetalFrameTargets(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);
        if (!device.SupportsTextureSampleCount(SceneSampleCount))
        {
            throw new NotSupportedException(
                "The selected Metal device does not support the IW4 two-sample Scene target.");
        }
        _device = device;
        _depthStencilFormat = depthStencilFormat.PixelFormat;
    }

    internal int Width => _width;
    internal int Height => _height;
    internal bool IsReady =>
        _width > 0 &&
        _height > 0 &&
        _multisampleColor.NativePtr != 0 &&
        _resolvedColor.NativePtr != 0 &&
        _depthStencil.NativePtr != 0;
    internal MTLTexture ResolvedColor => IsReady
        ? _resolvedColor
        : throw new InvalidOperationException("Metal frame targets are unavailable.");
    internal void Resize(int width, int height)
    {
        ThrowIfDisposed();
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (_width == width && _height == height)
            return;

        DeleteTextures();
        _width = width;
        _height = height;
        if (width == 0 || height == 0)
            return;

        _multisampleColor = CreateTexture(
            SceneColorFormat,
            MTLTextureType.Type2DMultisample,
            width,
            height,
            SceneSampleCount,
            MTLTextureUsage.RenderTarget);
        _resolvedColor = CreateTexture(
            SceneColorFormat,
            MTLTextureType.Type2D,
            width,
            height,
            sampleCount: 1,
            MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead);
        _depthStencil = CreateTexture(
            _depthStencilFormat,
            MTLTextureType.Type2DMultisample,
            width,
            height,
            SceneSampleCount,
            MTLTextureUsage.RenderTarget);
    }

    internal MTLRenderPassDescriptor CreateScenePass(
        double clearRed,
        double clearGreen,
        double clearBlue,
        double clearAlpha)
    {
        ThrowIfDisposed();
        if (!IsReady)
            throw new InvalidOperationException("Metal frame targets are unavailable.");

        var descriptor = new MTLRenderPassDescriptor
        {
            RenderTargetWidth = checked((ulong)_width),
            RenderTargetHeight = checked((ulong)_height),
            DefaultRasterSampleCount = SceneSampleCount
        };
        MTLRenderPassColorAttachmentDescriptor color =
            descriptor.ColorAttachments.Object(0);
        color.Texture = _multisampleColor;
        color.ResolveTexture = _resolvedColor;
        color.LoadAction = MTLLoadAction.Clear;
        color.StoreAction = MTLStoreAction.MultisampleResolve;
        color.ClearColor = new MTLClearColor
        {
            red = clearRed,
            green = clearGreen,
            blue = clearBlue,
            alpha = clearAlpha
        };

        MTLRenderPassDepthAttachmentDescriptor depth = descriptor.DepthAttachment;
        depth.Texture = _depthStencil;
        depth.LoadAction = MTLLoadAction.Clear;
        depth.StoreAction = MTLStoreAction.DontCare;
        depth.ClearDepth = 1.0;

        MTLRenderPassStencilAttachmentDescriptor stencil =
            descriptor.StencilAttachment;
        stencil.Texture = _depthStencil;
        stencil.LoadAction = MTLLoadAction.Clear;
        stencil.StoreAction = MTLStoreAction.DontCare;
        stencil.ClearStencil = 0;
        return descriptor;
    }

    internal static MTLRenderPassDescriptor CreatePresentationPass(
        MTLTexture drawableTexture)
    {
        if (drawableTexture.NativePtr == 0)
            throw new ArgumentException("A drawable texture is required.", nameof(drawableTexture));
        var descriptor = new MTLRenderPassDescriptor
        {
            RenderTargetWidth = drawableTexture.Width,
            RenderTargetHeight = drawableTexture.Height,
            DefaultRasterSampleCount = 1
        };
        MTLRenderPassColorAttachmentDescriptor color =
            descriptor.ColorAttachments.Object(0);
        color.Texture = drawableTexture;
        color.LoadAction = MTLLoadAction.DontCare;
        color.StoreAction = MTLStoreAction.Store;
        return descriptor;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DeleteTextures();
        _width = 0;
        _height = 0;
    }

    private MTLTexture CreateTexture(
        MTLPixelFormat format,
        MTLTextureType type,
        int width,
        int height,
        ulong sampleCount,
        MTLTextureUsage usage)
    {
        var descriptor = new MTLTextureDescriptor
        {
            TextureType = type,
            PixelFormat = format,
            Width = checked((ulong)width),
            Height = checked((ulong)height),
            Depth = 1,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = sampleCount,
            StorageMode = MTLStorageMode.Private,
            Usage = usage
        };
        try
        {
            MTLTexture texture = _device.NewTexture(descriptor);
            if (texture.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"Metal failed to create a {format} {width}x{height} target.");
            }
            return texture;
        }
        finally
        {
            descriptor.Dispose();
        }
    }

    private void DeleteTextures()
    {
        if (_depthStencil.NativePtr != 0)
        {
            _depthStencil.Dispose();
            _depthStencil = default;
        }
        if (_resolvedColor.NativePtr != 0)
        {
            _resolvedColor.Dispose();
            _resolvedColor = default;
        }
        if (_multisampleColor.NativePtr != 0)
        {
            _multisampleColor.Dispose();
            _multisampleColor = default;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
