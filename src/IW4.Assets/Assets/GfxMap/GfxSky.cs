using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxSky
{
    public const int SerializedSize = 0x10;

    public int SkySurfCount { get; init; }
    public XPointer<int[]> SkyStartSurfsPointer { get; init; }
    // Entries are positions in GfxWorld.dpvs.sortedSurfIndex. Resolve each
    // position through that table before indexing GfxWorld.dpvs.surfaces.
    public IReadOnlyList<int> SkyStartSurfs { get; init; } = [];
    public XPointer<GfxImageAsset> SkyImagePointer { get; init; }
    public GfxImageAsset? SkyImage { get; init; }
    // PS3 stores the authored sampler byte at +0x0C followed by three zero pad bytes.
    // The loader preserves the copied big-endian word so the effective byte is its MSB.
    public int SkySamplerState { get; init; }
    public byte SamplerState => unchecked((byte)((uint)SkySamplerState >> 24));
}
