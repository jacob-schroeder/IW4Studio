using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Sound;

public sealed class SndCurve : BaseAsset
{
    public const int SerializedSize = 0x88;
    public const int MaxKnotCount = 16;
    public const int KnotSerializedSize = 2 * sizeof(float);
    public const int KnotsByteCount = MaxKnotCount * KnotSerializedSize;
    public override XAssetType SerializedAssetType => XAssetType.SndCurve;

    public XPointer<string> FilenamePointer { get; init; }
    public string? Filename { get; init; }
    public override string? SerializedAssetName => Filename;
    public ushort KnotCount { get; init; }
    public ushort Padding { get; init; }
    public IReadOnlyList<SndCurveKnot> Knots { get; init; } = [];
}
