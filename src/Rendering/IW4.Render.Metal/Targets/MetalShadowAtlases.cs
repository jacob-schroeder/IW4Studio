using System.Runtime.Versioning;

using IW4.Render.Scheduling.Shadows;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Targets;

/// <summary>
/// Persistent native depth storage for IW4's directional and local-light
/// shadow targets. The two atlases deliberately retain the PS3 tile shapes;
/// no resize or editor-window extent participates in their lifetime.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalShadowAtlases : IDisposable
{
    internal const int SunWidth = 1024;
    internal const int SunHeight = 2048;
    internal const int SunPartitionSize = 1024;
    internal const int SunPartitionCount = 2;

    internal const int SpotWidth = MapRenderSpotShadowAtlasLayout.Width;
    internal const int SpotHeight = MapRenderSpotShadowAtlasLayout.Height;
    internal const int SpotTileSize = MapRenderSpotShadowAtlasLayout.TileSize;
    internal const int SpotTileCount =
        MapRenderSpotShadowAtlasLayout.MaximumEntryCount;

    private MTLTexture _sunDepthStencil;
    private MTLTexture _spotDepthStencil;
    private MTLSamplerState _sunComparisonSampler;
    private MTLSamplerState _spotComparisonSampler;
    private bool _disposed;

    internal MetalShadowAtlases(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);

        try
        {
            _sunDepthStencil = CreateAtlasTexture(
                device,
                depthStencilFormat.PixelFormat,
                SunWidth,
                SunHeight,
                "sun-shadow");
            _spotDepthStencil = CreateAtlasTexture(
                device,
                depthStencilFormat.PixelFormat,
                SpotWidth,
                SpotHeight,
                "spot-shadow");
            _sunComparisonSampler = CreateComparisonSampler(
                device,
                "sun-shadow");
            _spotComparisonSampler = CreateComparisonSampler(
                device,
                "spot-shadow");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal MTLTexture SunDepthStencil => Require(
        _sunDepthStencil,
        "sun-shadow atlas");

    internal MTLTexture SpotDepthStencil => Require(
        _spotDepthStencil,
        "spot-shadow atlas");

    internal MTLSamplerState SunComparisonSampler => Require(
        _sunComparisonSampler,
        "sun-shadow comparison sampler");

    internal MTLSamplerState SpotComparisonSampler => Require(
        _spotComparisonSampler,
        "spot-shadow comparison sampler");

    internal MTLRenderPassDescriptor CreateSunPass() =>
        CreateDepthPass(
            SunDepthStencil,
            SunWidth,
            SunHeight);

    internal MTLRenderPassDescriptor CreateSpotPass() =>
        CreateDepthPass(
            SpotDepthStencil,
            SpotWidth,
            SpotHeight);

    internal static MetalShadowAtlasTile GetSunPartition(
        int partitionIndex)
    {
        if ((uint)partitionIndex >= SunPartitionCount)
            throw new ArgumentOutOfRangeException(nameof(partitionIndex));
        return new(
            X: 0,
            Y: checked(partitionIndex * SunPartitionSize),
            Width: SunPartitionSize,
            Height: SunPartitionSize);
    }

    internal static MetalShadowAtlasTile GetSpotTile(int tileIndex)
    {
        if ((uint)tileIndex >= SpotTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        return new(
            X: 0,
            Y: checked(tileIndex * SpotTileSize),
            Width: SpotTileSize,
            Height: SpotTileSize);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(ref _spotComparisonSampler);
        Dispose(ref _sunComparisonSampler);
        Dispose(ref _spotDepthStencil);
        Dispose(ref _sunDepthStencil);
    }

    private static MTLTexture CreateAtlasTexture(
        MTLDevice device,
        MTLPixelFormat depthStencilFormat,
        int width,
        int height,
        string role)
    {
        using var descriptor = new MTLTextureDescriptor
        {
            TextureType = MTLTextureType.Type2D,
            PixelFormat = depthStencilFormat,
            Width = checked((ulong)width),
            Height = checked((ulong)height),
            Depth = 1,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = 1,
            StorageMode = MTLStorageMode.Private,
            Usage = MTLTextureUsage.RenderTarget |
                MTLTextureUsage.ShaderRead
        };
        MTLTexture texture = device.NewTexture(descriptor);
        if (texture.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal failed to create the {width}x{height} " +
                $"{depthStencilFormat} {role} atlas.");
        }
        return texture;
    }

    private static MTLSamplerState CreateComparisonSampler(
        MTLDevice device,
        string role)
    {
        using var descriptor = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            MipFilter = MTLSamplerMipFilter.NotMipmapped,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
            RAddressMode = MTLSamplerAddressMode.ClampToEdge,
            NormalizedCoordinates = true,
            CompareFunction = MTLCompareFunction.Less
        };
        MTLSamplerState sampler = device.NewSamplerState(descriptor);
        if (sampler.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal failed to create the {role} comparison sampler.");
        }
        return sampler;
    }

    private static MTLRenderPassDescriptor CreateDepthPass(
        MTLTexture depthStencil,
        int width,
        int height)
    {
        var descriptor = new MTLRenderPassDescriptor
        {
            RenderTargetWidth = checked((ulong)width),
            RenderTargetHeight = checked((ulong)height),
            DefaultRasterSampleCount = 1
        };
        MTLRenderPassDepthAttachmentDescriptor depth =
            descriptor.DepthAttachment;
        depth.Texture = depthStencil;
        depth.LoadAction = MTLLoadAction.Clear;
        depth.StoreAction = MTLStoreAction.Store;
        depth.ClearDepth = 1.0;

        MTLRenderPassStencilAttachmentDescriptor stencil =
            descriptor.StencilAttachment;
        stencil.Texture = depthStencil;
        stencil.LoadAction = MTLLoadAction.Clear;
        stencil.StoreAction = MTLStoreAction.DontCare;
        stencil.ClearStencil = 0;
        return descriptor;
    }

    private static MTLTexture Require(MTLTexture texture, string role) =>
        texture.NativePtr != 0
            ? texture
            : throw new ObjectDisposedException(role);

    private static MTLSamplerState Require(
        MTLSamplerState sampler,
        string role) => sampler.NativePtr != 0
            ? sampler
            : throw new ObjectDisposedException(role);

    private static void Dispose(ref MTLTexture texture)
    {
        if (texture.NativePtr == 0)
            return;
        texture.Dispose();
        texture = default;
    }

    private static void Dispose(ref MTLSamplerState sampler)
    {
        if (sampler.NativePtr == 0)
            return;
        sampler.Dispose();
        sampler = default;
    }
}

internal readonly record struct MetalShadowAtlasTile(
    int X,
    int Y,
    int Width,
    int Height);
