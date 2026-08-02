namespace IW4.FastFiles.Zone;

/// <summary>
/// Raw serialized 0x08-byte XAsset table row. The second word is intentionally
/// opaque here: native no-op rows preserve it without interpreting it as a
/// pointer, while loader-owned projections classify and resolve it later.
/// </summary>
public struct XAsset
{
    public XAssetType Type;
    public int RawHeader;
}
