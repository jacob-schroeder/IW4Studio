using IW4.Assets.Assets.Fx;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.ImpactFx;

public sealed class FxImpactEntry
{
    public const int SerializedSize = 0x8C;
    public const int SurfaceEffectCount = 31;
    public const int FleshEffectCount = 4;

    public int Offset { get; init; }
    public IReadOnlyList<XPointer<FxEffectDefAsset>> SurfaceEffectPointers { get; init; } = [];
    public IReadOnlyList<FxEffectDefAsset?> SurfaceEffects { get; init; } = [];
    public IReadOnlyList<XPointer<FxEffectDefAsset>> FleshEffectPointers { get; init; } = [];
    public IReadOnlyList<FxEffectDefAsset?> FleshEffects { get; init; } = [];
}
