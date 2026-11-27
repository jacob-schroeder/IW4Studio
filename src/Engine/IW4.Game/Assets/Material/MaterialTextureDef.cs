using IW4.Game.Assets.Image;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Material;

public sealed class MaterialTextureDef
{
    public const int SerializedSize = 0x0c;

    public uint NameHash { get; init; }
    public byte NameStart { get; init; }
    public byte NameEnd { get; init; }
    public MaterialSamplerState SamplerState { get; init; }
    public TextureSemantic Semantic { get; init; }
    public XPointerReference DataPointer { get; init; }
    public MaterialTextureDefInfo Data
    {
        get;
        init
        {
            if (value is null)
                return;
            if (field is not null)
                throw new InvalidOperationException("A material texture can have only one resolved payload.");
            field = value;
        }
    }

    public GfxImageAsset? Image
    {
        get => Data is GfxImageAsset image ? image : null;
        init => Data = value;
    }

    public MaterialWater? Water
    {
        get => Data is MaterialWater water ? water : null;
        init => Data = value;
    }
}
