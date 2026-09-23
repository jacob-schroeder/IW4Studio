using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.FxMap;

public sealed class FxGlassDef
{
    public const int SerializedSize = 0x2C;

    public int Offset { get; init; }
    public float HalfThickness { get; init; }
    public IReadOnlyList<FxVec2> TexVecs { get; init; } = [];
    public uint Color { get; init; }
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    public XPointer<MaterialAsset> MaterialShatteredPointer { get; init; }
    public MaterialAsset? MaterialShattered { get; init; }
    public XPointer<PhysPresetAsset> PhysPresetPointer { get; init; }
    public PhysPresetAsset? PhysPreset { get; init; }
    public float InvHighMipRadius { get; init; }
    public float ShatteredInvHighMipRadius { get; init; }
}
