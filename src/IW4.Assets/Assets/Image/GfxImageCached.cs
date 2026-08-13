namespace IW4.Assets.Assets.Image;

/// <summary>
/// GfxImage cache-lifecycle gate stored in byte 0x27. The PS3 runtime treats
/// every nonzero value as cached.
/// </summary>
public enum GfxImageCached : byte
{
    No = 0,
    Auto = 1,
    Manual = 2
}
