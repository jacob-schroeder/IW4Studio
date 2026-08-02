namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Native four-byte GfxColor union represented by its lossless packed word.
/// </summary>
public readonly record struct GfxColor(uint Packed)
{
    public const int SerializedSize = 0x04;
}
