using System.Runtime.Versioning;

using IW4.Render.Lighting;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Triple-buffered native projection of the mutable PS3 static-model
/// lighting cache. Each texture keeps its own dirty-entry set so an atlas
/// assignment can be published without writing a texture still sampled by an
/// earlier command buffer.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalStaticModelLightingResources : IDisposable
{
    internal const int FrameSlotCount = 3;

    private const int FullUploadThreshold = 256;

    private readonly MapRenderStaticModelLightingAtlas _atlas;
    private readonly MTLTexture[] _textures =
        new MTLTexture[FrameSlotCount];
    private readonly bool[][] _dirtyEntriesByFrameSlot =
        new bool[FrameSlotCount][];
    private readonly int[] _dirtyEntryCountByFrameSlot =
        new int[FrameSlotCount];
    private readonly int[] _objectByEntry = new int[
        MapRenderStaticModelLightingAtlas.StaticEntryCapacity];
    private readonly byte[] _physicalRgbaBytes;
    private MTLSamplerState _sampler;
    private bool _disposed;

    internal MetalStaticModelLightingResources(
        MTLDevice device,
        MapRenderStaticModelLightingAtlas atlas)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        _physicalRgbaBytes = atlas.RgbaBytes.ToArray();
        Array.Fill(_objectByEntry, -1);
        for (int index = 0;
             index < _dirtyEntriesByFrameSlot.Length;
             index++)
        {
            _dirtyEntriesByFrameSlot[index] = new bool[
                MapRenderStaticModelLightingAtlas.StaticEntryCapacity];
        }

        try
        {
            for (int index = 0; index < _textures.Length; index++)
                _textures[index] = CreateTexture(device, _physicalRgbaBytes);
            _sampler = CreateSampler(device);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal MTLSamplerState Sampler
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sampler.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "The Metal static-model lighting sampler is unavailable.");
            }
            return _sampler;
        }
    }

    internal void ApplyAssignments(
        ReadOnlySpan<MapRenderStaticModelLightingAssignment> assignments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int assignmentIndex = 0;
             assignmentIndex < assignments.Length;
             assignmentIndex++)
        {
            MapRenderStaticModelLightingAssignment assignment =
                assignments[assignmentIndex];
            if ((uint)assignment.EntryIndex >=
                MapRenderStaticModelLightingAtlas.StaticEntryCapacity)
            {
                throw new InvalidOperationException(
                    "A static-model lighting assignment is outside the physical cache.");
            }

            _atlas.CopySourceTileToPhysicalAtlas(
                assignment.ObjectIndex,
                assignment.EntryIndex,
                _physicalRgbaBytes);
            _objectByEntry[assignment.EntryIndex] = assignment.ObjectIndex;
            for (int frameSlot = 0;
                 frameSlot < _dirtyEntriesByFrameSlot.Length;
                 frameSlot++)
            {
                bool[] dirtyEntries =
                    _dirtyEntriesByFrameSlot[frameSlot];
                if (dirtyEntries[assignment.EntryIndex])
                    continue;
                dirtyEntries[assignment.EntryIndex] = true;
                _dirtyEntryCountByFrameSlot[frameSlot]++;
            }
        }
    }

    internal MTLTexture PrepareFrameSlot(int frameSlot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFrameSlot(frameSlot);
        MTLTexture texture = _textures[frameSlot];
        if (texture.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "The Metal static-model lighting atlas is unavailable.");
        }

        int dirtyCount = _dirtyEntryCountByFrameSlot[frameSlot];
        if (dirtyCount == 0)
            return texture;

        bool[] dirtyEntries = _dirtyEntriesByFrameSlot[frameSlot];
        if (dirtyCount >= FullUploadThreshold)
        {
            ReplaceWholeTexture(texture, _physicalRgbaBytes);
            Array.Clear(dirtyEntries);
            _dirtyEntryCountByFrameSlot[frameSlot] = 0;
            return texture;
        }

        for (int entryIndex = 0;
             entryIndex < dirtyEntries.Length;
             entryIndex++)
        {
            if (!dirtyEntries[entryIndex])
                continue;
            int objectIndex = _objectByEntry[entryIndex];
            if (objectIndex < 0)
            {
                throw new InvalidOperationException(
                    "A dirty static-model lighting entry has no object owner.");
            }
            ReplaceEntry(
                texture,
                entryIndex,
                _atlas.GetSourceTile(objectIndex));
            dirtyEntries[entryIndex] = false;
            _dirtyEntryCountByFrameSlot[frameSlot]--;
        }
        return texture;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_sampler.NativePtr != 0)
            _sampler.Dispose();
        _sampler = default;
        for (int index = 0; index < _textures.Length; index++)
        {
            if (_textures[index].NativePtr != 0)
                _textures[index].Dispose();
            _textures[index] = default;
        }
        Array.Clear(_dirtyEntryCountByFrameSlot);
    }

    private static MTLTexture CreateTexture(
        MTLDevice device,
        ReadOnlySpan<byte> initialBytes)
    {
        MTLStorageMode storageMode = device.HasUnifiedMemory
            ? MTLStorageMode.Shared
            : MTLStorageMode.Managed;
        using var descriptor = new MTLTextureDescriptor
        {
            TextureType = MTLTextureType.Type3D,
            PixelFormat = MTLPixelFormat.RGBA8Unorm,
            Width = MapRenderStaticModelLightingAtlas.Width,
            Height = MapRenderStaticModelLightingAtlas.Height,
            Depth = MapRenderStaticModelLightingAtlas.Depth,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = 1,
            StorageMode = storageMode,
            CpuCacheMode = MTLCPUCacheMode.WriteCombined,
            Usage = MTLTextureUsage.ShaderRead
        };
        MTLTexture texture = device.NewTexture(descriptor);
        if (texture.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal failed to create the 512x256x4 RGBA8 static-model lighting atlas.");
        }
        try
        {
            ReplaceWholeTexture(texture, initialBytes);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static MTLSamplerState CreateSampler(MTLDevice device)
    {
        using var descriptor = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            MipFilter = MTLSamplerMipFilter.NotMipmapped,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
            RAddressMode = MTLSamplerAddressMode.ClampToEdge,
            NormalizedCoordinates = true
        };
        MTLSamplerState sampler = device.NewSamplerState(descriptor);
        if (sampler.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal failed to create the static-model lighting sampler.");
        }
        return sampler;
    }

    private static void ReplaceWholeTexture(
        MTLTexture texture,
        ReadOnlySpan<byte> bytes)
    {
        int expectedByteCount = checked(
            MapRenderStaticModelLightingAtlas.Width *
            MapRenderStaticModelLightingAtlas.Height *
            MapRenderStaticModelLightingAtlas.Depth * 4);
        if (bytes.Length != expectedByteCount)
        {
            throw new ArgumentException(
                "Static-model lighting atlas bytes have an invalid length.",
                nameof(bytes));
        }
        var region = new MTLRegion
        {
            origin = new MTLOrigin(),
            size = new MTLSize
            {
                width = MapRenderStaticModelLightingAtlas.Width,
                height = MapRenderStaticModelLightingAtlas.Height,
                depth = MapRenderStaticModelLightingAtlas.Depth
            }
        };
        fixed (byte* source = bytes)
        {
            texture.ReplaceRegion(
                region,
                0,
                0,
                (nint)source,
                MapRenderStaticModelLightingAtlas.Width * 4,
                    MapRenderStaticModelLightingAtlas.Width *
                    MapRenderStaticModelLightingAtlas.Height * 4);
        }
    }

    private static void ReplaceEntry(
        MTLTexture texture,
        int entryIndex,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != MapRenderStaticModelLightingAtlas.TileByteCount)
        {
            throw new ArgumentException(
                "Static-model lighting tile bytes have an invalid length.",
                nameof(bytes));
        }
        int x =
            (entryIndex &
                (MapRenderStaticModelLightingAtlas.EntriesPerRow - 1)) *
            MapRenderStaticModelLightingAtlas.TileWidth;
        int y =
            (entryIndex / MapRenderStaticModelLightingAtlas.EntriesPerRow) *
            MapRenderStaticModelLightingAtlas.TileHeight;
        var region = new MTLRegion
        {
            origin = new MTLOrigin
            {
                x = checked((ulong)x),
                y = checked((ulong)y),
                z = 0
            },
            size = new MTLSize
            {
                width = MapRenderStaticModelLightingAtlas.TileWidth,
                height = MapRenderStaticModelLightingAtlas.TileHeight,
                depth = MapRenderStaticModelLightingAtlas.TileDepth
            }
        };
        fixed (byte* source = bytes)
        {
            texture.ReplaceRegion(
                region,
                0,
                0,
                (nint)source,
                MapRenderStaticModelLightingAtlas.TileWidth * 4,
                    MapRenderStaticModelLightingAtlas.TileWidth *
                    MapRenderStaticModelLightingAtlas.TileHeight * 4);
        }
    }

    private static void ValidateFrameSlot(int frameSlot)
    {
        if ((uint)frameSlot >= FrameSlotCount)
            throw new ArgumentOutOfRangeException(nameof(frameSlot));
    }
}
