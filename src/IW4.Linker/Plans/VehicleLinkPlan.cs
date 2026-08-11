using IW4.Assets.Assets;
using IW4.Assets.Assets.Vehicle;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen VehicleDef provider. Sound aliases retain their native direct
/// wrapper cells; their names remain XStrings rather than XAsset providers.
/// </summary>
internal sealed class VehicleLinkPlan : AssetLinkPlan
{
    private VehicleLinkPlan(
        AssetKey key,
        string originalSerializedName,
        VehicleDefAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"),
            requireReferencePlaceholder: false)
    {
        Root = CreateOwnedRoot(definition, freeze);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        VehicleDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        bool reference = originalSerializedName.StartsWith(',');
        if (reference)
        {
            ValidateReference(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Vehicle,
                originalSerializedName,
                freeze);
        }

        return new VehicleLinkPlan(key, originalSerializedName, definition, freeze);
    }

    private static void ValidateReference(VehicleDefAsset definition)
    {
        ValidateFixedMembers(definition, allowEmptyReferenceCollections: true);
        if (BuildRootBytes(definition).Any(value => value != 0) ||
            definition.UseHintStringPointer.Raw != 0 ||
            definition.UseHintString is not null ||
            definition.Phys.PhysPresetNamePointer.Raw != 0 ||
            definition.Phys.PhysPresetName is not null ||
            definition.Phys.PhysPresetPointer.Raw != 0 ||
            definition.Phys.PhysPreset is not null ||
            definition.Phys.AccelGraphNamePointer.Raw != 0 ||
            definition.Phys.AccelGraphName is not null ||
            definition.TurretWeaponNamePointer.Raw != 0 ||
            definition.TurretWeaponName is not null ||
            definition.TurretWeaponPointer.Raw != 0 ||
            definition.TurretWeapon is not null ||
            definition.CompassFriendlyIconPointer.Raw != 0 ||
            definition.CompassFriendlyIcon is not null ||
            definition.CompassEnemyIconPointer.Raw != 0 ||
            definition.CompassEnemyIcon is not null ||
            definition.SurfaceSoundPrefixPointer.Raw != 0 ||
            definition.SurfaceSoundPrefix is not null ||
            !IsZeroReferenceTags(definition.TrophyTags) ||
            !EnumerateSoundFields(definition).All(item => IsZero(item.Field)))
        {
            throw new InvalidDataException(
                "A comma-prefixed Vehicle provider must have a zeroed reference body.");
        }

    }

