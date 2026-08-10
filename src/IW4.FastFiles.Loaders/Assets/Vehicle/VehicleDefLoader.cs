using IW4.FastFiles.Loaders.Database;
using System.Buffers.Binary;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.FastFiles.Loaders.Assets.Weapon;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.Vehicle;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using WeaponAsset = IW4.Assets.Assets.Weapon.WeaponAsset;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Vehicle;

public sealed class VehicleDefLoader
{
    private const int MaterialSize = 0xA8;
    private readonly MaterialLoader _materialLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();
    private readonly WeaponLoader _weaponLoader = new();

    public VehicleDefAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level Vehicle pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<VehicleDefAsset>(
                pointer,
                VehicleDefAsset.SerializedSize,
                "Vehicle");
            VehicleDefAsset canonical = context.ResolveCanonicalAsset<VehicleDefAsset>(
                    pointer,
                    XAssetType.Vehicle)
                ?? throw new InvalidDataException(
                    $"Top-level Vehicle pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Vehicle asset.");
            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Vehicle pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            VehicleDefAsset vehicle = ReadVehicleDef(cursor, rootAddress, context);
            VehicleDefAsset canonical = context.DB_AddXAsset(
                XAssetType.Vehicle,
                vehicle.Name,
                vehicle,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private VehicleDefAsset ReadVehicleDef(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, VehicleDefAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"Vehicle pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        VehicleType type = (VehicleType)rootCursor.ReadInt32();
        XString useHintPointer = ReadXStringPointer(rootCursor, context);
        int health = rootCursor.ReadInt32();
        int quadBarrel = rootCursor.ReadInt32();
        float texScrollScale = ReadSingle(rootCursor);
        float topSpeed = ReadSingle(rootCursor);
        float accel = ReadSingle(rootCursor);
        float rotRate = ReadSingle(rootCursor);
        float rotAccel = ReadSingle(rootCursor);
        float maxBodyPitch = ReadSingle(rootCursor);
        float maxBodyRoll = ReadSingle(rootCursor);
        VehicleFakeBodyTuning fakeBody = ReadVehicleFakeBodyTuning(rootCursor);
        float collisionDamage = ReadSingle(rootCursor);
        float collisionSpeed = ReadSingle(rootCursor);
        VehicleVec3 killcamOffset = ReadVec3(rootCursor);
        int playerProtected = rootCursor.ReadInt32();
        int bulletDamage = rootCursor.ReadInt32();
        int armorPiercingDamage = rootCursor.ReadInt32();
        int grenadeDamage = rootCursor.ReadInt32();
        int projectileDamage = rootCursor.ReadInt32();
        int projectileSplashDamage = rootCursor.ReadInt32();
        int heavyExplosiveDamage = rootCursor.ReadInt32();
        VehiclePhysDef phys = ReadVehiclePhysDefRoot(rootCursor, context);
        float boostDuration = ReadSingle(rootCursor);
        float boostRechargeTime = ReadSingle(rootCursor);
        float boostAcceleration = ReadSingle(rootCursor);
        float suspensionTravel = ReadSingle(rootCursor);
        float maxSteeringAngle = ReadSingle(rootCursor);
        float steeringLerp = ReadSingle(rootCursor);
        float minSteeringScale = ReadSingle(rootCursor);
        float minSteeringSpeed = ReadSingle(rootCursor);
        int camLookEnabled = rootCursor.ReadInt32();
        float camLerp = ReadSingle(rootCursor);
        float camPitchInfluence = ReadSingle(rootCursor);
        float camRollInfluence = ReadSingle(rootCursor);
        float camFovIncrease = ReadSingle(rootCursor);
        float camFovOffset = ReadSingle(rootCursor);
        float camFovSpeed = ReadSingle(rootCursor);
        XString turretWeaponNamePointer = ReadXStringPointer(rootCursor, context);
        XPointer<WeaponAsset> turretWeaponPointer = ReadPointer<WeaponAsset>(rootCursor, context, XPointerResolutionMode.AliasCell);
        float turretHorizSpanLeft = ReadSingle(rootCursor);
        float turretHorizSpanRight = ReadSingle(rootCursor);
        float turretVertSpanUp = ReadSingle(rootCursor);
        float turretVertSpanDown = ReadSingle(rootCursor);
        float turretRotRate = ReadSingle(rootCursor);
        VehicleSoundAliasField turretSpinSoundRoot = ReadSoundAliasRoot(rootCursor, 0x1B4, context);
        VehicleSoundAliasField turretStopSoundRoot = ReadSoundAliasRoot(rootCursor, 0x1B8, context);
        int trophyEnabled = rootCursor.ReadInt32();
        float trophyRadius = ReadSingle(rootCursor);
        float trophyInactiveRadius = ReadSingle(rootCursor);
        int trophyAmmoCount = rootCursor.ReadInt32();
        float trophyReloadTime = ReadSingle(rootCursor);
        rootCursor.Skip(VehicleDefAsset.ScriptStringCount * sizeof(ushort));
        XBlockAddress scriptStringsAddress = rootAddress.Add(VehicleDefAsset.ScriptStringOffset);
        XPointer<MaterialAsset> compassFriendlyIconPointer = ReadPointer<MaterialAsset>(rootCursor, context, XPointerResolutionMode.AliasCell);
        XPointer<MaterialAsset> compassEnemyIconPointer = ReadPointer<MaterialAsset>(rootCursor, context, XPointerResolutionMode.AliasCell);
        float compassIconWidth = ReadSingle(rootCursor);
        float compassIconHeight = ReadSingle(rootCursor);
        VehicleEngineSoundFields engineSoundRoots = ReadEngineSoundRoots(rootCursor, context);
        VehicleSuspensionSoundFields suspensionSoundRoots = ReadSuspensionSoundRoots(rootCursor, context);
        VehicleSoundAliasField collisionSoundRoot = ReadSoundAliasRoot(rootCursor, 0x230, context);
        float collisionBlendSpeed = ReadSingle(rootCursor);
        VehicleSoundAliasField speedSoundRoot = ReadSoundAliasRoot(rootCursor, 0x238, context);
        float speedSoundBlendSpeed = ReadSingle(rootCursor);
        XString surfaceSoundPrefixPointer = ReadXStringPointer(rootCursor, context);
        IReadOnlyList<XString> surfaceSoundAliasPointers = ReadEmbeddedSoundAliasRoots(rootCursor, VehicleDefAsset.SurfaceSoundOffset, VehicleDefAsset.SurfaceSoundCount, context);
        float surfaceSoundBlendSpeed = ReadSingle(rootCursor);
        float slideVolume = ReadSingle(rootCursor);
        float slideBlendSpeed = ReadSingle(rootCursor);
        float inAirPitch = ReadSingle(rootCursor);
        if (rootCursor.Offset != VehicleDefAsset.SerializedSize)
            throw new InvalidDataException($"VehicleDef root parser stopped at 0x{rootCursor.Offset:X}, expected 0x{VehicleDefAsset.SerializedSize:X}.");

        string? name;
        string? useHintString;
        VehiclePhysDef resolvedPhys;
        string? turretWeaponName;
        WeaponAsset? turretWeapon;
        VehicleSoundAliasField turretSpinSound;
        VehicleSoundAliasField turretStopSound;
        IReadOnlyList<ushort> trophyTags;
        MaterialAsset? compassFriendlyIcon;
        MaterialAsset? compassEnemyIcon;
        VehicleEngineSoundFields engineSounds;
        VehicleSuspensionSoundFields suspensionSounds;
        VehicleSoundAliasField collisionSound;
        VehicleSoundAliasField speedSound;
        string? surfaceSoundPrefix;
        IReadOnlyList<string?> surfaceSoundAliases;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            useHintString = context.PointerReader.LoadXString(cursor, useHintPointer);
            resolvedPhys = ReadVehiclePhysDefChildren(cursor, phys, context);
            turretWeaponName = context.PointerReader.LoadXString(cursor, turretWeaponNamePointer);
            turretWeapon = _weaponLoader.LoadFromPointer(cursor, turretWeaponPointer.Untyped, context);
            turretSpinSound = ResolveSoundAliasField(cursor, turretSpinSoundRoot, context);
            turretStopSound = ResolveSoundAliasField(cursor, turretStopSoundRoot, context);
            trophyTags = ReadScriptStringArray(rootBytes, VehicleDefAsset.ScriptStringOffset, VehicleDefAsset.ScriptStringCount);
            compassFriendlyIcon = ReadMaterialPointer(cursor, compassFriendlyIconPointer.Untyped, context);
            compassEnemyIcon = ReadMaterialPointer(cursor, compassEnemyIconPointer.Untyped, context);
            engineSounds = ResolveEngineSoundFields(cursor, engineSoundRoots, context);
            suspensionSounds = ResolveSuspensionSoundFields(cursor, suspensionSoundRoots, context);
            collisionSound = ResolveSoundAliasField(cursor, collisionSoundRoot, context);
            speedSound = ResolveSoundAliasField(cursor, speedSoundRoot, context);
            surfaceSoundPrefix = context.PointerReader.LoadXString(cursor, surfaceSoundPrefixPointer);
            surfaceSoundAliases = ReadSoundAliasCellArray(cursor, surfaceSoundAliasPointers, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new VehicleDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Type = type,
            UseHintStringPointer = useHintPointer,
            UseHintString = useHintString,
            Health = health,
            QuadBarrel = quadBarrel,
            TexScrollScale = texScrollScale,
            TopSpeed = topSpeed,
            Accel = accel,
            RotRate = rotRate,
            RotAccel = rotAccel,
            MaxBodyPitch = maxBodyPitch,
            MaxBodyRoll = maxBodyRoll,
            FakeBody = fakeBody,
            CollisionDamage = collisionDamage,
            CollisionSpeed = collisionSpeed,
            KillcamOffset = killcamOffset,
            PlayerProtected = playerProtected,
            BulletDamage = bulletDamage,
            ArmorPiercingDamage = armorPiercingDamage,
            GrenadeDamage = grenadeDamage,
            ProjectileDamage = projectileDamage,
            ProjectileSplashDamage = projectileSplashDamage,
            HeavyExplosiveDamage = heavyExplosiveDamage,
            Phys = resolvedPhys,
            BoostDuration = boostDuration,
            BoostRechargeTime = boostRechargeTime,
            BoostAcceleration = boostAcceleration,
            SuspensionTravel = suspensionTravel,
            MaxSteeringAngle = maxSteeringAngle,
            SteeringLerp = steeringLerp,
            MinSteeringScale = minSteeringScale,
            MinSteeringSpeed = minSteeringSpeed,
            CamLookEnabled = camLookEnabled,
            CamLerp = camLerp,
            CamPitchInfluence = camPitchInfluence,
            CamRollInfluence = camRollInfluence,
            CamFovIncrease = camFovIncrease,
            CamFovOffset = camFovOffset,
            CamFovSpeed = camFovSpeed,
            TurretWeaponNamePointer = turretWeaponNamePointer,
            TurretWeaponName = turretWeaponName,
            TurretWeaponPointer = turretWeaponPointer,
            TurretWeapon = turretWeapon,
            TurretHorizSpanLeft = turretHorizSpanLeft,
            TurretHorizSpanRight = turretHorizSpanRight,
            TurretVertSpanUp = turretVertSpanUp,
            TurretVertSpanDown = turretVertSpanDown,
            TurretRotRate = turretRotRate,
            TurretSpinSound = turretSpinSound,
            TurretStopSound = turretStopSound,
            TrophyEnabled = trophyEnabled,
            TrophyRadius = trophyRadius,
            TrophyInactiveRadius = trophyInactiveRadius,
            TrophyAmmoCount = trophyAmmoCount,
            TrophyReloadTime = trophyReloadTime,
            ScriptStringsAddress = scriptStringsAddress,
            TrophyTags = trophyTags,
            CompassFriendlyIconPointer = compassFriendlyIconPointer,
            CompassFriendlyIcon = compassFriendlyIcon,
            CompassEnemyIconPointer = compassEnemyIconPointer,
            CompassEnemyIcon = compassEnemyIcon,
            CompassIconWidth = compassIconWidth,
            CompassIconHeight = compassIconHeight,
            EngineSounds = engineSounds,
            SuspensionSounds = suspensionSounds,
            CollisionSound = collisionSound,
            CollisionBlendSpeed = collisionBlendSpeed,
            SpeedSound = speedSound,
            SpeedSoundBlendSpeed = speedSoundBlendSpeed,
            SurfaceSoundPrefixPointer = surfaceSoundPrefixPointer,
            SurfaceSoundPrefix = surfaceSoundPrefix,
            SurfaceSoundAliasPointers = surfaceSoundAliasPointers,
            SurfaceSoundAliases = surfaceSoundAliases,
            SurfaceSoundBlendSpeed = surfaceSoundBlendSpeed,
            SlideVolume = slideVolume,
            SlideBlendSpeed = slideBlendSpeed,
            InAirPitch = inAirPitch
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        VehicleDefAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed Vehicle pointer has no destination cell.");

        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical Vehicle has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private static VehiclePhysDef ReadVehiclePhysDefRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        if (cursor.Offset != VehiclePhysDef.OffsetInVehicleDef)
            throw new InvalidDataException($"VehiclePhysDef root parser started at 0x{cursor.Offset:X}, expected 0x{VehiclePhysDef.OffsetInVehicleDef:X}.");

        int physicsEnabled = cursor.ReadInt32();
        XString physPresetNamePointer = ReadXStringPointer(cursor, context);
        XPointer<PhysPresetAsset> physPresetPointer = ReadPointer<PhysPresetAsset>(cursor, context, XPointerResolutionMode.AliasCell);
        XString accelGraphNamePointer = ReadXStringPointer(cursor, context);

        return new VehiclePhysDef
        {
            PhysicsEnabled = physicsEnabled,
            PhysPresetNamePointer = physPresetNamePointer,
            PhysPresetPointer = physPresetPointer,
            AccelGraphNamePointer = accelGraphNamePointer,
            SteeringAxle = (VehicleAxleType)cursor.ReadInt32(),
            PowerAxle = (VehicleAxleType)cursor.ReadInt32(),
            BrakingAxle = (VehicleAxleType)cursor.ReadInt32(),
            TopSpeed = ReadSingle(cursor),
            ReverseSpeed = ReadSingle(cursor),
            MaxVelocity = ReadSingle(cursor),
            MaxPitch = ReadSingle(cursor),
            MaxRoll = ReadSingle(cursor),
            SuspensionTravelFront = ReadSingle(cursor),
            SuspensionTravelRear = ReadSingle(cursor),
            SuspensionStrengthFront = ReadSingle(cursor),
            SuspensionDampingFront = ReadSingle(cursor),
            SuspensionStrengthRear = ReadSingle(cursor),
            SuspensionDampingRear = ReadSingle(cursor),
            FrictionBraking = ReadSingle(cursor),
            FrictionCoasting = ReadSingle(cursor),
            FrictionTopSpeed = ReadSingle(cursor),
            FrictionSide = ReadSingle(cursor),
            FrictionSideRear = ReadSingle(cursor),
            VelocityDependentSlip = ReadSingle(cursor),
            RollStability = ReadSingle(cursor),
            RollResistance = ReadSingle(cursor),
            PitchResistance = ReadSingle(cursor),
            YawResistance = ReadSingle(cursor),
            UprightStrengthPitch = ReadSingle(cursor),
            UprightStrengthRoll = ReadSingle(cursor),
            TargetAirPitch = ReadSingle(cursor),
            AirYawTorque = ReadSingle(cursor),
            AirPitchTorque = ReadSingle(cursor),
            MinimumMomentumForCollision = ReadSingle(cursor),
            CollisionLaunchForceScale = ReadSingle(cursor),
            WreckedMassScale = ReadSingle(cursor),
            WreckedBodyFriction = ReadSingle(cursor),
            MinimumJoltForNotify = ReadSingle(cursor),
            SlipThresholdFront = ReadSingle(cursor),
            SlipThresholdRear = ReadSingle(cursor),
            SlipFricScaleFront = ReadSingle(cursor),
            SlipFricScaleRear = ReadSingle(cursor),
            SlipFricRateFront = ReadSingle(cursor),
            SlipFricRateRear = ReadSingle(cursor),
            SlipYawTorque = ReadSingle(cursor)
        };
    }

    private VehiclePhysDef ReadVehiclePhysDefChildren(
        FastFileCursor cursor,
        VehiclePhysDef phys,
        DbLoadExecutionContext context)
    {
        string? physPresetName = context.PointerReader.LoadXString(cursor, phys.PhysPresetNamePointer);
        PhysPresetAsset? physPreset = _physPresetLoader.LoadFromPointer(cursor, phys.PhysPresetPointer.Untyped, context);
        string? accelGraphName = context.PointerReader.LoadXString(cursor, phys.AccelGraphNamePointer);

        return new VehiclePhysDef
        {
            PhysicsEnabled = phys.PhysicsEnabled,
            PhysPresetNamePointer = phys.PhysPresetNamePointer,
            PhysPresetName = physPresetName,
            PhysPresetPointer = phys.PhysPresetPointer,
            PhysPreset = physPreset,
            AccelGraphNamePointer = phys.AccelGraphNamePointer,
            AccelGraphName = accelGraphName,
            SteeringAxle = phys.SteeringAxle,
            PowerAxle = phys.PowerAxle,
            BrakingAxle = phys.BrakingAxle,
            TopSpeed = phys.TopSpeed,
            ReverseSpeed = phys.ReverseSpeed,
            MaxVelocity = phys.MaxVelocity,
            MaxPitch = phys.MaxPitch,
            MaxRoll = phys.MaxRoll,
            SuspensionTravelFront = phys.SuspensionTravelFront,
            SuspensionTravelRear = phys.SuspensionTravelRear,
            SuspensionStrengthFront = phys.SuspensionStrengthFront,
            SuspensionDampingFront = phys.SuspensionDampingFront,
            SuspensionStrengthRear = phys.SuspensionStrengthRear,
            SuspensionDampingRear = phys.SuspensionDampingRear,
            FrictionBraking = phys.FrictionBraking,
            FrictionCoasting = phys.FrictionCoasting,
            FrictionTopSpeed = phys.FrictionTopSpeed,
            FrictionSide = phys.FrictionSide,
            FrictionSideRear = phys.FrictionSideRear,
            VelocityDependentSlip = phys.VelocityDependentSlip,
            RollStability = phys.RollStability,
            RollResistance = phys.RollResistance,
            PitchResistance = phys.PitchResistance,
            YawResistance = phys.YawResistance,
            UprightStrengthPitch = phys.UprightStrengthPitch,
            UprightStrengthRoll = phys.UprightStrengthRoll,
            TargetAirPitch = phys.TargetAirPitch,
            AirYawTorque = phys.AirYawTorque,
            AirPitchTorque = phys.AirPitchTorque,
            MinimumMomentumForCollision = phys.MinimumMomentumForCollision,
            CollisionLaunchForceScale = phys.CollisionLaunchForceScale,
            WreckedMassScale = phys.WreckedMassScale,
            WreckedBodyFriction = phys.WreckedBodyFriction,
            MinimumJoltForNotify = phys.MinimumJoltForNotify,
            SlipThresholdFront = phys.SlipThresholdFront,
            SlipThresholdRear = phys.SlipThresholdRear,
            SlipFricScaleFront = phys.SlipFricScaleFront,
            SlipFricScaleRear = phys.SlipFricScaleRear,
            SlipFricRateFront = phys.SlipFricRateFront,
            SlipFricRateRear = phys.SlipFricRateRear,
            SlipYawTorque = phys.SlipYawTorque
        };
    }

    private MaterialAsset? ReadMaterialPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return _materialLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static VehicleFakeBodyTuning ReadVehicleFakeBodyTuning(FastFileCursor cursor)
    {
        return new VehicleFakeBodyTuning
        {
            AccelPitch = ReadSingle(cursor),
            AccelRoll = ReadSingle(cursor),
            VelPitch = ReadSingle(cursor),
            VelRoll = ReadSingle(cursor),
            SideVelPitch = ReadSingle(cursor),
            PitchStrength = ReadSingle(cursor),
            RollStrength = ReadSingle(cursor),
            PitchDampening = ReadSingle(cursor),
            RollDampening = ReadSingle(cursor),
            BoatRockingAmplitude = ReadSingle(cursor),
            BoatRockingPeriod = ReadSingle(cursor),
            BoatRockingRotationPeriod = ReadSingle(cursor),
            BoatRockingFadeoutSpeed = ReadSingle(cursor),
            BoatBouncingMinForce = ReadSingle(cursor),
            BoatBouncingMaxForce = ReadSingle(cursor),
            BoatBouncingRate = ReadSingle(cursor),
            BoatBouncingFadeinSpeed = ReadSingle(cursor),
            BoatBouncingFadeoutSteeringAngle = ReadSingle(cursor)
        };
    }

    private static VehicleEngineSoundFields ReadEngineSoundRoots(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new VehicleEngineSoundFields
        {
            IdleLowSound = ReadSoundAliasRoot(cursor, 0x1E8, context),
            IdleHighSound = ReadSoundAliasRoot(cursor, 0x1EC, context),
            EngineLowSound = ReadSoundAliasRoot(cursor, 0x1F0, context),
            EngineHighSound = ReadSoundAliasRoot(cursor, 0x1F4, context),
            EngineSoundSpeed = ReadSingle(cursor),
            EngineStartUpSound = ReadSoundAliasRoot(cursor, 0x1FC, context),
            EngineStartUpLength = ReadSingle(cursor),
            EngineShutdownSound = ReadSoundAliasRoot(cursor, 0x204, context),
            EngineIdleSound = ReadSoundAliasRoot(cursor, 0x208, context),
            EngineSustainSound = ReadSoundAliasRoot(cursor, 0x20C, context),
            EngineRampUpSound = ReadSoundAliasRoot(cursor, 0x210, context),
            EngineRampUpLength = ReadSingle(cursor),
            EngineRampDownSound = ReadSoundAliasRoot(cursor, 0x218, context),
            EngineRampDownLength = ReadSingle(cursor)
        };
    }

    private static VehicleEngineSoundFields ResolveEngineSoundFields(
        FastFileCursor cursor,
        VehicleEngineSoundFields fields,
        DbLoadExecutionContext context)
    {
        return new VehicleEngineSoundFields
        {
            IdleLowSound = ResolveSoundAliasField(cursor, fields.IdleLowSound, context),
            IdleHighSound = ResolveSoundAliasField(cursor, fields.IdleHighSound, context),
            EngineLowSound = ResolveSoundAliasField(cursor, fields.EngineLowSound, context),
            EngineHighSound = ResolveSoundAliasField(cursor, fields.EngineHighSound, context),
            EngineSoundSpeed = fields.EngineSoundSpeed,
            EngineStartUpSound = ResolveSoundAliasField(cursor, fields.EngineStartUpSound, context),
            EngineStartUpLength = fields.EngineStartUpLength,
            EngineShutdownSound = ResolveSoundAliasField(cursor, fields.EngineShutdownSound, context),
            EngineIdleSound = ResolveSoundAliasField(cursor, fields.EngineIdleSound, context),
            EngineSustainSound = ResolveSoundAliasField(cursor, fields.EngineSustainSound, context),
            EngineRampUpSound = ResolveSoundAliasField(cursor, fields.EngineRampUpSound, context),
            EngineRampUpLength = fields.EngineRampUpLength,
            EngineRampDownSound = ResolveSoundAliasField(cursor, fields.EngineRampDownSound, context),
            EngineRampDownLength = fields.EngineRampDownLength
        };
    }

    private static VehicleSuspensionSoundFields ReadSuspensionSoundRoots(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new VehicleSuspensionSoundFields
        {
            SuspensionSoftSound = ReadSoundAliasRoot(cursor, 0x220, context),
            SuspensionSoftCompression = ReadSingle(cursor),
            SuspensionHardSound = ReadSoundAliasRoot(cursor, 0x228, context),
            SuspensionHardCompression = ReadSingle(cursor)
        };
    }

    private static VehicleSuspensionSoundFields ResolveSuspensionSoundFields(
        FastFileCursor cursor,
        VehicleSuspensionSoundFields fields,
        DbLoadExecutionContext context)
    {
        return new VehicleSuspensionSoundFields
        {
            SuspensionSoftSound = ResolveSoundAliasField(cursor, fields.SuspensionSoftSound, context),
            SuspensionSoftCompression = fields.SuspensionSoftCompression,
            SuspensionHardSound = ResolveSoundAliasField(cursor, fields.SuspensionHardSound, context),
            SuspensionHardCompression = fields.SuspensionHardCompression
        };
    }

    private static VehicleSoundAliasField ReadSoundAliasRoot(FastFileCursor cursor, int offset, DbLoadExecutionContext context)
    {
        if (cursor.Offset != offset)
            throw new InvalidDataException($"Vehicle sound alias parser at 0x{cursor.Offset:X}, expected 0x{offset:X}.");

        return new VehicleSoundAliasField(offset, ReadXStringPointer(cursor, context), null);
    }

    private static VehicleSoundAliasField ResolveSoundAliasField(
        FastFileCursor cursor,
        VehicleSoundAliasField field,
        DbLoadExecutionContext context)
    {
        return field with { Value = ReadSoundAliasCell(cursor, field.Pointer, context) };
    }

    private static string? ReadSoundAliasCell(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        XPointerReference cellPointer = pointer.Untyped;
        if (cellPointer.Type == PointerType.Offset && cellPointer.PackedAddress is { } address)
            context.Blocks.ValidateMaterializedRange(address, sizeof(int), "snd_alias_list_name cell", cellPointer.Raw);

        if (cellPointer.Type == PointerType.Null || cellPointer.Type == PointerType.Offset)
            return null;

        if (cellPointer.Type is not PointerType.Inline)
            throw new NotSupportedException($"snd_alias_list_name cell 0x{cellPointer.Raw:X8} uses unsupported source sentinel {cellPointer.Type}.");

        // Align the destination before materializing the child XString cell;
        // the serialized source cursor remains contiguous.
        context.PointerReader.PatchInlinePointerCell(cellPointer, alignment: 4);
        byte[] nestedCellBytes = context.Blocks.Load(cursor, sizeof(int), out XBlockAddress nestedCellAddress);
        var nestedCellCursor = new FastFileCursor(nestedCellBytes, nestedCellAddress);
        XString nestedStringPointer = ReadXStringPointer(nestedCellCursor, context);
        return context.PointerReader.LoadXString(cursor, nestedStringPointer);
    }

    private static IReadOnlyList<XString> ReadEmbeddedSoundAliasRoots(
        FastFileCursor cursor,
        int offset,
        int count,
        DbLoadExecutionContext context)
    {
        if (cursor.Offset != offset)
            throw new InvalidDataException($"Vehicle surface sound parser at 0x{cursor.Offset:X}, expected 0x{offset:X}.");

        var pointers = new XString[count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadXStringPointer(cursor, context);

        return pointers;
    }

    private static IReadOnlyList<string?> ReadSoundAliasCellArray(
        FastFileCursor cursor,
        IReadOnlyList<XString> pointers,
        DbLoadExecutionContext context)
    {
        var values = new string?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
            values[i] = ReadSoundAliasCell(cursor, pointers[i], context);

        return values;
    }

    private static IReadOnlyList<ushort> ReadScriptStringArray(
        byte[] rootBytes,
        int offset,
        int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(rootBytes.AsSpan(offset + (i * sizeof(ushort)), sizeof(ushort)));

        return values;
    }

    private static VehicleVec3 ReadVec3(FastFileCursor cursor)
    {
        return new VehicleVec3(ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor));
    }

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadDeferredPointer<T>(cursor, mode);

    private static bool ResolveAliasCellOffset(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        int targetByteCount,
        string targetName)
    {
        if (pointer.Type != PointerType.Offset || pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            return false;

        if (pointer.CellAddress is not { } destinationCell)
            throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} has no destination cell to patch.");

        int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
        if (aliasedRaw != 0)
        {
            PointerType aliasedType = XPointerCodec.GetType(aliasedRaw);
            if (aliasedType != PointerType.Offset)
                throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} resolved to unresolved sentinel 0x{aliasedRaw:X8} for {targetName}.");

            context.PointerReader.ValidateOffsetPointerRange(
                XPointerReference.FromRaw(aliasedRaw, XPointerResolutionMode.Direct, pointer.PackedAddress),
                targetByteCount,
                targetName);
        }

        context.Blocks.WriteInt32(destinationCell, aliasedRaw);
        return true;
    }

}
