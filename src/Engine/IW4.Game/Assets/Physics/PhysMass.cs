using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Physics;

public sealed class PhysMass
{
    public Vec3 CenterOfMass { get; init; }
    public Vec3 MomentsOfInertia { get; init; }
    public Vec3 ProductsOfInertia { get; init; }
}
