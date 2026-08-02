using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class PhysMass
{
    public Vec3 CenterOfMass { get; init; }
    public Vec3 MomentsOfInertia { get; init; }
    public Vec3 ProductsOfInertia { get; init; }
}
