using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Explicit serializer for the 0x2d0 VehicleDef root.  Every child
/// is planned in the order in which VehicleDefLoader consumes it; nested
/// sound-alias cells are preserved as cells, not flattened strings.</summary>
public sealed class VehicleBodyEmitter : IXAssetBodyEmitter
{
    private const int RootSize = 0x2d0;
    private const int PhysScalarCount = 38;
    private const int MaterialRootSize = 0xa8;
    private const int PhysPresetRootSize = 0x2c;
    private const int WeaponVariantRootSize = 0x74;

    public XAssetType AssetType => XAssetType.Vehicle;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IVehicleBuildData data)
        {
            diagnostics.Add(Error("body", "Vehicle build data does not implement IVehicleBuildData.", rowIndex));
            return diagnostics;
        }
        if (!Enum.IsDefined((IW4.Assets.Assets.Vehicle.VehicleType)data.Type) || data.Type == (int)IW4.Assets.Assets.Vehicle.VehicleType.Count)
            diagnostics.Add(Error("type", "Vehicle type is not a defined serialized vehicle type.", rowIndex));
        ValidateString(data.Name, "name", diagnostics, rowIndex); ValidateString(data.UseHintString, "useHintString", diagnostics, rowIndex);
        ValidateFloats(data.MovementScalars, 7, "movementScalars", diagnostics, rowIndex);
        ValidateFakeBody(data.FakeBody, diagnostics, rowIndex);
        ValidateFinite(data.CollisionDamage, "collisionDamage", diagnostics, rowIndex); ValidateFinite(data.CollisionSpeed, "collisionSpeed", diagnostics, rowIndex); ValidateVector(data.KillcamOffset, "killcamOffset", diagnostics, rowIndex);
        if (data.DamageValues.Count != 7) diagnostics.Add(Error("damageValues", "Vehicle requires seven fixed damage values.", rowIndex));
        ValidatePhysics(data.Physics, diagnostics, rowIndex);
        ValidateFloats(data.BoostAndSteeringScalars, 8, "boostAndSteeringScalars", diagnostics, rowIndex); ValidateFloats(data.CameraScalars, 6, "cameraScalars", diagnostics, rowIndex);
        ValidateString(data.TurretWeaponName, "turretWeaponName", diagnostics, rowIndex); ValidateReference(data.TurretWeaponReference, XAssetType.Weapon, "turretWeaponReference", diagnostics, rowIndex); ValidateFloats(data.TurretScalars, 5, "turretScalars", diagnostics, rowIndex);
        ValidateString(data.TurretSpinSound, "turretSpinSound", diagnostics, rowIndex); ValidateString(data.TurretStopSound, "turretStopSound", diagnostics, rowIndex);
        if (data.TrophyTags.Count != 4) diagnostics.Add(Error("trophyTags", "Vehicle requires exactly four trophy script-string tags.", rowIndex));
        ValidateReference(data.CompassFriendlyIconReference, XAssetType.Material, "compassFriendlyIconReference", diagnostics, rowIndex); ValidateReference(data.CompassEnemyIconReference, XAssetType.Material, "compassEnemyIconReference", diagnostics, rowIndex);
        ValidateFinite(data.CompassIconWidth, "compassIconWidth", diagnostics, rowIndex); ValidateFinite(data.CompassIconHeight, "compassIconHeight", diagnostics, rowIndex);
        ValidateEngine(data.EngineSounds, diagnostics, rowIndex); ValidateSuspension(data.SuspensionSounds, diagnostics, rowIndex);
        ValidateString(data.CollisionSound, "collisionSound", diagnostics, rowIndex); ValidateFinite(data.CollisionBlendSpeed, "collisionBlendSpeed", diagnostics, rowIndex); ValidateString(data.SpeedSound, "speedSound", diagnostics, rowIndex); ValidateFinite(data.SpeedSoundBlendSpeed, "speedSoundBlendSpeed", diagnostics, rowIndex);
        ValidateString(data.SurfaceSoundPrefix, "surfaceSoundPrefix", diagnostics, rowIndex);
        if (data.SurfaceSoundAliases.Count != 31) diagnostics.Add(Error("surfaceSoundAliases", "Vehicle requires exactly 31 surface-sound slots.", rowIndex));
        for (int index = 0; index < data.SurfaceSoundAliases.Count; index++) ValidateString(data.SurfaceSoundAliases[index], $"surfaceSoundAliases[{index}]", diagnostics, rowIndex);
        ValidateFinite(data.SurfaceSoundBlendSpeed, "surfaceSoundBlendSpeed", diagnostics, rowIndex); ValidateFinite(data.SlideVolume, "slideVolume", diagnostics, rowIndex); ValidateFinite(data.SlideBlendSpeed, "slideBlendSpeed", diagnostics, rowIndex); ValidateFinite(data.InAirPitch, "inAirPitch", diagnostics, rowIndex);
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IVehicleBuildData data = (IVehicleBuildData)buildData;
        var all = new List<EmissionBlockSegment>();
        var sourceAfterRoot = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(RootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = PlanString(data.Name, plan, all, sourceAfterRoot);
        PlannedString? useHint = PlanString(data.UseHintString, plan, all, sourceAfterRoot);
        PlannedString? presetName = PlanString(data.Physics.PhysPresetName, plan, all, sourceAfterRoot);
        ExternalPlan? preset = PlanExternal(data.Physics.PhysPresetReference, XAssetType.PhysPreset, PhysPresetRootSize, plan, all);
        Add(sourceAfterRoot, preset);
        PlannedString? accelGraphName = PlanString(data.Physics.AccelGraphName, plan, all, sourceAfterRoot);
        PlannedString? turretWeaponName = PlanString(data.TurretWeaponName, plan, all, sourceAfterRoot);
        ExternalPlan? turretWeapon = PlanExternal(data.TurretWeaponReference, XAssetType.Weapon, WeaponVariantRootSize, plan, all);
        Add(sourceAfterRoot, turretWeapon);
        SoundCellPlan? turretSpin = PlanSoundCell(data.TurretSpinSound, plan, all); Add(sourceAfterRoot, turretSpin);
        SoundCellPlan? turretStop = PlanSoundCell(data.TurretStopSound, plan, all); Add(sourceAfterRoot, turretStop);
        ExternalPlan? compassFriendly = PlanExternal(data.CompassFriendlyIconReference, XAssetType.Material, MaterialRootSize, plan, all); Add(sourceAfterRoot, compassFriendly);
        ExternalPlan? compassEnemy = PlanExternal(data.CompassEnemyIconReference, XAssetType.Material, MaterialRootSize, plan, all); Add(sourceAfterRoot, compassEnemy);
        SoundCellPlan? idleLow = PlanSoundCell(data.EngineSounds.IdleLow, plan, all); Add(sourceAfterRoot, idleLow);
        SoundCellPlan? idleHigh = PlanSoundCell(data.EngineSounds.IdleHigh, plan, all); Add(sourceAfterRoot, idleHigh);
        SoundCellPlan? engineLow = PlanSoundCell(data.EngineSounds.EngineLow, plan, all); Add(sourceAfterRoot, engineLow);
        SoundCellPlan? engineHigh = PlanSoundCell(data.EngineSounds.EngineHigh, plan, all); Add(sourceAfterRoot, engineHigh);
        SoundCellPlan? engineStartUp = PlanSoundCell(data.EngineSounds.EngineStartUp, plan, all); Add(sourceAfterRoot, engineStartUp);
        SoundCellPlan? engineShutdown = PlanSoundCell(data.EngineSounds.EngineShutdown, plan, all); Add(sourceAfterRoot, engineShutdown);
        SoundCellPlan? engineIdle = PlanSoundCell(data.EngineSounds.EngineIdle, plan, all); Add(sourceAfterRoot, engineIdle);
        SoundCellPlan? engineSustain = PlanSoundCell(data.EngineSounds.EngineSustain, plan, all); Add(sourceAfterRoot, engineSustain);
        SoundCellPlan? engineRampUp = PlanSoundCell(data.EngineSounds.EngineRampUp, plan, all); Add(sourceAfterRoot, engineRampUp);
        SoundCellPlan? engineRampDown = PlanSoundCell(data.EngineSounds.EngineRampDown, plan, all); Add(sourceAfterRoot, engineRampDown);
        SoundCellPlan? suspensionSoft = PlanSoundCell(data.SuspensionSounds.Soft, plan, all); Add(sourceAfterRoot, suspensionSoft);
        SoundCellPlan? suspensionHard = PlanSoundCell(data.SuspensionSounds.Hard, plan, all); Add(sourceAfterRoot, suspensionHard);
        SoundCellPlan? collisionSound = PlanSoundCell(data.CollisionSound, plan, all); Add(sourceAfterRoot, collisionSound);
        SoundCellPlan? speedSound = PlanSoundCell(data.SpeedSound, plan, all); Add(sourceAfterRoot, speedSound);
        PlannedString? surfacePrefix = PlanString(data.SurfaceSoundPrefix, plan, all, sourceAfterRoot);
        SoundCellPlan?[] surfaceSounds = data.SurfaceSoundAliases.Select(value => PlanSoundCell(value, plan, all)).ToArray();
        foreach (SoundCellPlan? sound in surfaceSounds) Add(sourceAfterRoot, sound);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(data.Type); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(useHint)); writer.WriteInt32(data.Health); writer.WriteInt32(data.QuadBarrel);
        WriteFloats(writer, data.MovementScalars); WriteFakeBody(writer, data.FakeBody); writer.WriteSingle(data.CollisionDamage); writer.WriteSingle(data.CollisionSpeed); WriteVector(writer, data.KillcamOffset); foreach (int value in data.DamageValues) writer.WriteInt32(value);
        WritePhysics(writer, data.Physics, presetName, preset, accelGraphName);
        WriteFloats(writer, data.BoostAndSteeringScalars); writer.WriteInt32(data.CamLookEnabled); WriteFloats(writer, data.CameraScalars); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(turretWeaponName)); writer.WriteInt32(Pointer(turretWeapon)); WriteFloats(writer, data.TurretScalars);
        writer.WriteInt32(Pointer(turretSpin)); writer.WriteInt32(Pointer(turretStop)); writer.WriteInt32(data.TrophyEnabled); writer.WriteSingle(data.TrophyRadius); writer.WriteSingle(data.TrophyInactiveRadius); writer.WriteInt32(data.TrophyAmmoCount); writer.WriteSingle(data.TrophyReloadTime); foreach (ushort tag in data.TrophyTags) writer.WriteUInt16(tag);
        writer.WriteInt32(Pointer(compassFriendly)); writer.WriteInt32(Pointer(compassEnemy)); writer.WriteSingle(data.CompassIconWidth); writer.WriteSingle(data.CompassIconHeight);
        WriteEngineSounds(writer, data.EngineSounds, idleLow, idleHigh, engineLow, engineHigh, engineStartUp, engineShutdown, engineIdle, engineSustain, engineRampUp, engineRampDown);
        writer.WriteInt32(Pointer(suspensionSoft)); writer.WriteSingle(data.SuspensionSounds.SoftCompression); writer.WriteInt32(Pointer(suspensionHard)); writer.WriteSingle(data.SuspensionSounds.HardCompression);
        writer.WriteInt32(Pointer(collisionSound)); writer.WriteSingle(data.CollisionBlendSpeed); writer.WriteInt32(Pointer(speedSound)); writer.WriteSingle(data.SpeedSoundBlendSpeed); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(surfacePrefix)); foreach (SoundCellPlan? sound in surfaceSounds) writer.WriteInt32(Pointer(sound)); writer.WriteSingle(data.SurfaceSoundBlendSpeed); writer.WriteSingle(data.SlideVolume); writer.WriteSingle(data.SlideBlendSpeed); writer.WriteSingle(data.InAirPitch);
        if (writer.Position != RootSize) throw new InvalidDataException($"Vehicle root emission produced 0x{writer.Position:X} bytes instead of 0x{RootSize:X}.");
        EmissionBlockSegment rootSegment = new(root, writer.ToArray()); all.Add(rootSegment);
        List<EmissionBlockSegment> source = [rootSegment]; source.AddRange(sourceAfterRoot);
        return new AssetBodyEmission(AssetType, root, all, source);
    }

    private static PlannedString? PlanString(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source)
    { int before = all.Count; PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases); source.AddRange(all.Skip(before)); return result; }
    private static SoundCellPlan? PlanSoundCell(string? value, EmissionPlan plan, List<EmissionBlockSegment> all)
    { if (value is null) return null; EmissionAddress cell = plan.Allocate(sizeof(int), 4); int before = all.Count; PlannedString? stringValue = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases); EmissionBlockSegment cellSegment = new(cell, Int32Segment(AssetBodyEmitterHelpers.SourcePointer(stringValue))); all.Add(cellSegment); List<EmissionBlockSegment> source = [cellSegment]; source.AddRange(all.Skip(before).Where(segment => segment != cellSegment)); return new SoundCellPlan(cellSegment, source); }
    private static ExternalPlan? PlanExternal(SymbolicXAssetReference? reference, XAssetType type, int rootSize, EmissionPlan plan, List<EmissionBlockSegment> all)
    { if (reference is null) return null; plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(rootSize, 4); plan.Push(XFileBlockType.LARGE); int before = all.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases); EmissionBlockSegment[] strings = all.Skip(before).ToArray(); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP); var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.Reserve(rootSize - sizeof(int)); EmissionBlockSegment rootSegment = new(root, writer.ToArray()); all.Add(rootSegment); List<EmissionBlockSegment> source = [rootSegment]; source.AddRange(strings); return new ExternalPlan(rootSegment, source); }
    private static byte[] Int32Segment(int value) { var writer = new XSourceWriter(); writer.WriteInt32(value); return writer.ToArray(); }
    private static void Add(List<EmissionBlockSegment> source, ExternalPlan? plan) { if (plan is not null) source.AddRange(plan.SourceSegments); }
    private static void Add(List<EmissionBlockSegment> source, SoundCellPlan? plan) { if (plan is not null) source.AddRange(plan.SourceSegments); }
    private static int Pointer(ExternalPlan? plan) => plan is null ? 0 : -1;
    private static int Pointer(SoundCellPlan? plan) => plan is null ? 0 : -1;
    private static void WriteFloats(XSourceWriter writer, IReadOnlyList<float> values) { foreach (float value in values) writer.WriteSingle(value); }
    private static void WriteVector(XSourceWriter writer, VehicleVec3BuildData value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void WriteFakeBody(XSourceWriter writer, VehicleFakeBodyBuildData value) { WriteFloats(writer, [value.AccelPitch, value.AccelRoll, value.VelPitch, value.VelRoll, value.SideVelPitch, value.PitchStrength, value.RollStrength, value.PitchDampening, value.RollDampening, value.BoatRockingAmplitude, value.BoatRockingPeriod, value.BoatRockingRotationPeriod, value.BoatRockingFadeoutSpeed, value.BoatBouncingMinForce, value.BoatBouncingMaxForce, value.BoatBouncingRate, value.BoatBouncingFadeinSpeed, value.BoatBouncingFadeoutSteeringAngle]); }
    private static void WritePhysics(XSourceWriter writer, VehiclePhysicsBuildData value, PlannedString? presetName, ExternalPlan? preset, PlannedString? accelGraphName) { writer.WriteInt32(value.PhysicsEnabled); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(presetName)); writer.WriteInt32(Pointer(preset)); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(accelGraphName)); writer.WriteInt32(value.SteeringAxle); writer.WriteInt32(value.PowerAxle); writer.WriteInt32(value.BrakingAxle); WriteFloats(writer, value.Scalars); }
    private static void WriteEngineSounds(XSourceWriter writer, VehicleEngineSoundsBuildData value, SoundCellPlan? idleLow, SoundCellPlan? idleHigh, SoundCellPlan? engineLow, SoundCellPlan? engineHigh, SoundCellPlan? startUp, SoundCellPlan? shutdown, SoundCellPlan? idle, SoundCellPlan? sustain, SoundCellPlan? rampUp, SoundCellPlan? rampDown) { writer.WriteInt32(Pointer(idleLow)); writer.WriteInt32(Pointer(idleHigh)); writer.WriteInt32(Pointer(engineLow)); writer.WriteInt32(Pointer(engineHigh)); writer.WriteSingle(value.EngineSoundSpeed); writer.WriteInt32(Pointer(startUp)); writer.WriteSingle(value.EngineStartUpLength); writer.WriteInt32(Pointer(shutdown)); writer.WriteInt32(Pointer(idle)); writer.WriteInt32(Pointer(sustain)); writer.WriteInt32(Pointer(rampUp)); writer.WriteSingle(value.EngineRampUpLength); writer.WriteInt32(Pointer(rampDown)); writer.WriteSingle(value.EngineRampDownLength); }
    private static void ValidatePhysics(VehiclePhysicsBuildData value, List<EmissionError> errors, int? rowIndex) { ValidateString(value.PhysPresetName, "physics.physPresetName", errors, rowIndex); ValidateReference(value.PhysPresetReference, XAssetType.PhysPreset, "physics.physPresetReference", errors, rowIndex); ValidateString(value.AccelGraphName, "physics.accelGraphName", errors, rowIndex); if (!IsAxle(value.SteeringAxle)) errors.Add(Error("physics.steeringAxle", "Invalid vehicle axle discriminator.", rowIndex)); if (!IsAxle(value.PowerAxle)) errors.Add(Error("physics.powerAxle", "Invalid vehicle axle discriminator.", rowIndex)); if (!IsAxle(value.BrakingAxle)) errors.Add(Error("physics.brakingAxle", "Invalid vehicle axle discriminator.", rowIndex)); ValidateFloats(value.Scalars, PhysScalarCount, "physics.scalars", errors, rowIndex); }
    private static bool IsAxle(int value) => value >= 0 && value < (int)IW4.Assets.Assets.Vehicle.VehicleAxleType.Count;
    private static void ValidateEngine(VehicleEngineSoundsBuildData value, List<EmissionError> errors, int? rowIndex) { foreach ((string path, string? text) in new[] { ("engineSounds.idleLow", value.IdleLow), ("engineSounds.idleHigh", value.IdleHigh), ("engineSounds.engineLow", value.EngineLow), ("engineSounds.engineHigh", value.EngineHigh), ("engineSounds.engineStartUp", value.EngineStartUp), ("engineSounds.engineShutdown", value.EngineShutdown), ("engineSounds.engineIdle", value.EngineIdle), ("engineSounds.engineSustain", value.EngineSustain), ("engineSounds.engineRampUp", value.EngineRampUp), ("engineSounds.engineRampDown", value.EngineRampDown) }) ValidateString(text, path, errors, rowIndex); ValidateFinite(value.EngineSoundSpeed, "engineSounds.speed", errors, rowIndex); ValidateFinite(value.EngineStartUpLength, "engineSounds.startUpLength", errors, rowIndex); ValidateFinite(value.EngineRampUpLength, "engineSounds.rampUpLength", errors, rowIndex); ValidateFinite(value.EngineRampDownLength, "engineSounds.rampDownLength", errors, rowIndex); }
    private static void ValidateSuspension(VehicleSuspensionSoundsBuildData value, List<EmissionError> errors, int? rowIndex) { ValidateString(value.Soft, "suspensionSounds.soft", errors, rowIndex); ValidateString(value.Hard, "suspensionSounds.hard", errors, rowIndex); ValidateFinite(value.SoftCompression, "suspensionSounds.softCompression", errors, rowIndex); ValidateFinite(value.HardCompression, "suspensionSounds.hardCompression", errors, rowIndex); }
    private static void ValidateFakeBody(VehicleFakeBodyBuildData value, List<EmissionError> errors, int? rowIndex) => ValidateFloats([value.AccelPitch, value.AccelRoll, value.VelPitch, value.VelRoll, value.SideVelPitch, value.PitchStrength, value.RollStrength, value.PitchDampening, value.RollDampening, value.BoatRockingAmplitude, value.BoatRockingPeriod, value.BoatRockingRotationPeriod, value.BoatRockingFadeoutSpeed, value.BoatBouncingMinForce, value.BoatBouncingMaxForce, value.BoatBouncingRate, value.BoatBouncingFadeinSpeed, value.BoatBouncingFadeoutSteeringAngle], 18, "fakeBody", errors, rowIndex);
    private static void ValidateFloats(IReadOnlyList<float> values, int expected, string path, List<EmissionError> errors, int? rowIndex) { if (values.Count != expected) { errors.Add(Error(path, $"Requires exactly {expected} values.", rowIndex)); return; } for (int index = 0; index < values.Count; index++) ValidateFinite(values[index], $"{path}[{index}]", errors, rowIndex); }
    private static void ValidateReference(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && (value.AssetType != expected || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) errors.Add(Error(path, $"Reference must be a comma-prefixed external {expected} identity.", rowIndex)); }
    private static void ValidateVector(VehicleVec3BuildData value, string path, List<EmissionError> errors, int? rowIndex) { ValidateFinite(value.X, $"{path}.x", errors, rowIndex); ValidateFinite(value.Y, $"{path}.y", errors, rowIndex); ValidateFinite(value.Z, $"{path}.z", errors, rowIndex); }
    private static void ValidateString(string? value, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex)); }
    private static void ValidateFinite(float value, string path, List<EmissionError> errors, int? rowIndex) { if (!float.IsFinite(value)) errors.Add(Error(path, "Value must be finite.", rowIndex)); }
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.Vehicle);
    private sealed record ExternalPlan(EmissionBlockSegment Root, IReadOnlyList<EmissionBlockSegment> SourceSegments);
    private sealed record SoundCellPlan(EmissionBlockSegment Cell, IReadOnlyList<EmissionBlockSegment> SourceSegments);
}
