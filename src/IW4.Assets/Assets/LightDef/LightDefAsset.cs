using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.LightDef;

public sealed class LightDefAsset : BaseAsset
{
    public const int SerializedSize = 0x10;

    public override XAssetType SerializedAssetType => XAssetType.LightDef;

    // 0x00: XString name.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: GfxLightImage.image.
    public XPointer<GfxImageAsset> ImagePointer { get; init; }
    public GfxImageAsset? Image { get; init; }

    // 0x08: GfxLightImage.samplerState.
    public MaterialSamplerState SamplerState { get; init; }
    public byte[] Pad09To0B { get; init; } = [];

    // 0x0C: GfxLightDef.lmapLookupStart.
    public uint LmapLookupStart { get; init; }
}
