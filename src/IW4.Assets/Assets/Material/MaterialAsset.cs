using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class MaterialAsset : BaseAsset
{
    public const int SerializedSize = 0xa8;
    public const int TechniqueSlotCount = 37;

    private IReadOnlyList<ushort> _inlineTechniqueSlotStateBits = [];
    private IReadOnlyList<ushort> _runtimeTechniqueSlotStateBits = [];

    public override XAssetType SerializedAssetType => XAssetType.Material;
    public override string? SerializedAssetName => Info.Name;

    public MaterialInfo Info { get; init; } = new();
    public IReadOnlyList<MaterialStateBitsEntry> StateBitsEntries { get; init; } = [];
    public byte TextureCount { get; init; }
    public byte ConstantCount { get; init; }
    public byte StateBitsCount { get; init; }
    public byte StateFlags { get; init; }
    public byte CameraRegion { get; init; }
    public byte XStringCount { get; init; }
    public byte Pad43 { get; init; }
    public IReadOnlyList<ushort> InlineTechniqueSlotStateBits
    {
        get => _inlineTechniqueSlotStateBits;
        init => _inlineTechniqueSlotStateBits = value;
    }
    public ushort Pad8E { get; init; }
    public XPointerReference RuntimeTechniqueSlotStateBitsPointer { get; init; }
    public IReadOnlyList<ushort> RuntimeTechniqueSlotStateBits
    {
        get => _runtimeTechniqueSlotStateBits;
        init => _runtimeTechniqueSlotStateBits = value;
    }
    public XPointer<MaterialTechniqueSetAsset> TechniqueSetPointer { get; init; }
    public MaterialTechniqueSetAsset? TechniqueSet { get; init; }
    public XPointerReference TextureTablePointer { get; init; }
    public IReadOnlyList<MaterialTextureDef> Textures { get; init; } = [];
    public XPointerReference ConstantTablePointer { get; init; }
    public IReadOnlyList<MaterialConstantDef> Constants { get; init; } = [];
    public XPointerReference StateBitsPointer { get; init; }
    public IReadOnlyList<GfxStateBits> StateBits { get; init; } = [];
    public XPointerReference XStringTablePointer { get; init; }
    public IReadOnlyList<MaterialXStringEntry> XStrings { get; init; } = [];

    internal void SetTechniqueSlotStateId(int slotIndex, int passIndex, ushort stateId)
    {
        if ((uint)slotIndex >= TechniqueSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (passIndex is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(passIndex));

        if (passIndex == 0)
            _inlineTechniqueSlotStateBits = SetSlot(_inlineTechniqueSlotStateBits, slotIndex, stateId);
        else
            _runtimeTechniqueSlotStateBits = SetSlot(_runtimeTechniqueSlotStateBits, slotIndex, stateId);
    }

    private static IReadOnlyList<ushort> SetSlot(
        IReadOnlyList<ushort> current,
        int slotIndex,
        ushort stateId)
    {
        ushort[] values = new ushort[TechniqueSlotCount];
        for (int index = 0; index < System.Math.Min(current.Count, values.Length); index++)
            values[index] = current[index];
        values[slotIndex] = stateId;
        return Array.AsReadOnly(values);
    }
}
