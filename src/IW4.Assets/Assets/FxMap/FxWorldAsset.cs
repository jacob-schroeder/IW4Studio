using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.FxMap;

public sealed class FxWorldAsset : BaseAsset
{
    public const int SerializedSize = 0x74;

    public XAssetType Type => XAssetType.FxMap;

    // 0x00: XString. PS3 FxWorld body stores root+0x00 into varXString and calls Load_XString.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: embedded FxGlassSystem. PS3 sets varFxGlassSystem to root+0x04 and calls Load_FxGlassSystem.
    public FxGlassSystem GlassSystem { get; init; } = new();
}
