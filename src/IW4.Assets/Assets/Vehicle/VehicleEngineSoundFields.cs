using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed class VehicleEngineSoundFields
{
    public VehicleSoundAliasField IdleLowSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField IdleHighSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField EngineLowSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField EngineHighSound { get; init; } = VehicleSoundAliasField.Empty;
    public float EngineSoundSpeed { get; init; }
    public VehicleSoundAliasField EngineStartUpSound { get; init; } = VehicleSoundAliasField.Empty;
    public int EngineStartUpLength { get; init; }
    public VehicleSoundAliasField EngineShutdownSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField EngineIdleSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField EngineSustainSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField EngineRampUpSound { get; init; } = VehicleSoundAliasField.Empty;
    public int EngineRampUpLength { get; init; }
    public VehicleSoundAliasField EngineRampDownSound { get; init; } = VehicleSoundAliasField.Empty;
    public int EngineRampDownLength { get; init; }
}
