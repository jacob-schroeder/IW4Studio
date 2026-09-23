using IW4.Game.Assets.Material;
using IW4.Game.Assets.Weapon;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Vehicle;

public sealed record VehicleSoundAliasField(
    int Offset,
    XPointer<string> Pointer,
    XPointer<string> ValuePointer,
    string? Value)
{
    public static VehicleSoundAliasField Empty { get; } = new(
        0,
        default,
        default,
        null);
}
