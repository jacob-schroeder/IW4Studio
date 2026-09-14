using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Codecs.Image;
namespace IW4.Assets.Codecs.GfxMap;

public static class GfxLightmapCodec
{
    public const int PrimaryWidth = 1024;
    public const int PrimaryHeight = 1024;
    public const int SecondaryWidth = 512;
    public const int SecondaryPlaneHeight = 512;
    public const int SecondaryHeight = 1024;

    /// <summary>Creates the native no-bake lightmap pair for an explicit fullbright world.</summary>
    public static GfxLightmapArray CreateFullbright()
    {
        var primary = new byte[PrimaryWidth * PrimaryHeight];
        var secondary = new byte[SecondaryWidth * SecondaryHeight * 4];
        Array.Fill(primary, byte.MaxValue);
        Array.Fill(secondary, byte.MaxValue);
        return Create(0, primary, secondary);
    }

    /// <summary>Creates one v22 atlas pair from linear primary and RGBA secondary pixels.</summary>
    public static GfxLightmapArray Create(int index, ReadOnlySpan<byte> primaryLinear, ReadOnlySpan<byte> secondaryLinearRgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (primaryLinear.Length != PrimaryWidth * PrimaryHeight || secondaryLinearRgba.Length != SecondaryWidth * SecondaryHeight * 4)
            throw new ArgumentException("A lightmap requires one 1024x1024 luminance plane and two 512x512 RGBA planes.");
        byte[] secondary = secondaryLinearRgba.ToArray();
        GfxImageChannelCodec.RgbaToArgb(secondary);
        return new GfxLightmapArray
        {
            Primary = CreateImage($"*lightmap{index}_primary", true,
                GfxImagePixelLayout.SwizzleMorton2D(primaryLinear, PrimaryWidth, PrimaryHeight, 1)),
            Secondary = CreateImage($"*lightmap{index}_secondary", false,
                GfxImagePixelLayout.SwizzleMorton2D(secondary, SecondaryWidth, SecondaryHeight, 4))
        };
    }

    private static GfxImageAsset CreateImage(
        string name,
        bool primary,
        byte[] payload)
    {
        ushort width = primary ? (ushort)PrimaryWidth : (ushort)SecondaryWidth;
        const ushort height = PrimaryHeight;
        return new GfxImageAsset
        {
            Format = (byte)(primary
                ? GfxImageBaseFormat.B8
                : GfxImageBaseFormat.A8R8G8B8),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = primary ? 0x0001a9ffu : 0x0001aae4u,
            Width = width,
            Height = height,
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = TextureSemantic.Function,
            Category = ImageCategory.Lightmap,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = width,
            BaseHeight = height,
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.No,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }
}
