namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Native four-byte GfxColor union represented by its lossless packed word.
/// </summary>
public readonly record struct GfxColor(uint Packed)
{
    public const int SerializedSize = 0x04;

    public byte Red => (byte)(Packed >> 24);

    public byte Green => (byte)(Packed >> 16);

    public byte Blue => (byte)(Packed >> 8);

    public byte Alpha => (byte)Packed;

    public static GfxColor FromRgba(
        byte red,
        byte green,
        byte blue,
        byte alpha) =>
        new(
            (uint)red << 24 |
            (uint)green << 16 |
            (uint)blue << 8 |
            alpha);
}
