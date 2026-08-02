using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed class VehicleSuspensionSoundFields
{
    public VehicleSoundAliasField SuspensionSoftSound { get; init; } = VehicleSoundAliasField.Empty;
    public float SuspensionSoftCompression { get; init; }
    public VehicleSoundAliasField SuspensionHardSound { get; init; } = VehicleSoundAliasField.Empty;
    public float SuspensionHardCompression { get; init; }
}