    private LinkStorageSymbol CreateOwnedRoot(
        VehicleDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ValidateFixedMembers(definition, allowEmptyReferenceCollections: false);
        LinkStorageSymbol? useHint = FreezeOptionalXString(
            freeze,
            definition.UseHintString,
            definition.UseHintStringPointer.Untyped,
            "Vehicle.UseHintString");
        LinkStorageSymbol? physPresetName = FreezeOptionalXString(
            freeze,
            definition.Phys.PhysPresetName,
            definition.Phys.PhysPresetNamePointer.Untyped,
            "Vehicle.Phys.PhysPresetName");
        AssetDependency? physPreset = FreezeProviderDependency(
            definition.Phys.PhysPresetPointer.Untyped,
            definition.Phys.PhysPreset,
            XAssetType.PhysPreset,
            "Vehicle.Phys.PhysPreset");
        LinkStorageSymbol? accelGraphName = FreezeOptionalXString(
            freeze,
            definition.Phys.AccelGraphName,
            definition.Phys.AccelGraphNamePointer.Untyped,
            "Vehicle.Phys.AccelGraphName");
        LinkStorageSymbol? turretWeaponName = FreezeOptionalXString(
            freeze,
            definition.TurretWeaponName,
            definition.TurretWeaponNamePointer.Untyped,
            "Vehicle.TurretWeaponName");
        AssetDependency? turretWeapon = FreezeProviderDependency(
            definition.TurretWeaponPointer.Untyped,
            definition.TurretWeapon,
            XAssetType.Weapon,
            "Vehicle.TurretWeapon");
        AssetDependency? compassFriendly = FreezeProviderDependency(
            definition.CompassFriendlyIconPointer.Untyped,
            definition.CompassFriendlyIcon,
            XAssetType.Material,
            "Vehicle.CompassFriendlyIcon");
        AssetDependency? compassEnemy = FreezeProviderDependency(
            definition.CompassEnemyIconPointer.Untyped,
            definition.CompassEnemyIcon,
            XAssetType.Material,
            "Vehicle.CompassEnemyIcon");
        LinkStorageSymbol? surfaceSoundPrefix = FreezeOptionalXString(
            freeze,
            definition.SurfaceSoundPrefix,
            definition.SurfaceSoundPrefixPointer.Untyped,
            "Vehicle.SurfaceSoundPrefix");

        FrozenSoundField[] soundFields = EnumerateSoundFields(definition)
            .Select(item => FreezeSoundField(
                item.Field,
                item.Offset,
                item.Path,
                freeze))
            .ToArray();

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            BuildRootBytes(definition),
            alignment: 4,
            root => RootOperations(
                root,
                useHint,
                physPresetName,
                physPreset,
                accelGraphName,
                turretWeaponName,
                turretWeapon,
                compassFriendly,
                compassEnemy,
                surfaceSoundPrefix,
                soundFields,
                definition.TrophyTags));
    }

    private IEnumerable<LinkOperation> RootOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? useHint,
        LinkStorageSymbol? physPresetName,
        AssetDependency? physPreset,
        LinkStorageSymbol? accelGraphName,
        LinkStorageSymbol? turretWeaponName,
        AssetDependency? turretWeapon,
        AssetDependency? compassFriendly,
        AssetDependency? compassEnemy,
        LinkStorageSymbol? surfaceSoundPrefix,
        IReadOnlyList<FrozenSoundField> soundFields,
        IReadOnlyList<ScriptStringReference> trophyTags)
    {
        yield return NameOperation(root, 0);
        if (useHint is not null)
            yield return XStringOperation(root, 0x08, useHint, "Vehicle.UseHintString");
        if (physPresetName is not null)
        {
            yield return XStringOperation(
                root,
                0xac,
                physPresetName,
                "Vehicle.Phys.PhysPresetName");
        }
        if (physPreset is { } preset)
            yield return ProviderOperation(root, 0xb0, preset);
        if (accelGraphName is not null)
        {
            yield return XStringOperation(
                root,
                0xb4,
                accelGraphName,
                "Vehicle.Phys.AccelGraphName");
        }
        if (turretWeaponName is not null)
        {
            yield return XStringOperation(
                root,
                0x198,
                turretWeaponName,
                "Vehicle.TurretWeaponName");
        }
        if (turretWeapon is { } weapon)
            yield return ProviderOperation(root, 0x19c, weapon);

        foreach (FrozenSoundField sound in soundFields.Take(2))
        {
            if (sound.Target is { } target)
                yield return Direct(root, sound.Offset, target, sound.Path);
        }
        for (int index = 0; index < trophyTags.Count; index++)
        {
            yield return new ScriptStringLinkOperation(
                new LinkStorageCell(
                    root,
                    checked(VehicleDefAsset.ScriptStringOffset + index * sizeof(ushort))),
                trophyTags[index],
                $"Vehicle.TrophyTags[{index}]");
        }
        if (compassFriendly is { } friendly)
            yield return ProviderOperation(root, 0x1d8, friendly);
        if (compassEnemy is { } enemy)
            yield return ProviderOperation(root, 0x1dc, enemy);

        foreach (FrozenSoundField sound in soundFields.Skip(2).Take(14))
        {
            if (sound.Target is { } target)
                yield return Direct(root, sound.Offset, target, sound.Path);
        }
        if (surfaceSoundPrefix is not null)
        {
            yield return XStringOperation(
                root,
                0x240,
                surfaceSoundPrefix,
                "Vehicle.SurfaceSoundPrefix");
        }
        foreach (FrozenSoundField sound in soundFields.Skip(16))
        {
            if (sound.Target is { } target)
                yield return Direct(root, sound.Offset, target, sound.Path);
        }
    }

    private static FrozenSoundField FreezeSoundField(
        VehicleSoundAliasField field,
        int expectedOffset,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Offset != 0 && field.Offset != expectedOffset)
        {
            throw new InvalidDataException(
                $"{fieldPath} retains root offset 0x{field.Offset:X}, expected 0x{expectedOffset:X}.");
        }

        XPointerReference outerPointer = field.Pointer.Untyped;
        XPointerReference valuePointer = field.ValuePointer.Untyped;
        bool present = outerPointer.Type != PointerType.Null ||
            valuePointer.Type != PointerType.Null ||
            field.Value is not null;
        if (!present)
            return new FrozenSoundField(expectedOffset, fieldPath, null);
        if (field.Value is null && valuePointer.Type != PointerType.Null)
        {
            throw new NotSupportedException(
                $"{fieldPath} retains a non-null nested XString pointer without semantic text.");
        }

        LinkStorageSymbol? value = field.Value is null
            ? null
            : freeze.FreezeRequiredXString(
                field.Value,
                valuePointer,
                $"{fieldPath}.Value");
        LinkStorageTarget target = freeze.FreezeStorage(
            outerPointer,
            new byte[sizeof(int)],
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, addend) => IndirectXStringOperations(
                owner,
                addend,
                value,
                field.Value,
                XAssetType.Sound,
                $"{fieldPath}.Value"),
            fieldPath);
        return new FrozenSoundField(expectedOffset, fieldPath, target);
    }

    private static LinkStorageSymbol? FreezeOptionalXString(
        LinkAssetFreezeScope freeze,
        string? value,
        XPointerReference pointer,
        string fieldPath)
    {
        if (value is null)
        {
            if (pointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains a non-null pointer without semantic text.");
            }
            return null;
        }
        return freeze.FreezeRequiredXString(value, pointer, fieldPath);
    }

    private static byte[] BuildRootBytes(VehicleDefAsset definition)
    {
        VehicleFakeBodyTuning fakeBody = definition.FakeBody ??
            throw new InvalidDataException("Vehicle.FakeBody cannot be null.");
        VehicleVec3 killcam = definition.KillcamOffset ??
            throw new InvalidDataException("Vehicle.KillcamOffset cannot be null.");
        VehiclePhysDef phys = definition.Phys ??
            throw new InvalidDataException("Vehicle.Phys cannot be null.");
        VehicleEngineSoundFields engine = definition.EngineSounds ??
            throw new InvalidDataException("Vehicle.EngineSounds cannot be null.");
        VehicleSuspensionSoundFields suspension = definition.SuspensionSounds ??
            throw new InvalidDataException("Vehicle.SuspensionSounds cannot be null.");

        var writer = new LinkTemplateWriter(VehicleDefAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32((int)definition.Type);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.Health);
        writer.WriteInt32(definition.QuadBarrel);
        WriteSingles(writer,
            definition.TexScrollScale,
            definition.TopSpeed,
            definition.Accel,
            definition.RotRate,
            definition.RotAccel,
            definition.MaxBodyPitch,
            definition.MaxBodyRoll);
        WriteSingles(writer,
            fakeBody.AccelPitch,
            fakeBody.AccelRoll,
            fakeBody.VelPitch,
            fakeBody.VelRoll,
            fakeBody.SideVelPitch,
            fakeBody.PitchStrength,
            fakeBody.RollStrength,
            fakeBody.PitchDampening,
            fakeBody.RollDampening,
            fakeBody.BoatRockingAmplitude,
            fakeBody.BoatRockingPeriod,
            fakeBody.BoatRockingRotationPeriod,
            fakeBody.BoatRockingFadeoutSpeed,
            fakeBody.BoatBouncingMinForce,
            fakeBody.BoatBouncingMaxForce,
            fakeBody.BoatBouncingRate,
            fakeBody.BoatBouncingFadeinSpeed,
            fakeBody.BoatBouncingFadeoutSteeringAngle);
        WriteSingles(
            writer,
            definition.CollisionDamage,
            definition.CollisionSpeed,
            killcam.X,
            killcam.Y,
            killcam.Z);
        writer.WriteInt32(definition.PlayerProtected);
        writer.WriteInt32(definition.BulletDamage);
        writer.WriteInt32(definition.ArmorPiercingDamage);
        writer.WriteInt32(definition.GrenadeDamage);
        writer.WriteInt32(definition.ProjectileDamage);
        writer.WriteInt32(definition.ProjectileSplashDamage);
        writer.WriteInt32(definition.HeavyExplosiveDamage);

        writer.WriteInt32(phys.PhysicsEnabled);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32((int)phys.SteeringAxle);
        writer.WriteInt32((int)phys.PowerAxle);
        writer.WriteInt32((int)phys.BrakingAxle);
        WriteSingles(writer,
            phys.TopSpeed,
            phys.ReverseSpeed,
            phys.MaxVelocity,
            phys.MaxPitch,
            phys.MaxRoll,
            phys.SuspensionTravelFront,
            phys.SuspensionTravelRear,
            phys.SuspensionStrengthFront,
            phys.SuspensionDampingFront,
            phys.SuspensionStrengthRear,
            phys.SuspensionDampingRear,
            phys.FrictionBraking,
            phys.FrictionCoasting,
            phys.FrictionTopSpeed,
            phys.FrictionSide,
            phys.FrictionSideRear,
            phys.VelocityDependentSlip,
            phys.RollStability,
            phys.RollResistance,
            phys.PitchResistance,
            phys.YawResistance,
            phys.UprightStrengthPitch,
            phys.UprightStrengthRoll,
            phys.TargetAirPitch,
            phys.AirYawTorque,
            phys.AirPitchTorque,
            phys.MinimumMomentumForCollision,
            phys.CollisionLaunchForceScale,
            phys.WreckedMassScale,
            phys.WreckedBodyFriction,
            phys.MinimumJoltForNotify,
            phys.SlipThresholdFront,
            phys.SlipThresholdRear,
            phys.SlipFricScaleFront,
            phys.SlipFricScaleRear,
            phys.SlipFricRateFront,
            phys.SlipFricRateRear,
            phys.SlipYawTorque);
        WriteSingles(writer,
            definition.BoostDuration,
            definition.BoostRechargeTime,
            definition.BoostAcceleration,
            definition.SuspensionTravel,
            definition.MaxSteeringAngle,
            definition.SteeringLerp,
            definition.MinSteeringScale,
            definition.MinSteeringSpeed);
        writer.WriteInt32(definition.CamLookEnabled);
        WriteSingles(writer,
            definition.CamLerp,
            definition.CamPitchInfluence,
            definition.CamRollInfluence,
            definition.CamFovIncrease,
            definition.CamFovOffset,
            definition.CamFovSpeed);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        WriteSingles(writer,
            definition.TurretHorizSpanLeft,
            definition.TurretHorizSpanRight,
            definition.TurretVertSpanUp,
            definition.TurretVertSpanDown,
            definition.TurretRotRate);
        writer.Skip(2 * sizeof(int));
        writer.WriteInt32(definition.TrophyEnabled);
        WriteSingle(writer, definition.TrophyRadius);
        WriteSingle(writer, definition.TrophyInactiveRadius);
        writer.WriteInt32(definition.TrophyAmmoCount);
        WriteSingle(writer, definition.TrophyReloadTime);
        writer.Skip(VehicleDefAsset.ScriptStringCount * sizeof(ushort));
        writer.Skip(2 * sizeof(int));
        WriteSingles(
            writer,
            definition.CompassIconWidth,
            definition.CompassIconHeight);
        writer.Skip(4 * sizeof(int));
        WriteSingle(writer, engine.EngineSoundSpeed);
        writer.Skip(sizeof(int));
        WriteSingle(writer, engine.EngineStartUpLength);
        writer.Skip(4 * sizeof(int));
        WriteSingle(writer, engine.EngineRampUpLength);
        writer.Skip(sizeof(int));
        WriteSingle(writer, engine.EngineRampDownLength);
        writer.Skip(sizeof(int));
        WriteSingle(writer, suspension.SuspensionSoftCompression);
        writer.Skip(sizeof(int));
        WriteSingle(writer, suspension.SuspensionHardCompression);
        writer.Skip(sizeof(int));
        WriteSingle(writer, definition.CollisionBlendSpeed);
        writer.Skip(sizeof(int));
        WriteSingle(writer, definition.SpeedSoundBlendSpeed);
        writer.Skip(sizeof(int));
        writer.Skip(VehicleDefAsset.SurfaceSoundCount * sizeof(int));
        WriteSingles(writer,
            definition.SurfaceSoundBlendSpeed,
            definition.SlideVolume,
            definition.SlideBlendSpeed,
            definition.InAirPitch);
        return writer.Complete();
    }

    private static void ValidateFixedMembers(
        VehicleDefAsset definition,
        bool allowEmptyReferenceCollections)
    {
        if (!Enum.IsDefined(definition.Type) || definition.Type == VehicleType.Count)
            throw new InvalidDataException("Vehicle.Type is not a defined serialized type.");
        VehiclePhysDef phys = definition.Phys ??
            throw new InvalidDataException("Vehicle.Phys cannot be null.");
        ValidateAxle(phys.SteeringAxle, "Vehicle.Phys.SteeringAxle");
        ValidateAxle(phys.PowerAxle, "Vehicle.Phys.PowerAxle");
        ValidateAxle(phys.BrakingAxle, "Vehicle.Phys.BrakingAxle");
        _ = definition.FakeBody ??
            throw new InvalidDataException("Vehicle.FakeBody cannot be null.");
        _ = definition.KillcamOffset ??
            throw new InvalidDataException("Vehicle.KillcamOffset cannot be null.");
        _ = definition.EngineSounds ??
            throw new InvalidDataException("Vehicle.EngineSounds cannot be null.");
        _ = definition.SuspensionSounds ??
            throw new InvalidDataException("Vehicle.SuspensionSounds cannot be null.");

        if (definition.TrophyTags.Count != VehicleDefAsset.ScriptStringCount &&
            !(allowEmptyReferenceCollections && definition.TrophyTags.Count == 0))
        {
            throw new InvalidDataException(
                $"Vehicle.TrophyTags requires exactly {VehicleDefAsset.ScriptStringCount} entries.");
        }
        for (int index = 0; index < definition.TrophyTags.Count; index++)
        {
            ScriptStringReference value = definition.TrophyTags[index] ??
                throw new InvalidDataException($"Vehicle.TrophyTags[{index}] cannot be null.");
        }

        if (definition.SurfaceSoundFields.Count != VehicleDefAsset.SurfaceSoundCount &&
            !(allowEmptyReferenceCollections && definition.SurfaceSoundFields.Count == 0))
        {
            throw new InvalidDataException(
                $"Vehicle.SurfaceSoundFields requires exactly {VehicleDefAsset.SurfaceSoundCount} entries.");
        }
    }

    private static void ValidateAxle(VehicleAxleType value, string fieldPath)
    {
        if (!Enum.IsDefined(value) || value == VehicleAxleType.Count)
            throw new InvalidDataException($"{fieldPath} is not a defined serialized axle type.");
    }

    private static IEnumerable<(VehicleSoundAliasField Field, int Offset, string Path)>
        EnumerateSoundFields(VehicleDefAsset definition)
    {
        VehicleEngineSoundFields engine = definition.EngineSounds;
        VehicleSuspensionSoundFields suspension = definition.SuspensionSounds;
        yield return (definition.TurretSpinSound, 0x1b4, "Vehicle.TurretSpinSound");
        yield return (definition.TurretStopSound, 0x1b8, "Vehicle.TurretStopSound");
        yield return (engine.IdleLowSound, 0x1e8, "Vehicle.EngineSounds.IdleLowSound");
        yield return (engine.IdleHighSound, 0x1ec, "Vehicle.EngineSounds.IdleHighSound");
        yield return (engine.EngineLowSound, 0x1f0, "Vehicle.EngineSounds.EngineLowSound");
        yield return (engine.EngineHighSound, 0x1f4, "Vehicle.EngineSounds.EngineHighSound");
        yield return (engine.EngineStartUpSound, 0x1fc, "Vehicle.EngineSounds.EngineStartUpSound");
        yield return (engine.EngineShutdownSound, 0x204, "Vehicle.EngineSounds.EngineShutdownSound");
        yield return (engine.EngineIdleSound, 0x208, "Vehicle.EngineSounds.EngineIdleSound");
        yield return (engine.EngineSustainSound, 0x20c, "Vehicle.EngineSounds.EngineSustainSound");
        yield return (engine.EngineRampUpSound, 0x210, "Vehicle.EngineSounds.EngineRampUpSound");
        yield return (engine.EngineRampDownSound, 0x218, "Vehicle.EngineSounds.EngineRampDownSound");
        yield return (suspension.SuspensionSoftSound, 0x220, "Vehicle.SuspensionSounds.SuspensionSoftSound");
        yield return (suspension.SuspensionHardSound, 0x228, "Vehicle.SuspensionSounds.SuspensionHardSound");
        yield return (definition.CollisionSound, 0x230, "Vehicle.CollisionSound");
        yield return (definition.SpeedSound, 0x238, "Vehicle.SpeedSound");
        for (int index = 0; index < definition.SurfaceSoundFields.Count; index++)
        {
            yield return (
                definition.SurfaceSoundFields[index],
                checked(VehicleDefAsset.SurfaceSoundOffset + index * sizeof(int)),
                $"Vehicle.SurfaceSoundFields[{index}]");
        }
    }

    private static bool IsZeroReferenceTags(
        IReadOnlyList<ScriptStringReference> values) =>
        values.Count == 0 || values.All(value =>
            value is not null && value.RawLocalIndex == 0 && value.Text is null);

    private static bool IsZero(VehicleSoundAliasField field) =>
        field is not null &&
        field.Pointer.Raw == 0 &&
        field.ValuePointer.Raw == 0 &&
        field.Value is null;

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static void WriteSingles(
        LinkTemplateWriter writer,
        params float[] values)
    {
        foreach (float value in values)
            WriteSingle(writer, value);
    }

    private static void WriteSingle(LinkTemplateWriter writer, float value) =>
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

    private readonly record struct FrozenSoundField(
        int Offset,
        string Path,
        LinkStorageTarget? Target);
}
