using IW4.Game.Assets.Image;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Material;

public readonly record struct MaterialWaterWritable(uint RawValue)
{
    public float FloatTime => BitConverter.Int32BitsToSingle(unchecked((int)RawValue));
}
