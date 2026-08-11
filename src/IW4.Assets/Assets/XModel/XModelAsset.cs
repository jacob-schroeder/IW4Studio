using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XModel;

public sealed class XModelAsset : BaseAsset
{
    private byte _numSurfs;

    public const int SerializedSize = 0x120;
    public override XAssetType SerializedAssetType => XAssetType.XModel;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public byte NumBones { get; init; }
    public byte NumRootBones { get; init; }
    public byte NumSurfs
    {
        get => _numSurfs;
        init => _numSurfs = value;
    }
    public byte Pad07 { get; init; }
    public float Scale { get; init; }
    public IReadOnlyList<uint> NoScalePartBits { get; init; } = [];
    public XPointer<ushort[]> BoneNamesPointer { get; init; }
    public IReadOnlyList<ushort> BoneNames { get; init; } = [];
    public XPointer<byte[]> ParentListPointer { get; init; }
    public IReadOnlyList<byte> ParentList { get; init; } = [];
    public XPointer<short[]> QuatsPointer { get; init; }
    public IReadOnlyList<short> Quats { get; init; } = [];
    public XPointer<float[]> TransPointer { get; init; }
    public IReadOnlyList<float> Trans { get; init; } = [];
    public XPointer<byte[]> PartClassificationPointer { get; init; }
    public IReadOnlyList<byte> PartClassification { get; init; } = [];
    public XPointer<byte[]> BaseMatPointer { get; init; }
    public IReadOnlyList<DObjAnimMat> BaseMat { get; init; } = [];
    public XPointer<XPointer<MaterialAsset>[]> MaterialHandlesPointer { get; init; }
    public IReadOnlyList<XPointer<MaterialAsset>> MaterialPointers { get; init; } = [];
    public IReadOnlyList<MaterialAsset?> Materials { get; init; } = [];
    public IReadOnlyList<XModelLodInfo> Lods { get; init; } = [];
    public byte MaxLoadedLod { get; init; }
    public byte NumLods { get; init; }
    public byte CollLod { get; init; }
    public byte Flags { get; init; }
    public XPointer<byte[]> CollSurfsPointer { get; init; }
    public int NumCollSurfs { get; init; }
    public int Contents { get; init; }
    public IReadOnlyList<XModelCollSurf> CollSurfs { get; init; } = [];
    public XPointer<byte[]> BoneInfoPointer { get; init; }
    public IReadOnlyList<XBoneInfo> BoneInfo { get; init; } = [];
    public float Radius { get; init; }
    public Bounds Bounds { get; init; } = new();
    public XPointer<ushort[]> InvHighMipRadiusPointer { get; init; }
    public IReadOnlyList<ushort> InvHighMipRadius { get; init; } = [];
    public int MemUsage { get; init; }
    public XPointer<PhysPresetAsset> PhysPresetPointer { get; init; }
    public PhysPresetAsset? PhysPreset { get; init; }
    public XPointer<PhysCollmapAsset> PhysCollmapPointer { get; init; }
    public PhysCollmapAsset? PhysCollmap { get; init; }

    internal void ApplyCanonicalSurfaceCount(byte numSurfs)
    {
        _numSurfs = numSurfs;
    }
}
