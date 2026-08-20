using System.Runtime.Versioning;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Targets;

/// <summary>
/// One device-owned packed depth/stencil decision shared by every target and
/// render pipeline created by a Metal renderer. Apple GPUs expose the exact
/// D32S8 fallback when the optional D24S8 format is unavailable; keeping the
/// selection typed prevents target and pipeline descriptors from drifting.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalDepthStencilFormatSelection
{
    private MetalDepthStencilFormatSelection(MTLPixelFormat pixelFormat)
    {
        PixelFormat = pixelFormat;
    }

    internal MTLPixelFormat PixelFormat { get; }

    internal static MetalDepthStencilFormatSelection Select(
        MTLDevice device)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));

        return new MetalDepthStencilFormatSelection(
            device.IsDepth24Stencil8PixelFormatSupported
                ? MTLPixelFormat.Depth24UnormStencil8
                : MTLPixelFormat.Depth32FloatStencil8);
    }
}
