using System.Runtime.Versioning;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace IW4.Render.Metal.Native;

/// <summary>
/// Owns a Metal device, command queue, and <see cref="CAMetalLayer"/> installed
/// on an existing Cocoa window's content view.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MetalLayerHost : IDisposable
{
    private readonly int _ownerThreadId;
    private nint _contentView;
    private nint _previousLayer;
    private bool _previousWantsLayer;
    private bool _viewConfigurationChanged;
    private bool _layerWasInstalled;
    private MTLDevice _device;
    private MTLCommandQueue _commandQueue;
    private CAMetalLayer _layer;
    private bool _disposed;

    public MetalLayerHost(
        nint cocoaWindow,
        int drawablePixelWidth,
        int drawablePixelHeight)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The native Metal renderer requires macOS.");
        }
        if (cocoaWindow == 0)
        {
            throw new ArgumentException(
                "A Cocoa NSWindow is required.",
                nameof(cocoaWindow));
        }

        ValidateDrawableSize(drawablePixelWidth, drawablePixelHeight);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        MetalNativeFrameworks.EnsureLoaded();

        _contentView = MetalObjectiveC.GetContentView(cocoaWindow);
        if (_contentView == 0)
        {
            throw new InvalidOperationException(
                "The Cocoa NSWindow does not have a content view.");
        }

        MetalObjectiveC.Retain(_contentView);
        try
        {
            _previousWantsLayer =
                MetalObjectiveC.GetWantsLayer(_contentView);
            _previousLayer = MetalObjectiveC.GetLayer(_contentView);
            MetalObjectiveC.Retain(_previousLayer);

            _device = MTLDevice.CreateSystemDefaultDevice();
            if (_device.NativePtr == 0)
            {
                throw new PlatformNotSupportedException(
                    "Metal did not provide a system-default GPU device.");
            }

            _commandQueue = _device.NewCommandQueue();
            if (_commandQueue.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The Metal command queue could not be created.");
            }

            nint layerPointer =
                MetalObjectiveC.CreateInstance("CAMetalLayer");
            _layer = new CAMetalLayer(layerPointer)
            {
                Device = _device,
                // IW4's authored shader-packer path writes display-encoded
                // values explicitly. A linear drawable avoids applying a
                // second hardware sRGB encode during presentation.
                PixelFormat = MTLPixelFormat.BGRA8Unorm,
                FramebufferOnly = true,
                MaximumDrawableCount = 3,
                DisplaySyncEnabled = false,
                AllowsNextDrawableTimeout = true
            };
            Resize(drawablePixelWidth, drawablePixelHeight);

            MetalObjectiveC.SetWantsLayer(_contentView, true);
            _viewConfigurationChanged = true;
            MetalObjectiveC.SetLayer(_contentView, _layer.NativePtr);
            _layerWasInstalled = true;
        }
        catch
        {
            ReleaseResources(restoreView: true);
            _disposed = true;
            throw;
        }
    }

    /// <summary>
    /// Gets a borrowed wrapper for the host-owned device. The caller must not
    /// dispose the returned value.
    /// </summary>
    public MTLDevice Device
    {
        get
        {
            ThrowIfDisposed();
            return _device;
        }
    }

    /// <summary>
    /// Gets a borrowed wrapper for the host-owned command queue. The caller
    /// must not dispose the returned value.
    /// </summary>
    public MTLCommandQueue CommandQueue
    {
        get
        {
            ThrowIfDisposed();
            return _commandQueue;
        }
    }

    /// <summary>
    /// Gets a borrowed wrapper for the host-owned presentation layer. The
    /// caller must not dispose the returned value.
    /// </summary>
    public CAMetalLayer Layer
    {
        get
        {
            ThrowIfDisposed();
            return _layer;
        }
    }

    public int DrawablePixelWidth { get; private set; }

    public int DrawablePixelHeight { get; private set; }

    /// <summary>
    /// Acquires an owned drawable, or returns <see langword="null"/> when the
    /// layer cannot vend one. The caller must dispose a returned drawable.
    /// </summary>
    public CAMetalDrawable? AcquireDrawable()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        if (DrawablePixelWidth == 0 || DrawablePixelHeight == 0)
            return null;

        nint drawablePointer =
            MetalObjectiveC.GetNextDrawable(_layer.NativePtr);
        if (drawablePointer == 0)
            return null;

        MetalObjectiveC.Retain(drawablePointer);
        return new CAMetalDrawable(drawablePointer);
    }

    public void Resize(int drawablePixelWidth, int drawablePixelHeight)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ValidateDrawableSize(drawablePixelWidth, drawablePixelHeight);
        if (drawablePixelWidth == DrawablePixelWidth &&
            drawablePixelHeight == DrawablePixelHeight)
        {
            return;
        }

        MetalObjectiveC.SetDrawableSize(
            _layer.NativePtr,
            drawablePixelWidth,
            drawablePixelHeight);
        DrawablePixelWidth = drawablePixelWidth;
        DrawablePixelHeight = drawablePixelHeight;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;

        ReleaseResources(restoreView: true);
        _disposed = true;
    }

    private static void ValidateDrawableSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
    }

    private void ReleaseResources(bool restoreView)
    {
        if (restoreView && _viewConfigurationChanged && _contentView != 0)
        {
            nint currentLayer = MetalObjectiveC.GetLayer(_contentView);
            if (!_layerWasInstalled || currentLayer == _layer.NativePtr)
            {
                MetalObjectiveC.SetLayer(_contentView, _previousLayer);
                MetalObjectiveC.SetWantsLayer(
                    _contentView,
                    _previousWantsLayer);
            }
        }

        MetalObjectiveC.Release(_layer.NativePtr);
        _layer = default;
        MetalObjectiveC.Release(_commandQueue.NativePtr);
        _commandQueue = default;
        MetalObjectiveC.Release(_device.NativePtr);
        _device = default;
        MetalObjectiveC.Release(_previousLayer);
        _previousLayer = 0;
        MetalObjectiveC.Release(_contentView);
        _contentView = 0;
        _viewConfigurationChanged = false;
        _layerWasInstalled = false;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The Metal layer host may only be used on its owning " +
                "UI/render thread.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
