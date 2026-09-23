using IW4.Game.Assets.Material;
using IW4.Game.Assets.Weapon;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Vehicle;

public sealed record VehicleVec3(float X, float Y, float Z)
{
    public VehicleVec3()
        : this(0, 0, 0)
    {
    }
}
