using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.ComWorld;

public sealed class ComWorldAsset : BaseAsset
{
    public const int SerializedSize = 0x10;

    public XAssetType Type => XAssetType.ComMap;

    // 0x00: XString name.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: ComWorld.isInUse.
    public int IsInUse { get; init; }

    // 0x08: ComWorld.primaryLightCount.
    public int PrimaryLightCount { get; init; }

    // 0x0C: optional inline ComPrimaryLight[primaryLightCount] payload.
    public XPointer<ComPrimaryLight[]> PrimaryLightsPointer { get; init; }
    public IReadOnlyList<ComPrimaryLight> PrimaryLights { get; init; } = [];
}
