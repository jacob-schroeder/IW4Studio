using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed record VehicleSoundAliasField(
    int Offset,
    XPointer<string> Pointer,
    string? Value)
{
    public static VehicleSoundAliasField Empty { get; } = new(0, default, null);
}
