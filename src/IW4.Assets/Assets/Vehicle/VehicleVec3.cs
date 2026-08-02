using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed record VehicleVec3(float X, float Y, float Z)
{
    public VehicleVec3()
        : this(0, 0, 0)
    {
    }
}
