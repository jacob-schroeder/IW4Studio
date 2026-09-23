using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Fx;

public sealed class FxEffectDefAsset : BaseAsset
{
    public const int SerializedSize = 0x20;

    public override XAssetType SerializedAssetType => XAssetType.Fx;
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public int Flags { get; init; }
    public int TotalSize { get; init; }
    public int MsecLoopingLife { get; init; }
    public int ElemDefCountLooping { get; init; }
    public int ElemDefCountOneShot { get; init; }
    public int ElemDefCountEmission { get; init; }
    public int ElemDefCount => checked(ElemDefCountLooping + ElemDefCountOneShot + ElemDefCountEmission);
    public XPointer<FxElemDef[]> ElemDefsPointer { get; init; }
    public IReadOnlyList<FxElemDef> ElemDefs { get; init; } = [];
}
