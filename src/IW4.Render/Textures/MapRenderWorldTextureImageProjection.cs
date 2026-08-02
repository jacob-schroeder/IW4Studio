using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

/// <summary>
/// Creates a decode-only image view whose texture header comes from the
/// authoritative runtime GfxTexture row while payload/stream ownership stays
/// with the separately resolved canonical GfxImage.
/// </summary>
public static class MapRenderWorldTextureImageProjection
{
    public static GfxImageAsset Create(
        GfxImageAsset source,
        GfxTexture descriptor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(descriptor);
        uint header = descriptor.Words[0];
        uint dimensions = descriptor.Words[2];
        uint depthAndBlock = descriptor.Words[3];
        byte levelCount = (byte)(header >> 16);
        ushort width = (ushort)(dimensions >> 16);
        ushort height = (ushort)dimensions;
        ushort depth = (ushort)(depthAndBlock >> 16);
        return new GfxImageAsset
        {
            Offset = source.Offset,
            Format = (byte)(header >> 24),
            LevelCount = levelCount,
            DimensionCount = (byte)(header >> 8),
            MultiFaceControl = (byte)header,
            TextureFlags = descriptor.Words[1],
            Width = width,
            Height = height,
            Depth = depth,
            PixelDataBlock = (byte)(depthAndBlock >> 8),
            Pad0F = (byte)depthAndBlock,
            RenderTargetPitch = descriptor.Words[4],
            PixelsOffset = descriptor.Words[5],
            MapType = source.MapType,
            TextureSemantic = source.TextureSemantic,
            Category = source.Category,
            Pad1B = source.Pad1B,
            CardMemory = source.CardMemory,
            BaseWidth = width,
            BaseHeight = height,
            BaseDepth = depth,
            BaseLevelCount = levelCount,
            Cached = source.Cached,
            PayloadPointer = source.PayloadPointer,
            StreamData = source.StreamData,
            StreamImageIndex = source.StreamImageIndex,
            StreamEntries = source.StreamEntries,
            PayloadByteCount = source.PayloadByteCount,
            PayloadBytes = source.PayloadBytes,
            NamePointer = source.NamePointer,
            Name = source.Name
        };
    }
}
