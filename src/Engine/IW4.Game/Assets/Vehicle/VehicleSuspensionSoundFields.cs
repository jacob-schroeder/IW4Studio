using IW4.Game.Assets.Material;
using IW4.Game.Assets.Weapon;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Vehicle;

public sealed class VehicleSuspensionSoundFields
{
    public VehicleSoundAliasField SuspensionSoftSound { get; init; } = VehicleSoundAliasField.Empty;
    public float SuspensionSoftCompression { get; init; }
    public VehicleSoundAliasField SuspensionHardSound { get; init; } = VehicleSoundAliasField.Empty;
    public float SuspensionHardCompression { get; init; }
}
