using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponViewMovementFields
{
    public Vec3 StandMove { get; init; }                                           // 0x138
    public Vec3 StandRotation { get; init; }                                       // 0x144
    public Vec3 StrafeMove { get; init; }                                          // 0x150
    public Vec3 StrafeRotation { get; init; }                                      // 0x15C
    public Vec3 DuckedOffset { get; init; }                                        // 0x168
    public Vec3 DuckedMove { get; init; }                                          // 0x174
    public Vec3 DuckedRotation { get; init; }                                      // 0x180
    public Vec3 ProneOffset { get; init; }                                         // 0x18C
    public Vec3 ProneMove { get; init; }                                           // 0x198
    public Vec3 ProneRotation { get; init; }                                       // 0x1A4
}
