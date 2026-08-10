using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Fx;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.FastFiles.Loaders.Assets.Tracer;
using IW4.FastFiles.Loaders.Assets.XModel;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;
using MaterialAsset = IW4.Assets.Assets.Material.MaterialAsset;
using TracerDefAsset = IW4.Assets.Assets.Tracer.TracerDefAsset;
using XModelAsset = IW4.Assets.Assets.XModel.XModelAsset;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

public sealed class WeaponLoader
{
    private static readonly FxEffectDefLoader FxEffectDefLoader = new();
    private static readonly MaterialLoader MaterialLoader = new();
    private static readonly PhysCollmapLoader PhysCollmapLoader = new();
    private static readonly TracerDefLoader TracerDefLoader = new();
    private static readonly XModelLoader XModelLoader = new();

    public WeaponAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level Weapon pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<WeaponAsset>(
                pointer,
                WeaponAsset.SerializedSize,
                "Weapon");
            WeaponAsset canonical = context.ResolveWeapon(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level Weapon pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical Weapon asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed Weapon pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical Weapon has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Top-level Weapon pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            WeaponVariantRoot root = ReadWeaponVariantRoot(cursor, context);

            WeaponVariantDef variant;
            context.Blocks.Push(XFileBlockType.LARGE);
            try
            {
                variant = ReadWeaponVariantChildren(cursor, root, context);
            }
            finally
            {
                context.Blocks.Pop();
            }

            var weapon = new WeaponAsset
            {
                Offset = root.Offset,
                RuntimeAddress = rootAddress,
                Variant = variant
            };
            WeaponAsset canonical = context.DB_AddXAsset(weapon, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    public WeaponAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<WeaponAsset>(
                pointer,
                WeaponAsset.SerializedSize,
                "Weapon");
            WeaponAsset? canonical = context.ResolveWeapon(pointer);
            if (canonical is null)
                return null;

            if (pointer.CellAddress is { } pointerCellAddress)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical Weapon has no runtime address.");
                context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            }

            return canonical;
        }

        return LoadFromAssetPointer(cursor, pointer, context);
    }

    private static WeaponVariantRoot ReadWeaponVariantRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, WeaponVariantDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var root = new WeaponVariantRoot(
            Offset: offset,
            InternalNamePointer: ReadXStringPointer(rootCursor, context),
            DefinitionPointer: ReadPointer<WeaponDef>(rootCursor, context, XPointerResolutionMode.Direct),
            DisplayNamePointer: ReadXStringPointer(rootCursor, context),
            HideTagsPointer: ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct),
            AnimationNamesPointer: ReadPointer<XString[]>(rootCursor, context, XPointerResolutionMode.Direct),
            AdsZoomFov: ReadSingle(rootCursor),
            AdsTransitionInTime: rootCursor.ReadInt32(),
            AdsTransitionOutTime: rootCursor.ReadInt32(),
            ClipSize: rootCursor.ReadInt32(),
            ImpactType: rootCursor.ReadInt32(),
            FireTime: rootCursor.ReadInt32(),
            DpadIconRatio: rootCursor.ReadInt32(),
            PenetrateMultiplier: ReadSingle(rootCursor),
            AdsViewKickCenterSpeed: ReadSingle(rootCursor),
            HipViewKickCenterSpeed: ReadSingle(rootCursor),
            AlternateWeaponNamePointer: ReadXStringPointer(rootCursor, context),
            AlternateWeaponIndex: rootCursor.ReadUInt32(),
            AlternateRaiseTime: rootCursor.ReadInt32(),
            KillIconPointer: ReadPointer<MaterialAsset>(rootCursor, context, XPointerResolutionMode.AliasCell),
            DpadIconPointer: ReadPointer<MaterialAsset>(rootCursor, context, XPointerResolutionMode.AliasCell),
            DropAmmoMin: rootCursor.ReadInt32(),
            FirstRaiseTime: rootCursor.ReadInt32(),
            DropAmmoMax: rootCursor.ReadInt32(),
            AdsDofStart: ReadSingle(rootCursor),
            AdsDofEnd: ReadSingle(rootCursor),
            AccuracyGraphKnotCount: rootCursor.ReadUInt16(),
            OriginalAccuracyGraphKnotCount: rootCursor.ReadUInt16(),
            AccuracyGraphKnotsPointer: ReadPointer<Vec2[]>(rootCursor, context, XPointerResolutionMode.Direct),
            OriginalAccuracyGraphKnotsPointer: ReadPointer<Vec2[]>(rootCursor, context, XPointerResolutionMode.Direct),
            MotionTracker: rootCursor.ReadByte(),
            Enhanced: rootCursor.ReadByte(),
            DpadIconShowsAmmo: rootCursor.ReadByte(),
            Padding73: rootCursor.ReadByte());

        if (rootCursor.Offset != WeaponVariantDef.SerializedSize)
            throw new InvalidDataException($"WeaponVariantDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{WeaponVariantDef.SerializedSize:X}.");


        return root;
    }

    private static WeaponVariantDef ReadWeaponVariantChildren(
        FastFileCursor cursor,
        WeaponVariantRoot root,
        DbLoadExecutionContext context)
    {
        string? internalName = ReadXString(cursor, root.InternalNamePointer, context);
        WeaponDef? definition = ReadWeaponDefPointer(cursor, root.DefinitionPointer.Untyped, root, context);
        string? displayName = ReadXString(cursor, root.DisplayNamePointer, context);
        IReadOnlyList<ScriptStringReference> hideTags = ReadScriptStringArray(
            cursor,
            root.HideTagsPointer.Untyped,
            WeaponVariantDef.HideTagCount,
            "WeaponVariantDef.hideTags",
            context);
        XStringArrayPayload animations = ReadXStringArray(
            cursor,
            root.AnimationNamesPointer.Untyped,
            WeaponVariantDef.WeaponAnimCount,
            context);
        IReadOnlyList<XString> animationPointers = animations.Pointers;
        IReadOnlyList<string?> animationNames = animations.Values;
        string? alternateWeaponName = ReadXString(cursor, root.AlternateWeaponNamePointer, context);

        MaterialAsset? killIcon = ReadMaterialPointer(cursor, root.KillIconPointer.Untyped, "WeaponVariantDef.killIcon", context);
        MaterialAsset? dpadIcon = ReadMaterialPointer(cursor, root.DpadIconPointer.Untyped, "WeaponVariantDef.dpadIcon", context);

        IReadOnlyList<Vec2> accuracyGraphKnots = ReadVec2Array(cursor, root.AccuracyGraphKnotsPointer.Untyped, root.AccuracyGraphKnotCount, context);
        IReadOnlyList<Vec2> originalAccuracyGraphKnots = ReadVec2Array(cursor, root.OriginalAccuracyGraphKnotsPointer.Untyped, root.OriginalAccuracyGraphKnotCount, context);

        return new WeaponVariantDef
        {
            Offset = root.Offset,
            InternalNamePointer = root.InternalNamePointer,
            InternalName = internalName,
            DefinitionPointer = root.DefinitionPointer,
            Definition = definition,
            DisplayNamePointer = root.DisplayNamePointer,
            DisplayName = displayName,
            HideTagsPointer = root.HideTagsPointer,
            HideTags = hideTags,
            AnimationNamesPointer = root.AnimationNamesPointer,
            AnimationNamePointers = animationPointers,
            AnimationNames = animationNames,
            AdsZoomFov = root.AdsZoomFov,
            AdsTransitionInTime = root.AdsTransitionInTime,
            AdsTransitionOutTime = root.AdsTransitionOutTime,
            ClipSize = root.ClipSize,
            ImpactType = root.ImpactType,
            FireTime = root.FireTime,
            DpadIconRatio = root.DpadIconRatio,
            PenetrateMultiplier = root.PenetrateMultiplier,
            AdsViewKickCenterSpeed = root.AdsViewKickCenterSpeed,
            HipViewKickCenterSpeed = root.HipViewKickCenterSpeed,
            AlternateWeaponNamePointer = root.AlternateWeaponNamePointer,
            AlternateWeaponName = alternateWeaponName,
            AlternateWeaponIndex = root.AlternateWeaponIndex,
            AlternateRaiseTime = root.AlternateRaiseTime,
            KillIconPointer = root.KillIconPointer,
            DpadIconPointer = root.DpadIconPointer,
            KillIcon = killIcon,
            DpadIcon = dpadIcon,
            DropAmmoMin = root.DropAmmoMin,
            FirstRaiseTime = root.FirstRaiseTime,
            DropAmmoMax = root.DropAmmoMax,
            AdsDofStart = root.AdsDofStart,
            AdsDofEnd = root.AdsDofEnd,
            AccuracyGraphKnotCount = root.AccuracyGraphKnotCount,
            OriginalAccuracyGraphKnotCount = root.OriginalAccuracyGraphKnotCount,
            AccuracyGraphKnotsPointer = root.AccuracyGraphKnotsPointer,
            AccuracyGraphKnots = accuracyGraphKnots,
            OriginalAccuracyGraphKnotsPointer = root.OriginalAccuracyGraphKnotsPointer,
            OriginalAccuracyGraphKnots = originalAccuracyGraphKnots,
            MotionTracker = root.MotionTracker,
            Enhanced = root.Enhanced,
            DpadIconShowsAmmo = root.DpadIconShowsAmmo,
            Padding73 = root.Padding73
        };
    }

    private static WeaponDef? ReadWeaponDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        WeaponVariantRoot owner,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            ValidateOffsetPointerRange<WeaponDef>(
                pointer,
                WeaponDef.SerializedSize,
                "WeaponDef",
                context);
            return context.ResolveMaterializedDirect<WeaponDef>(pointer, "WeaponDef");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            throw new InvalidDataException(
                $"WeaponDef pointer 0x{unchecked((uint)pointer.Raw):X8} is not null, packed direct, or a supported inline sentinel.");
        }

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        WeaponDefRoot root = ReadWeaponDefRoot(cursor, context);
        if (root.Address != targetAddress)
        {
            throw new InvalidDataException(
                $"WeaponDef pointer patched to {targetAddress}, but root loaded at {root.Address}.");
        }

        WeaponDef definition = ReadWeaponDefChildren(cursor, root, owner, context);
        return context.RegisterMaterialized(targetAddress, definition, "WeaponDef");
    }

    private static WeaponDefRoot ReadWeaponDefRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, WeaponDef.SerializedSize, out XBlockAddress rootAddress);
        var c = new FastFileCursor(rootBytes, rootAddress);

        var root = new WeaponDefRoot
        {
            Offset = offset,
            Address = rootAddress,
            InternalNamePointer = ReadXStringPointer(c, context),
            GunModelsPointer = ReadPointer<XPointer<XModelAsset>[]>(c, context, XPointerResolutionMode.Direct),
            HandModelPointer = ReadPointer<XModelAsset>(c, context, XPointerResolutionMode.AliasCell),
            RightHandAnimationNamesPointer = ReadPointer<XString[]>(c, context, XPointerResolutionMode.Direct),
            LeftHandAnimationNamesPointer = ReadPointer<XString[]>(c, context, XPointerResolutionMode.Direct),
            ModeNamePointer = ReadXStringPointer(c, context),
            NoteTrackMaps = new WeaponNoteTrackMapPointers(
                ReadPointer<ushort[]>(c, context, XPointerResolutionMode.Direct),
                ReadPointer<ushort[]>(c, context, XPointerResolutionMode.Direct),
                ReadPointer<ushort[]>(c, context, XPointerResolutionMode.Direct),
                ReadPointer<ushort[]>(c, context, XPointerResolutionMode.Direct))
        };

        Seek(c, 0x028);
        root.PlayerAnimType = c.ReadInt32();
        root.WeaponType = (WeaponType)c.ReadInt32();
        root.WeaponClass = (WeaponClass)c.ReadInt32();
        root.PenetrateType = (PenetrateType)c.ReadInt32();
        root.InventoryType = (WeaponInventoryType)c.ReadInt32();
        root.FireType = (WeaponFireType)c.ReadInt32();
        root.OffhandClass = (OffhandClass)c.ReadInt32();
        root.Stance = (WeaponStance)c.ReadInt32();

        Seek(c, 0x048);
        root.FlashEffectPointers = ReadAliasPointerArray<FxEffectDefAsset>(c, 2, context);

        Seek(c, 0x050);
        root.SoundAliasPointers = ReadSoundAliasCellPointers(c, WeaponDef.WeaponSoundAliasCount, context);
        root.BounceSoundPointer = ReadPointer<XString[]>(c, context, XPointerResolutionMode.Direct);
        root.EffectPointers = ReadAliasPointerArray<FxEffectDefAsset>(c, 4, context);
        root.MaterialPointers = ReadAliasPointerArray<MaterialAsset>(c, 2, context);
        root.Reticle = new WeaponReticleFields
        {
            CenterSize = c.ReadInt32(),
            SideSize = c.ReadInt32(),
            MinOffset = c.ReadInt32(),
            ActiveType = (ActiveReticleType)c.ReadInt32()
        };
        root.ViewMovement = ReadViewMovementFields(c);
        root.PositionalMovement = new WeaponPositionalMovementFields
        {
            PositionMoveRate = ReadSingle(c),
            PositionProneMoveRate = ReadSingle(c),
            StandMoveMinSpeed = ReadSingle(c),
            DuckedMoveMinSpeed = ReadSingle(c),
            ProneMoveMinSpeed = ReadSingle(c),
            PositionRotationRate = ReadSingle(c),
            PositionProneRotationRate = ReadSingle(c),
            StandRotationMinSpeed = ReadSingle(c),
            DuckedRotationMinSpeed = ReadSingle(c),
            ProneRotationMinSpeed = ReadSingle(c)
        };

        Seek(c, 0x1d8);
        root.WorldGunModelsPointer = ReadPointer<XPointer<XModelAsset>[]>(c, context, XPointerResolutionMode.Direct);
        root.WorldModelPointers = ReadAliasPointerArray<XModelAsset>(c, 4, context);
        root.Icons = new WeaponIconPointers
        {
            HudIconPointer = ReadPointer<MaterialAsset>(c, context, XPointerResolutionMode.AliasCell),
            HudIconRatio = c.ReadInt32(),
            PickupIconPointer = ReadPointer<MaterialAsset>(c, context, XPointerResolutionMode.AliasCell),
            PickupIconRatio = c.ReadInt32(),
            AmmoCounterIconPointer = ReadPointer<MaterialAsset>(c, context, XPointerResolutionMode.AliasCell),
            AmmoCounterIconRatio = c.ReadInt32(),
            AmmoCounterClip = (AmmoCounterClipType)c.ReadInt32(),
            StartAmmo = c.ReadInt32()
        };

        Seek(c, 0x20c);
        root.Ammo = new WeaponAmmoFields
        {
            AmmoNamePointer = ReadXStringPointer(c, context),
            AmmoIndex = c.ReadInt32(),
            ClipNamePointer = ReadXStringPointer(c, context),
            ClipIndex = c.ReadInt32(),
            MaxAmmo = c.ReadInt32(),
            ShotCount = c.ReadInt32(),
            SharedAmmoCapNamePointer = ReadXStringPointer(c, context),
            SharedAmmoCapIndex = c.ReadInt32(),
            SharedAmmoCap = c.ReadInt32(),
            Damage = c.ReadInt32(),
            PlayerDamage = c.ReadInt32(),
            MeleeDamage = c.ReadInt32(),
            DamageType = c.ReadInt32()
        };
        root.Timing = ReadWeaponTimingFields(c);
        root.AimMovementTuning = ReadAimMovementTuningFields(c);

        Seek(c, 0x308);
        root.Overlay = new WeaponOverlayFields
        {
            OverlayMaterials = ReadAliasPointerArray<MaterialAsset>(c, 4, context),
            Reticle = (WeaponOverlayReticle)c.ReadInt32(),
            Interface = (WeaponOverlayInterface)c.ReadInt32(),
            Width = c.ReadInt32(),
            Height = c.ReadInt32(),
            WidthSplitscreen = c.ReadInt32(),
            HeightSplitscreen = c.ReadInt32()
        };
        root.AdsViewAndSpread = ReadAdsViewAndSpreadFields(c);

        Seek(c, 0x3c8);
        root.PhysCollmapPointer = ReadPointer<PhysCollmapAsset>(c, context, XPointerResolutionMode.AliasCell);
        root.Physics = ReadWeaponPhysicsFields(c);

        Seek(c, 0x420);
        root.ProjectileModelPointer = ReadPointer<XModelAsset>(c, context, XPointerResolutionMode.AliasCell);
        root.ProjectileModelField = c.ReadInt32();
        root.ProjectileEffectPointers = ReadAliasPointerArray<FxEffectDefAsset>(c, 2, context);
        root.ProjectileSoundAliasPointers = ReadSoundAliasCellPointers(c, 2, context);
        root.ProjectileFieldsA = ReadInt32Array(c, 3);
        root.ParallelBouncePointer = ReadPointer<float[]>(c, context, XPointerResolutionMode.Direct);
        root.PerpendicularBouncePointer = ReadPointer<float[]>(c, context, XPointerResolutionMode.Direct);
        root.ImpactEffectPointers = ReadAliasPointerArray<FxEffectDefAsset>(c, 2, context);
        root.ImpactFieldsA = ReadInt32Array(c, 3);
        root.ImpactFieldB = c.ReadInt32();
        root.ImpactFieldsC = ReadInt32Array(c, 2);
        root.ViewShellEjectEffectPointer = ReadPointer<FxEffectDefAsset>(c, context, XPointerResolutionMode.AliasCell);
        root.ShellEjectSoundPointer = ReadXStringPointer(c, context);
        root.ShellEjectFields = ReadInt32Array(c, 3);
        root.AdsHipGunKickAiDistanceFields = ReadInt32Array(c, 35);

        Seek(c, 0x50c);
        root.AccuracyGraphName0Pointer = ReadXStringPointer(c, context);
        root.AccuracyGraphName1Pointer = ReadXStringPointer(c, context);
        root.AccuracyGraphKnotsPointer = ReadPointer<Vec2[]>(c, context, XPointerResolutionMode.Direct);
        root.OriginalAccuracyGraphKnotsPointer = ReadPointer<Vec2[]>(c, context, XPointerResolutionMode.Direct);
        root.LocalGraphKnotCount = c.ReadUInt16();
        root.LocalOriginalGraphKnotCount = c.ReadUInt16();
        root.AnimationNotifyComparison = c.ReadInt32();
        root.LeftArc = ReadSingle(c);
        root.RightArc = ReadSingle(c);
        root.TopArc = ReadSingle(c);
        root.BottomArc = ReadSingle(c);
        root.Accuracy = ReadSingle(c);
        root.AiSpread = ReadSingle(c);
        root.PlayerSpread = ReadSingle(c);
        root.TurnSpeedAndRange = ReadWeaponTurnSpeedAndRangeFields(c);

        root.UseHintStringPointer = ReadXStringPointer(c, context);
        root.DropHintStringPointer = ReadXStringPointer(c, context);
        root.UseHintStringIndex = c.ReadInt32();
        root.DropHintStringIndex = c.ReadInt32();
        root.HorizontalViewJitter = ReadSingle(c);
        root.VerticalViewJitter = ReadSingle(c);
        root.ScanSpeed = ReadSingle(c);
        root.ScanAcceleration = ReadSingle(c);
        root.ScanPauseTime = c.ReadInt32();

        root.ScriptNamePointer = ReadXStringPointer(c, context);
        root.OOPosAnimLength = ReadSingle(c);
        root.MinDamage = ReadSingle(c);
        root.MinPlayerDamage = c.ReadInt32();
        root.MaxDamageRange = ReadSingle(c);
        root.MinDamageRange = ReadSingle(c);
        root.DestabilizationRateTime = ReadSingle(c);
        root.DestabilizationCurvatureMax = ReadSingle(c);
        root.DestabilizeDistance = ReadSingle(c);
        root.DestabilizeDistanceToTimeScale = c.ReadInt32();

        root.LocationDamageMultipliersPointer = ReadPointer<float[]>(c, context, XPointerResolutionMode.Direct);
        root.FireRumblePointer = ReadXStringPointer(c, context);
        root.MeleeImpactRumblePointer = ReadXStringPointer(c, context);
        root.TracerPointer = ReadPointer<TracerDefAsset>(c, context, XPointerResolutionMode.AliasCell);
        root.TurretScopeZoomRate = ReadSingle(c);
        root.TurretScopeZoomMin = ReadSingle(c);
        root.TurretScopeZoomMax = ReadSingle(c);
        root.TurretOverheatUpRate = ReadSingle(c);
        root.TurretOverheatDownRate = ReadSingle(c);
        root.TurretOverheatPenalty = ReadSingle(c);

        root.TurretOverheatSoundPointer = ReadXStringPointer(c, context);
        root.TurretOverheatEffectPointer = ReadPointer<FxEffectDefAsset>(c, context, XPointerResolutionMode.AliasCell);
        root.TurretBarrelSpinRumblePointer = ReadXStringPointer(c, context);
        root.TurretBarrelSpinSpeed = ReadSingle(c);
        root.TurretBarrelSpinUpTime = ReadSingle(c);
        root.TurretBarrelSpinDownTime = ReadSingle(c);
        root.TurretBarrelSpinMaxSoundPointer = ReadXStringPointer(c, context);
        root.TurretBarrelSpinUpSoundPointers = ReadSoundAliasCellPointers(c, WeaponDef.TurretBarrelSpinSoundCount, context);
        root.TurretBarrelSpinDownSoundPointers = ReadSoundAliasCellPointers(c, WeaponDef.TurretBarrelSpinSoundCount, context);

        root.MissileConeSoundAliasPointer = ReadXStringPointer(c, context);
        root.MissileConeSoundAliasAtBasePointer = ReadXStringPointer(c, context);
        root.MissileConeFloats = ReadSingleArray(c, 13);

        Seek(c, 0x654);
        root.TailFlags = ReadTailFlags(c);

        if (c.Offset != WeaponDef.SerializedSize)
            throw new InvalidDataException($"WeaponDef consumed 0x{c.Offset:X} bytes instead of 0x{WeaponDef.SerializedSize:X}.");


        return root;
    }

    private static WeaponDef ReadWeaponDefChildren(
        FastFileCursor cursor,
        WeaponDefRoot root,
        WeaponVariantRoot owner,
        DbLoadExecutionContext context)
    {
        string? internalName = ReadXString(cursor, root.InternalNamePointer, context);
        IReadOnlyList<XPointer<XModelAsset>> gunModelPointers = ReadXModelPointerArray(
            cursor,
            root.GunModelsPointer.Untyped,
            WeaponDef.GunModelCount,
            context,
            out IReadOnlyList<XModelAsset?> gunModels);
        XModelAsset? handModel = ReadXModelPointer(cursor, root.HandModelPointer.Untyped, context);
        XStringArrayPayload rightHandAnimations = ReadXStringArray(
            cursor,
            root.RightHandAnimationNamesPointer.Untyped,
            WeaponDef.WeaponAnimCount,
            context);
        IReadOnlyList<XString> rightHandAnimationNames = rightHandAnimations.Pointers;
        IReadOnlyList<string?> rightHandAnimationNameValues = rightHandAnimations.Values;
        XStringArrayPayload leftHandAnimations = ReadXStringArray(
            cursor,
            root.LeftHandAnimationNamesPointer.Untyped,
            WeaponDef.WeaponAnimCount,
            context);
        IReadOnlyList<XString> leftHandAnimationNames = leftHandAnimations.Pointers;
        IReadOnlyList<string?> leftHandAnimationNameValues = leftHandAnimations.Values;
        string? modeName = ReadXString(cursor, root.ModeNamePointer, context);

        WeaponNoteTrackMaps noteTrackMaps = new()
        {
            SoundMapKeysPointer = root.NoteTrackMaps.SoundMapKeysPointer,
            SoundMapKeys = ReadScriptStringArray(
                cursor,
                root.NoteTrackMaps.SoundMapKeysPointer.Untyped,
                WeaponDef.NoteTrackMapCount,
                "WeaponDef.notetrackSoundMapKeys",
                context),
            SoundMapValuesPointer = root.NoteTrackMaps.SoundMapValuesPointer,
            SoundMapValues = ReadScriptStringArray(
                cursor,
                root.NoteTrackMaps.SoundMapValuesPointer.Untyped,
                WeaponDef.NoteTrackMapCount,
                "WeaponDef.notetrackSoundMapValues",
                context),
            RumbleMapKeysPointer = root.NoteTrackMaps.RumbleMapKeysPointer,
            RumbleMapKeys = ReadScriptStringArray(
                cursor,
                root.NoteTrackMaps.RumbleMapKeysPointer.Untyped,
                WeaponDef.NoteTrackMapCount,
                "WeaponDef.notetrackRumbleMapKeys",
                context),
            RumbleMapValuesPointer = root.NoteTrackMaps.RumbleMapValuesPointer,
            RumbleMapValues = ReadScriptStringArray(
                cursor,
                root.NoteTrackMaps.RumbleMapValuesPointer.Untyped,
                WeaponDef.NoteTrackMapCount,
                "WeaponDef.notetrackRumbleMapValues",
                context)
        };

        IReadOnlyList<FxEffectDefAsset?> flashEffects = ReadFxPointers(cursor, root.FlashEffectPointers, context);
        IReadOnlyList<string?> soundAliasNames = ReadSoundAliasCells(cursor, root.SoundAliasPointers, context);
        SoundAliasCellArrayPayload bounceSounds = ReadSoundAliasCellArray(cursor, root.BounceSoundPointer.Untyped, WeaponDef.SurfaceCount, context);
        IReadOnlyList<FxEffectDefAsset?> effects = ReadFxPointers(cursor, root.EffectPointers, context);
        IReadOnlyList<MaterialAsset?> materials = ReadMaterialPointers(cursor, root.MaterialPointers, "WeaponDef.materialPointers", context);

        IReadOnlyList<XPointer<XModelAsset>> worldGunModelPointers = ReadXModelPointerArray(
            cursor,
            root.WorldGunModelsPointer.Untyped,
            WeaponDef.GunModelCount,
            context,
            out IReadOnlyList<XModelAsset?> worldGunModels);
        IReadOnlyList<XModelAsset?> worldModels = ReadXModelPointers(cursor, root.WorldModelPointers, context);
        MaterialAsset? hudIcon = ReadMaterialPointer(cursor, root.Icons.HudIconPointer.Untyped, "WeaponDef.icons.hudIcon", context);
        MaterialAsset? pickupIcon = ReadMaterialPointer(cursor, root.Icons.PickupIconPointer.Untyped, "WeaponDef.icons.pickupIcon", context);
        MaterialAsset? ammoCounterIcon = ReadMaterialPointer(cursor, root.Icons.AmmoCounterIconPointer.Untyped, "WeaponDef.icons.ammoCounterIcon", context);

        string? ammoName = ReadXString(cursor, root.Ammo.AmmoNamePointer, context);
        string? clipName = ReadXString(cursor, root.Ammo.ClipNamePointer, context);
        string? sharedAmmoCapName = ReadXString(cursor, root.Ammo.SharedAmmoCapNamePointer, context);
        IReadOnlyList<MaterialAsset?> overlayMaterials = ReadMaterialPointers(cursor, root.Overlay.OverlayMaterials, "WeaponDef.overlayMaterials", context);
        IW4.Assets.Assets.Physics.PhysCollmapAsset? physCollmap = ReadPhysCollmapPointer(cursor, root.PhysCollmapPointer.Untyped, context);

        XModelAsset? projectileModel = ReadXModelPointer(cursor, root.ProjectileModelPointer.Untyped, context);
        IReadOnlyList<FxEffectDefAsset?> projectileEffects = ReadFxPointers(cursor, root.ProjectileEffectPointers, context);
        IReadOnlyList<string?> projectileSoundAliasNames = ReadSoundAliasCells(cursor, root.ProjectileSoundAliasPointers, context);
        IReadOnlyList<float> parallelBounce = ReadFloatArray(cursor, root.ParallelBouncePointer.Untyped, WeaponDef.SurfaceCount, context);
        IReadOnlyList<float> perpendicularBounce = ReadFloatArray(cursor, root.PerpendicularBouncePointer.Untyped, WeaponDef.SurfaceCount, context);
        IReadOnlyList<FxEffectDefAsset?> impactEffects = ReadFxPointers(cursor, root.ImpactEffectPointers, context);
        FxEffectDefAsset? viewShellEjectEffect = ReadFxPointer(cursor, root.ViewShellEjectEffectPointer.Untyped, context);
        string? shellEjectSound = ReadSoundAliasCell(cursor, root.ShellEjectSoundPointer, context);

        string? graphName0 = ReadXString(cursor, root.AccuracyGraphName0Pointer, context);
        IReadOnlyList<Vec2> graphKnots = ReadVec2Array(cursor, root.AccuracyGraphKnotsPointer.Untyped, owner.AccuracyGraphKnotCount, context);
        string? graphName1 = ReadXString(cursor, root.AccuracyGraphName1Pointer, context);
        IReadOnlyList<Vec2> originalGraphKnots = ReadVec2Array(cursor, root.OriginalAccuracyGraphKnotsPointer.Untyped, owner.OriginalAccuracyGraphKnotCount, context);

        string? useHintString = ReadXString(cursor, root.UseHintStringPointer, context);
        string? dropHintString = ReadXString(cursor, root.DropHintStringPointer, context);
        string? scriptName = ReadXString(cursor, root.ScriptNamePointer, context);
        IReadOnlyList<float> locationDamageMultipliers = ReadFloatArray(cursor, root.LocationDamageMultipliersPointer.Untyped, WeaponDef.HitLocationCount, context);
        string? fireRumble = ReadXString(cursor, root.FireRumblePointer, context);
        string? meleeImpactRumble = ReadXString(cursor, root.MeleeImpactRumblePointer, context);
        TracerDefAsset? tracer = ReadTracerPointer(cursor, root.TracerPointer.Untyped, context);

        string? turretOverheatSound = ReadSoundAliasCell(cursor, root.TurretOverheatSoundPointer, context);
        FxEffectDefAsset? turretOverheatEffect = ReadFxPointer(cursor, root.TurretOverheatEffectPointer.Untyped, context);
        string? turretBarrelSpinRumble = ReadXString(cursor, root.TurretBarrelSpinRumblePointer, context);
        string? turretBarrelSpinMaxSound = ReadSoundAliasCell(cursor, root.TurretBarrelSpinMaxSoundPointer, context);
        IReadOnlyList<string?> barrelSpinUpSoundNames = ReadSoundAliasCells(cursor, root.TurretBarrelSpinUpSoundPointers, context);
        IReadOnlyList<string?> barrelSpinDownSoundNames = ReadSoundAliasCells(cursor, root.TurretBarrelSpinDownSoundPointers, context);
        string? missileConeSoundAlias = ReadSoundAliasCell(cursor, root.MissileConeSoundAliasPointer, context);
        string? missileConeSoundAliasAtBase = ReadSoundAliasCell(cursor, root.MissileConeSoundAliasAtBasePointer, context);

        return new WeaponDef
        {
            Offset = root.Offset,
            InternalNamePointer = root.InternalNamePointer,
            InternalName = internalName,
            GunModelsPointer = root.GunModelsPointer,
            GunModelPointers = gunModelPointers,
            GunModels = gunModels,
            HandModelPointer = root.HandModelPointer,
            HandModel = handModel,
            RightHandAnimationNamesPointer = root.RightHandAnimationNamesPointer,
            RightHandAnimationNamePointers = rightHandAnimationNames,
            RightHandAnimationNames = rightHandAnimationNameValues,
            LeftHandAnimationNamesPointer = root.LeftHandAnimationNamesPointer,
            LeftHandAnimationNamePointers = leftHandAnimationNames,
            LeftHandAnimationNames = leftHandAnimationNameValues,
            ModeNamePointer = root.ModeNamePointer,
            ModeName = modeName,
            NoteTrackMaps = noteTrackMaps,
            PlayerAnimType = root.PlayerAnimType,
            WeaponType = root.WeaponType,
            WeaponClass = root.WeaponClass,
            PenetrateType = root.PenetrateType,
            InventoryType = root.InventoryType,
            FireType = root.FireType,
            OffhandClass = root.OffhandClass,
            Stance = root.Stance,
            FlashEffectPointers = root.FlashEffectPointers,
            FlashEffects = flashEffects,
            SoundAliasPointers = root.SoundAliasPointers,
            SoundAliasNames = soundAliasNames,
            BounceSoundPointer = root.BounceSoundPointer,
            BounceSoundPointers = bounceSounds.Pointers,
            BounceSoundNames = bounceSounds.Values,
            EffectPointers = root.EffectPointers,
            Effects = effects,
            MaterialPointers = root.MaterialPointers,
            Materials = materials,
            Reticle = root.Reticle,
            ViewMovement = root.ViewMovement,
            PositionalMovement = root.PositionalMovement,
            WorldGunModelsPointer = root.WorldGunModelsPointer,
            WorldGunModelPointers = worldGunModelPointers,
            WorldGunModels = worldGunModels,
            WorldModelPointers = root.WorldModelPointers,
            WorldModels = worldModels,
            Icons = root.Icons,
            IconMaterials = [hudIcon, pickupIcon, ammoCounterIcon],
            Ammo = new WeaponAmmoFields
            {
                AmmoNamePointer = root.Ammo.AmmoNamePointer,
                AmmoName = ammoName,
                AmmoIndex = root.Ammo.AmmoIndex,
                ClipNamePointer = root.Ammo.ClipNamePointer,
                ClipName = clipName,
                ClipIndex = root.Ammo.ClipIndex,
                MaxAmmo = root.Ammo.MaxAmmo,
                ShotCount = root.Ammo.ShotCount,
                SharedAmmoCapNamePointer = root.Ammo.SharedAmmoCapNamePointer,
                SharedAmmoCapName = sharedAmmoCapName,
                SharedAmmoCapIndex = root.Ammo.SharedAmmoCapIndex,
                SharedAmmoCap = root.Ammo.SharedAmmoCap,
                Damage = root.Ammo.Damage,
                PlayerDamage = root.Ammo.PlayerDamage,
                MeleeDamage = root.Ammo.MeleeDamage,
                DamageType = root.Ammo.DamageType
            },
            Overlay = root.Overlay,
            OverlayMaterials = overlayMaterials,
            Timing = root.Timing,
            AimMovementTuning = root.AimMovementTuning,
            AdsViewAndSpread = root.AdsViewAndSpread,
            PhysCollmapPointer = root.PhysCollmapPointer,
            PhysCollmapName = physCollmap?.Name,
            Physics = root.Physics,
            Projectile = new WeaponProjectileFields
            {
                ModelPointer = root.ProjectileModelPointer,
                Model = projectileModel,
                Explosion = (WeaponProjectileExplosion)root.ProjectileModelField,
                ExplosionEffectPointer = root.ProjectileEffectPointers[0],
                DudEffectPointer = root.ProjectileEffectPointers[1],
                ExplosionSoundPointer = root.ProjectileSoundAliasPointers[0],
                ExplosionSound = projectileSoundAliasNames[0],
                DudSoundPointer = root.ProjectileSoundAliasPointers[1],
                DudSound = projectileSoundAliasNames[1],
                Stickiness = (WeaponStickiness)root.ProjectileFieldsA[0],
                LowAmmoWarningThreshold = root.ProjectileFieldsA[1],
                RicochetChance = SingleFromRawInt(root.ProjectileFieldsA[2]),
                ParallelBouncePointer = root.ParallelBouncePointer,
                ParallelBounce = parallelBounce,
                PerpendicularBouncePointer = root.PerpendicularBouncePointer,
                PerpendicularBounce = perpendicularBounce,
                TrailEffectPointer = root.ImpactEffectPointers[0],
                BeaconEffectPointer = root.ImpactEffectPointers[1],
                ProjectileColor = Vec3FromRawInts(root.ImpactFieldsA),
                GuidedMissileType = (GuidedMissileType)root.ImpactFieldB,
                MaxSteeringAcceleration = SingleFromRawInt(root.ImpactFieldsC[0]),
                IgnitionDelay = root.ImpactFieldsC[1],
                IgnitionEffectPointer = root.ViewShellEjectEffectPointer,
                IgnitionSoundPointer = root.ShellEjectSoundPointer,
                IgnitionSound = shellEjectSound,
                AdsAimPitch = SingleFromRawInt(root.ShellEjectFields[0]),
                AdsCrosshairInFraction = SingleFromRawInt(root.ShellEjectFields[1]),
                AdsCrosshairOutFraction = SingleFromRawInt(root.ShellEjectFields[2]),
                GunKickAndDistance = GunKickAndDistanceFromRawInts(root.AdsHipGunKickAiDistanceFields)
            },
            ProjectileEffects = projectileEffects,
            ImpactEffects = impactEffects,
            ViewShellEjectEffect = viewShellEjectEffect,
            Accuracy = new WeaponAccuracyFields
            {
                GraphName0Pointer = root.AccuracyGraphName0Pointer,
                GraphName0 = graphName0,
                GraphName1Pointer = root.AccuracyGraphName1Pointer,
                GraphName1 = graphName1,
                GraphKnotsPointer = root.AccuracyGraphKnotsPointer,
                GraphKnots = graphKnots,
                OriginalGraphKnotsPointer = root.OriginalAccuracyGraphKnotsPointer,
                OriginalGraphKnots = originalGraphKnots,
                LocalGraphKnotCount = root.LocalGraphKnotCount,
                LocalOriginalGraphKnotCount = root.LocalOriginalGraphKnotCount,
                AnimationNotifyComparison = root.AnimationNotifyComparison,
                LeftArc = root.LeftArc,
                RightArc = root.RightArc,
                TopArc = root.TopArc,
                BottomArc = root.BottomArc,
                Accuracy = root.Accuracy,
                AiSpread = root.AiSpread,
                PlayerSpread = root.PlayerSpread
            },
            TurnSpeedAndRange = root.TurnSpeedAndRange,
            Hints = new WeaponHintFields
            {
                UseHintStringPointer = root.UseHintStringPointer,
                UseHintString = useHintString,
                DropHintStringPointer = root.DropHintStringPointer,
                DropHintString = dropHintString,
                UseHintStringIndex = root.UseHintStringIndex,
                DropHintStringIndex = root.DropHintStringIndex,
                HorizontalViewJitter = root.HorizontalViewJitter,
                VerticalViewJitter = root.VerticalViewJitter,
                ScanSpeed = root.ScanSpeed,
                ScanAcceleration = root.ScanAcceleration,
                ScanPauseTime = root.ScanPauseTime
            },
            ScriptNamePointer = root.ScriptNamePointer,
            ScriptName = scriptName,
            OOPosAnimLength = root.OOPosAnimLength,
            MinDamage = root.MinDamage,
            MinPlayerDamage = root.MinPlayerDamage,
            MaxDamageRange = root.MaxDamageRange,
            MinDamageRange = root.MinDamageRange,
            DestabilizationRateTime = root.DestabilizationRateTime,
            DestabilizationCurvatureMax = root.DestabilizationCurvatureMax,
            DestabilizeDistance = root.DestabilizeDistance,
            DestabilizeDistanceToTimeScale = root.DestabilizeDistanceToTimeScale,
            LocationDamageMultipliersPointer = root.LocationDamageMultipliersPointer,
            LocationDamageMultipliers = locationDamageMultipliers,
            Rumble = new WeaponRumbleFields
            {
                FireRumblePointer = root.FireRumblePointer,
                FireRumble = fireRumble,
                MeleeImpactRumblePointer = root.MeleeImpactRumblePointer,
                MeleeImpactRumble = meleeImpactRumble
            },
            TracerPointer = root.TracerPointer,
            Tracer = tracer,
            TurretScopeZoomRate = root.TurretScopeZoomRate,
            TurretScopeZoomMin = root.TurretScopeZoomMin,
            TurretScopeZoomMax = root.TurretScopeZoomMax,
            TurretOverheatUpRate = root.TurretOverheatUpRate,
            TurretOverheatDownRate = root.TurretOverheatDownRate,
            TurretOverheatPenalty = root.TurretOverheatPenalty,
            Turret = new WeaponTurretFields
            {
                OverheatSoundPointer = root.TurretOverheatSoundPointer,
                OverheatSound = turretOverheatSound,
                OverheatEffectPointer = root.TurretOverheatEffectPointer,
                BarrelSpinRumblePointer = root.TurretBarrelSpinRumblePointer,
                BarrelSpinRumble = turretBarrelSpinRumble,
                BarrelSpinSpeed = root.TurretBarrelSpinSpeed,
                BarrelSpinUpTime = root.TurretBarrelSpinUpTime,
                BarrelSpinDownTime = root.TurretBarrelSpinDownTime,
                BarrelSpinMaxSoundPointer = root.TurretBarrelSpinMaxSoundPointer,
                BarrelSpinMaxSound = turretBarrelSpinMaxSound,
                BarrelSpinUpSoundPointers = root.TurretBarrelSpinUpSoundPointers,
                BarrelSpinUpSoundNames = barrelSpinUpSoundNames,
                BarrelSpinDownSoundPointers = root.TurretBarrelSpinDownSoundPointers,
                BarrelSpinDownSoundNames = barrelSpinDownSoundNames
            },
            TurretOverheatEffect = turretOverheatEffect,
            MissileConeSound = new WeaponMissileConeSoundFields
            {
                AliasPointer = root.MissileConeSoundAliasPointer,
                Alias = missileConeSoundAlias,
                AliasAtBasePointer = root.MissileConeSoundAliasAtBasePointer,
                AliasAtBase = missileConeSoundAliasAtBase,
                RadiusAtTop = root.MissileConeFloats[0],
                RadiusAtBase = root.MissileConeFloats[1],
                Height = root.MissileConeFloats[2],
                OriginOffset = root.MissileConeFloats[3],
                VolumeScaleAtCore = root.MissileConeFloats[4],
                VolumeScaleAtEdge = root.MissileConeFloats[5],
                VolumeScaleCoreSize = root.MissileConeFloats[6],
                PitchAtTop = root.MissileConeFloats[7],
                PitchAtBottom = root.MissileConeFloats[8],
                PitchTopSize = root.MissileConeFloats[9],
                PitchBottomSize = root.MissileConeFloats[10],
                CrossfadeTopSize = root.MissileConeFloats[11],
                CrossfadeBottomSize = root.MissileConeFloats[12]
            },
            TailFlags = root.TailFlags
        };
    }

    private static IReadOnlyList<XPointer<XModelAsset>> ReadXModelPointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out IReadOnlyList<XModelAsset?> models)
    {
        string view = $"XModelPtr[{count}]";
        if (pointer.Type == PointerType.Offset &&
            pointer.PackedAddress is { } existingAddress &&
            context.TryGetMaterializedView<XModelPointerArrayPayload>(
                existingAddress,
                view,
                out XModelPointerArrayPayload? existing) &&
            existing is not null)
        {
            models = existing.Models;
            return existing.Pointers;
        }
        IReadOnlyList<XPointer<XModelAsset>> pointers =
            ReadAliasPointerArrayPayload<XModelAsset>(
                cursor,
                pointer,
                count,
                context,
                out XBlockAddress tableAddress);
        models = ReadXModelPointers(cursor, pointers, context);
        if (pointer.Type != PointerType.Null)
        {
            XModelPointerArrayPayload payload = context.RegisterMaterializedView(
                tableAddress,
                view,
                new XModelPointerArrayPayload(pointers, models),
                view);
            models = payload.Models;
            return payload.Pointers;
        }
        return pointers;
    }

    private static IReadOnlyList<XPointer<T>> ReadAliasPointerArrayPayload<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress tableAddress)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative alias pointer array count {count}.");

        int byteCount = checked(count * sizeof(int));
        if (pointer.Type == PointerType.Null)
        {
            tableAddress = default;
            return [];
        }

        byte[] pointerBytes;
        if (!TryReadDirectOffsetPayload<XPointer<T>[]>(
                pointer,
                byteCount,
                $"{typeof(T).Name}*[]",
                context,
                out pointerBytes,
                out tableAddress))
        {
            RequireInlinePayload(pointer, $"{typeof(T).Name}*[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            pointerBytes = context.Blocks.Load(cursor, byteCount, out tableAddress);
            RequireLoadedAt(targetAddress, tableAddress, $"{typeof(T).Name}*[]");
        }

        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var pointers = new XPointer<T>[count];

        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadPointer<T>(pointerCursor, context, XPointerResolutionMode.AliasCell);

        return pointers;
    }

    private static XStringArrayPayload ReadXStringArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative XString pointer array count {count}.");

        string view = $"XString[{count}]";
        int byteCount = checked(count * sizeof(int));
        if (pointer.Type == PointerType.Null)
            return new XStringArrayPayload([], []);
        if (pointer.Type == PointerType.Offset &&
            pointer.PackedAddress is { } existingAddress &&
            context.TryGetMaterializedView<XStringArrayPayload>(
                existingAddress,
                view,
                out XStringArrayPayload? existing) &&
            existing is not null)
        {
            return existing;
        }

        byte[] pointerBytes;
        XBlockAddress tableAddress;
        if (!TryReadDirectOffsetPayload<XString[]>(pointer, byteCount, "XString[]", context, out pointerBytes, out tableAddress))
        {
            RequireInlinePayload(pointer, "XString[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            pointerBytes = context.Blocks.Load(cursor, byteCount, out tableAddress);
            RequireLoadedAt(targetAddress, tableAddress, "XString[]");
        }

        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var pointers = new XString[count];

        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadXStringPointer(pointerCursor, context);

        IReadOnlyList<string?> values = ReadXStrings(cursor, pointers, context);
        return context.RegisterMaterializedView(
            tableAddress,
            view,
            new XStringArrayPayload(pointers, values),
            view);
    }

    private static IReadOnlyList<ScriptStringReference> ReadScriptStringArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        string memberName,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ScriptString array count {count}.");

        int byteCount = checked(count * sizeof(ushort));
        if (pointer.Type == PointerType.Null)
            return [];

        string view = $"ScriptString[{count}]";
        byte[] bytes;
        XBlockAddress arrayAddress;
        if (pointer.Type == PointerType.Offset)
        {
            if (!TryReadDirectOffsetPayload<ushort[]>(pointer, byteCount, $"{memberName} ScriptString[]", context, out bytes, out arrayAddress))
                throw new InvalidDataException($"{memberName} packed ScriptString pointer was not resolved.");

            if (context.TryGetMaterializedView<ScriptStringReference[]>(
                    arrayAddress,
                    view,
                    out ScriptStringReference[]? existing) &&
                existing is not null)
            {
                RequireArrayCount(existing, count, memberName);
                return existing;
            }
        }
        else
        {
            RequireInlinePayload(pointer, $"{memberName} ScriptString[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 2);
            bytes = context.Blocks.Load(cursor, byteCount, out arrayAddress);
            RequireLoadedAt(targetAddress, arrayAddress, $"{memberName} ScriptString[]");
        }

        var arrayCursor = new FastFileCursor(bytes, arrayAddress);
        var values = new ScriptStringReference[count];

        for (int i = 0; i < values.Length; i++)
        {
            ushort rawLocalIndex = arrayCursor.ReadUInt16();
            XBlockAddress destinationCell = arrayAddress.Add(i * sizeof(ushort));
            ScriptStringReference resolved = context.ZoneScriptStrings.Resolve(
                rawLocalIndex,
                destinationCell,
                $"{memberName}[{i}]");
            context.Blocks.WriteUInt16(destinationCell, resolved.RuntimeHandle.Value);
            values[i] = resolved;
        }

        return context.RegisterMaterializedView(
            arrayAddress,
            view,
            values,
            $"{memberName} ScriptString[]");
    }

    private static IReadOnlyList<float> ReadFloatArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative float array count {count}.");

        int byteCount = checked(count * sizeof(float));
        if (pointer.Type == PointerType.Null)
            return [];
        string view = $"Single[{count}]";
        if (pointer.Type == PointerType.Offset &&
            pointer.PackedAddress is { } existingAddress &&
            context.TryGetMaterializedView<float[]>(
                existingAddress,
                view,
                out float[]? existing) &&
            existing is not null)
        {
            return existing;
        }

        byte[] bytes;
        XBlockAddress arrayAddress;
        if (!TryReadDirectOffsetPayload<float[]>(pointer, byteCount, "float[]", context, out bytes, out arrayAddress))
        {
            RequireInlinePayload(pointer, "float[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            bytes = context.Blocks.Load(cursor, byteCount, out arrayAddress);
            RequireLoadedAt(targetAddress, arrayAddress, "float[]");
        }

        var arrayCursor = new FastFileCursor(bytes, arrayAddress);
        float[] values = ReadSingleArray(arrayCursor, count);
        return context.RegisterMaterializedView(
            arrayAddress,
            view,
            values,
            view);
    }

    private static IReadOnlyList<Vec2> ReadVec2Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative Vec2 array count {count}.");

        int byteCount = checked(count * 2 * sizeof(float));
        if (pointer.Type == PointerType.Null)
            return [];
        string view = $"Vec2[{count}]";
        if (pointer.Type == PointerType.Offset &&
            pointer.PackedAddress is { } existingAddress &&
            context.TryGetMaterializedView<Vec2[]>(
                existingAddress,
                view,
                out Vec2[]? existing) &&
            existing is not null)
        {
            return existing;
        }

        byte[] bytes;
        XBlockAddress arrayAddress;
        if (!TryReadDirectOffsetPayload<Vec2[]>(pointer, byteCount, "Vec2[]", context, out bytes, out arrayAddress))
        {
            RequireInlinePayload(pointer, "Vec2[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            bytes = context.Blocks.Load(cursor, byteCount, out arrayAddress);
            RequireLoadedAt(targetAddress, arrayAddress, "Vec2[]");
        }

        var arrayCursor = new FastFileCursor(bytes, arrayAddress);
        var values = new Vec2[count];

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new Vec2
            {
                a = ReadSingle(arrayCursor),
                b = ReadSingle(arrayCursor)
            };
        }

        return context.RegisterMaterializedView(
            arrayAddress,
            view,
            values,
            view);
    }

    private static IReadOnlyList<string?> ReadXStrings(
        FastFileCursor cursor,
        IReadOnlyList<XString> pointers,
        DbLoadExecutionContext context)
    {
        var values = new string?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
            values[i] = ReadXString(cursor, pointers[i], context);

        return values;
    }

    private static IReadOnlyList<XString> ReadSoundAliasCellPointers(
        FastFileCursor cursor,
        int count,
        DbLoadExecutionContext context)
    {
        var pointers = new XString[count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadXStringPointer(cursor, context);

        return pointers;
    }

    private static IReadOnlyList<string?> ReadSoundAliasCells(
        FastFileCursor cursor,
        IReadOnlyList<XString> pointers,
        DbLoadExecutionContext context)
    {
        var values = new string?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
            values[i] = ReadSoundAliasCell(cursor, pointers[i], context);

        return values;
    }

    private static string? ReadSoundAliasCell(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        XPointerReference cellPointer = pointer.Untyped;
        if (cellPointer.Type == PointerType.Null)
            return null;

        if (cellPointer.Type == PointerType.Offset)
        {
            if (cellPointer.ResolutionMode != XPointerResolutionMode.Direct || cellPointer.PackedAddress is not { } address)
            {
                throw new InvalidDataException(
                    $"snd_alias_list_name cell pointer 0x{unchecked((uint)cellPointer.Raw):X8} is not a packed direct pointer.");
            }

            context.Blocks.ValidateMaterializedRange(address, sizeof(int), "snd_alias_list_name cell", cellPointer.Raw);
            int nestedRaw = context.Blocks.ReadInt32(address);
            if (nestedRaw == 0)
                return null;

            var materializedNestedStringPointer = new XString(
                nestedRaw,
                XPointerResolutionMode.Direct,
                address);
            return ReadXString(cursor, materializedNestedStringPointer, context);
        }

        RequireInlinePayload(cellPointer, "snd_alias_list_name cell");

        // A non-null sound-alias custom cell points at a nested XString cell,
        // which then points at the C string.
        context.PointerReader.PatchInlinePointerCell(cellPointer, alignment: 4);
        byte[] nestedCellBytes = context.Blocks.Load(cursor, sizeof(int), out XBlockAddress nestedCellAddress);
        var nestedCellCursor = new FastFileCursor(nestedCellBytes, nestedCellAddress);
        XString nestedStringPointer = ReadXStringPointer(nestedCellCursor, context);
        return ReadXString(cursor, nestedStringPointer, context);
    }

    private static SoundAliasCellArrayPayload ReadSoundAliasCellArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative sound alias count {count}.");

        if (pointer.Type == PointerType.Null)
            return new SoundAliasCellArrayPayload([], []);

        string view = $"SoundAliasCell[{count}]";
        if (pointer.Type == PointerType.Offset &&
            pointer.PackedAddress is { } existingAddress &&
            context.TryGetMaterializedView<SoundAliasCellArrayPayload>(
                existingAddress,
                view,
                out SoundAliasCellArrayPayload? existing) &&
            existing is not null)
        {
            return existing;
        }
        int byteCount = checked(count * sizeof(int));
        byte[] cellBytes;
        XBlockAddress arrayAddress;
        if (!TryReadDirectOffsetPayload<XString[]>(
                pointer,
                byteCount,
                "snd_alias_list_name[]",
                context,
                out cellBytes,
                out arrayAddress))
        {
            RequireInlinePayload(pointer, "snd_alias_list_name[]");
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            cellBytes = context.Blocks.Load(cursor, byteCount, out arrayAddress);
            RequireLoadedAt(targetAddress, arrayAddress, "snd_alias_list_name[]");
        }

        var cellCursor = new FastFileCursor(cellBytes, arrayAddress);
        IReadOnlyList<XString> pointers = ReadSoundAliasCellPointers(cellCursor, count, context);
        var values = new string?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
        {
            values[i] = ReadSoundAliasCell(cursor, pointers[i], context);
        }

        return context.RegisterMaterializedView(
            arrayAddress,
            view,
            new SoundAliasCellArrayPayload(pointers, values),
            view);
    }

    private static IReadOnlyList<MaterialAsset?> ReadMaterialPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<MaterialAsset>> pointers,
        string ownerName,
        DbLoadExecutionContext context)
    {
        var values = new MaterialAsset?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
            values[i] = ReadMaterialPointer(cursor, pointers[i].Untyped, $"{ownerName}[{i}]", context);
        return values;
    }

    private static MaterialAsset? ReadMaterialPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        string ownerName,
        DbLoadExecutionContext context)
    {

        return MaterialLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static IReadOnlyList<XModelAsset?> ReadXModelPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<XModelAsset>> pointers,
        DbLoadExecutionContext context)
    {
        var models = new XModelAsset?[pointers.Count];
        for (int index = 0; index < pointers.Count; index++)
            models[index] = ReadXModelPointer(cursor, pointers[index].Untyped, context);
        return models;
    }

    private static XModelAsset? ReadXModelPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return XModelLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static IReadOnlyList<FxEffectDefAsset?> ReadFxPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<FxEffectDefAsset>> pointers,
        DbLoadExecutionContext context)
    {
        var values = new FxEffectDefAsset?[pointers.Count];
        for (int index = 0; index < pointers.Count; index++)
            values[index] = ReadFxPointer(cursor, pointers[index].Untyped, context);
        return values;
    }

    private static FxEffectDefAsset? ReadFxPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return FxEffectDefLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static TracerDefAsset? ReadTracerPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return TracerDefLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static IW4.Assets.Assets.Physics.PhysCollmapAsset? ReadPhysCollmapPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return PhysCollmapLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode)
    {
        // Weapon roots can point at cells materialized later in the child walk.
        // Validate these in the child consumer helpers instead of during root byte decode.
        return context.PointerReader.ReadDeferredPointer<T>(cursor, mode);
    }

    private static XString ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);
    }

    private static void ValidateOffsetPointerRange(
        XPointerReference pointer,
        int byteCount,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (pointer.Type != PointerType.Offset)
            return;

        context.PointerReader.ValidateOffsetPointerRange(pointer, byteCount, targetName);
    }

    private static void ValidateOffsetPointerRange<T>(
        XPointerReference pointer,
        int byteCount,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (pointer.Type != PointerType.Offset)
            return;

        context.PointerReader.ValidateOffsetPointerRange<T>(pointer, byteCount, targetName);
    }

    private static bool TryReadDirectOffsetPayload<TTarget>(
        XPointerReference pointer,
        int byteCount,
        string targetName,
        DbLoadExecutionContext context,
        out byte[] bytes,
        out XBlockAddress address)
    {
        if (pointer.Type != PointerType.Offset)
        {
            bytes = [];
            address = default;
            return false;
        }

        if (pointer.ResolutionMode != XPointerResolutionMode.Direct || pointer.PackedAddress is not { } packedAddress)
        {
            throw new InvalidDataException(
                $"{targetName} pointer 0x{unchecked((uint)pointer.Raw):X8} is not a packed direct pointer.");
        }

        ValidateOffsetPointerRange<TTarget>(pointer, byteCount, targetName, context);
        address = packedAddress;
        bytes = context.Blocks.ReadBytes(address, byteCount);
        return true;
    }

    private static void RequireInlinePayload(XPointerReference pointer, string targetName)
    {
        if (pointer.Type != PointerType.Inline)
        {
            throw new InvalidDataException(
                $"{targetName} pointer 0x{unchecked((uint)pointer.Raw):X8} is not null, packed direct, or a supported inline sentinel.");
        }
    }

    private static void RequireLoadedAt(
        XBlockAddress expectedAddress,
        XBlockAddress loadedAddress,
        string targetName)
    {
        if (loadedAddress != expectedAddress)
        {
            throw new InvalidDataException(
                $"{targetName} pointer patched to {expectedAddress}, but payload loaded at {loadedAddress}.");
        }
    }

    private static void RequireArrayCount<T>(T[] values, int expectedCount, string targetName)
    {
        if (values.Length != expectedCount)
        {
            throw new InvalidDataException(
                $"Packed {targetName} array has {values.Length} value(s); expected {expectedCount}.");
        }
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static IReadOnlyList<XPointer<T>> ReadAliasPointerArray<T>(
        FastFileCursor cursor,
        int count,
        DbLoadExecutionContext context)
    {
        var values = new XPointer<T>[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadPointer<T>(cursor, context, XPointerResolutionMode.AliasCell);

        return values;
    }

    private static WeaponViewMovementFields ReadViewMovementFields(FastFileCursor cursor)
    {
        return new WeaponViewMovementFields
        {
            StandMove = ReadVec3(cursor),
            StandRotation = ReadVec3(cursor),
            StrafeMove = ReadVec3(cursor),
            StrafeRotation = ReadVec3(cursor),
            DuckedOffset = ReadVec3(cursor),
            DuckedMove = ReadVec3(cursor),
            DuckedRotation = ReadVec3(cursor),
            ProneOffset = ReadVec3(cursor),
            ProneMove = ReadVec3(cursor),
            ProneRotation = ReadVec3(cursor)
        };
    }

    private static WeaponTimingFields ReadWeaponTimingFields(FastFileCursor cursor)
    {
        return new WeaponTimingFields
        {
            FireDelay = cursor.ReadInt32(),
            MeleeDelay = cursor.ReadInt32(),
            MeleeChargeDelay = cursor.ReadInt32(),
            DetonateDelay = cursor.ReadInt32(),
            RechamberTime = cursor.ReadInt32(),
            RechamberTimeOneHanded = cursor.ReadInt32(),
            RechamberBoltTime = cursor.ReadInt32(),
            HoldFireTime = cursor.ReadInt32(),
            DetonateTime = cursor.ReadInt32(),
            MeleeTime = cursor.ReadInt32(),
            MeleeChargeTime = cursor.ReadInt32(),
            ReloadTime = cursor.ReadInt32(),
            ReloadShowRocketTime = cursor.ReadInt32(),
            ReloadEmptyTime = cursor.ReadInt32(),
            ReloadAddTime = cursor.ReadInt32(),
            ReloadStartTime = cursor.ReadInt32(),
            ReloadStartAddTime = cursor.ReadInt32(),
            ReloadEndTime = cursor.ReadInt32(),
            DropTime = cursor.ReadInt32(),
            RaiseTime = cursor.ReadInt32(),
            AltDropTime = cursor.ReadInt32(),
            QuickDropTime = cursor.ReadInt32(),
            QuickRaiseTime = cursor.ReadInt32(),
            BreachRaiseTime = cursor.ReadInt32(),
            EmptyRaiseTime = cursor.ReadInt32(),
            EmptyDropTime = cursor.ReadInt32(),
            SprintInTime = cursor.ReadInt32(),
            SprintLoopTime = cursor.ReadInt32(),
            SprintOutTime = cursor.ReadInt32(),
            StunnedTimeBegin = cursor.ReadInt32(),
            StunnedTimeLoop = cursor.ReadInt32(),
            StunnedTimeEnd = cursor.ReadInt32(),
            NightVisionWearTime = cursor.ReadInt32(),
            NightVisionWearTimeFadeOutEnd = cursor.ReadInt32(),
            NightVisionWearTimePowerUp = cursor.ReadInt32(),
            NightVisionRemoveTime = cursor.ReadInt32(),
            NightVisionRemoveTimePowerDown = cursor.ReadInt32(),
            NightVisionRemoveTimeFadeInStart = cursor.ReadInt32(),
            FuseTime = cursor.ReadInt32(),
            AiFuseTime = cursor.ReadInt32()
        };
    }

    private static WeaponAimMovementTuningFields ReadAimMovementTuningFields(FastFileCursor cursor)
    {
        return new WeaponAimMovementTuningFields
        {
            AutoAimRange = ReadSingle(cursor),
            AimAssistRange = ReadSingle(cursor),
            AimAssistRangeAds = ReadSingle(cursor),
            AimPadding = ReadSingle(cursor),
            EnemyCrosshairRange = ReadSingle(cursor),
            MoveSpeedScale = ReadSingle(cursor),
            AdsMoveSpeedScale = ReadSingle(cursor),
            SprintDurationScale = ReadSingle(cursor),
            AdsZoomInFraction = ReadSingle(cursor),
            AdsZoomOutFraction = ReadSingle(cursor)
        };
    }

    private static WeaponAdsViewAndSpreadFields ReadAdsViewAndSpreadFields(FastFileCursor cursor)
    {
        return new WeaponAdsViewAndSpreadFields
        {
            AdsBobFactor = ReadSingle(cursor),
            AdsViewBobMultiplier = ReadSingle(cursor),
            HipSpreadStandMin = ReadSingle(cursor),
            HipSpreadDuckedMin = ReadSingle(cursor),
            HipSpreadProneMin = ReadSingle(cursor),
            HipSpreadStandMax = ReadSingle(cursor),
            HipSpreadDuckedMax = ReadSingle(cursor),
            HipSpreadProneMax = ReadSingle(cursor),
            HipSpreadDecayRate = ReadSingle(cursor),
            HipSpreadFireAdd = ReadSingle(cursor),
            HipSpreadTurnAdd = ReadSingle(cursor),
            HipSpreadMoveAdd = ReadSingle(cursor),
            HipSpreadDuckedDecay = ReadSingle(cursor),
            HipSpreadProneDecay = ReadSingle(cursor),
            HipReticleSidePosition = ReadSingle(cursor),
            AdsIdleAmount = ReadSingle(cursor),
            HipIdleAmount = ReadSingle(cursor),
            AdsIdleSpeed = ReadSingle(cursor),
            HipIdleSpeed = ReadSingle(cursor),
            IdleCrouchFactor = ReadSingle(cursor),
            IdleProneFactor = ReadSingle(cursor),
            GunMaxPitch = ReadSingle(cursor),
            GunMaxYaw = ReadSingle(cursor),
            SwayMaxAngle = ReadSingle(cursor),
            SwayLerpSpeed = ReadSingle(cursor),
            SwayPitchScale = ReadSingle(cursor),
            SwayYawScale = ReadSingle(cursor),
            SwayHorizontalScale = ReadSingle(cursor),
            SwayVerticalScale = ReadSingle(cursor),
            SwayShellShockScale = ReadSingle(cursor),
            AdsSwayMaxAngle = ReadSingle(cursor),
            AdsSwayLerpSpeed = ReadSingle(cursor),
            AdsSwayPitchScale = ReadSingle(cursor),
            AdsSwayYawScale = ReadSingle(cursor),
            AdsSwayHorizontalScale = ReadSingle(cursor),
            AdsSwayVerticalScale = ReadSingle(cursor),
            AdsViewErrorMin = ReadSingle(cursor),
            AdsViewErrorMax = ReadSingle(cursor)
        };
    }

    private static WeaponTurnSpeedAndRangeFields ReadWeaponTurnSpeedAndRangeFields(FastFileCursor cursor)
    {
        return new WeaponTurnSpeedAndRangeFields
        {
            MinTurnSpeed = ReadSingle(cursor),
            MaxTurnSpeed = ReadSingle(cursor),
            PitchConvergenceTime = ReadSingle(cursor),
            YawConvergenceTime = ReadSingle(cursor),
            SuppressTime = ReadSingle(cursor),
            MaxRange = ReadSingle(cursor),
            AnimationHorizontalRotateIncrement = ReadSingle(cursor),
            PlayerPositionDistance = ReadSingle(cursor),
            ScanSpeed = ReadSingle(cursor),
            ScanAcceleration = ReadSingle(cursor)
        };
    }

    private static WeaponPhysicsFields ReadWeaponPhysicsFields(FastFileCursor cursor)
    {
        return new WeaponPhysicsFields
        {
            DualWieldViewModelOffset = ReadSingle(cursor),
            KillIconRatio = cursor.ReadInt32(),
            ReloadAmmoAdd = cursor.ReadInt32(),
            ReloadStartAdd = cursor.ReadInt32(),
            AmmoDropStockMin = cursor.ReadInt32(),
            AmmoDropClipPercentMin = ReadSingle(cursor),
            AmmoDropClipPercentMax = ReadSingle(cursor),
            ExplosionRadius = cursor.ReadInt32(),
            ExplosionRadiusMin = cursor.ReadInt32(),
            ExplosionInnerDamage = cursor.ReadInt32(),
            ExplosionOuterDamage = cursor.ReadInt32(),
            DamageConeAngle = ReadSingle(cursor),
            BulletExplosionDamageMultiplier = ReadSingle(cursor),
            BulletExplosionRadiusMultiplier = ReadSingle(cursor),
            ProjectileSpeed = cursor.ReadInt32(),
            ProjectileSpeedUp = cursor.ReadInt32(),
            ProjectileSpeedForward = cursor.ReadInt32(),
            ProjectileActivateDistance = cursor.ReadInt32(),
            ProjectileLifetime = cursor.ReadInt32(),
            TimeToAccelerate = cursor.ReadInt32(),
            ProjectileCurvature = ReadSingle(cursor)
        };
    }

    private static Vec3 Vec3FromRawInts(IReadOnlyList<int> values)
    {
        if (values.Count != 3)
            throw new InvalidDataException($"Expected 3 raw float dwords, got {values.Count}.");

        return new Vec3
        {
            X = SingleFromRawInt(values[0]),
            Y = SingleFromRawInt(values[1]),
            Z = SingleFromRawInt(values[2])
        };
    }

    private static WeaponGunKickAndDistanceFields GunKickAndDistanceFromRawInts(IReadOnlyList<int> values)
    {
        if (values.Count != 35)
            throw new InvalidDataException($"Expected 35 gun-kick/distance dwords, got {values.Count}.");

        return new WeaponGunKickAndDistanceFields
        {
            AdsGunKickReducedKickBullets = values[0],
            AdsGunKickReducedKickPercent = SingleFromRawInt(values[1]),
            AdsGunKickPitchMin = SingleFromRawInt(values[2]),
            AdsGunKickPitchMax = SingleFromRawInt(values[3]),
            AdsGunKickYawMin = SingleFromRawInt(values[4]),
            AdsGunKickYawMax = SingleFromRawInt(values[5]),
            AdsGunKickAcceleration = SingleFromRawInt(values[6]),
            AdsGunKickSpeedMax = SingleFromRawInt(values[7]),
            AdsGunKickSpeedDecay = SingleFromRawInt(values[8]),
            AdsGunKickStaticDecay = SingleFromRawInt(values[9]),
            AdsViewKickPitchMin = SingleFromRawInt(values[10]),
            AdsViewKickPitchMax = SingleFromRawInt(values[11]),
            AdsViewKickYawMin = SingleFromRawInt(values[12]),
            AdsViewKickYawMax = SingleFromRawInt(values[13]),
            AdsViewScatterMin = SingleFromRawInt(values[14]),
            AdsViewScatterMax = SingleFromRawInt(values[15]),
            AdsSpread = SingleFromRawInt(values[16]),
            HipGunKickReducedKickBullets = values[17],
            HipGunKickReducedKickPercent = SingleFromRawInt(values[18]),
            HipGunKickPitchMin = SingleFromRawInt(values[19]),
            HipGunKickPitchMax = SingleFromRawInt(values[20]),
            HipGunKickYawMin = SingleFromRawInt(values[21]),
            HipGunKickYawMax = SingleFromRawInt(values[22]),
            HipGunKickAcceleration = SingleFromRawInt(values[23]),
            HipGunKickSpeedMax = SingleFromRawInt(values[24]),
            HipGunKickSpeedDecay = SingleFromRawInt(values[25]),
            HipGunKickStaticDecay = SingleFromRawInt(values[26]),
            HipViewKickPitchMin = SingleFromRawInt(values[27]),
            HipViewKickPitchMax = SingleFromRawInt(values[28]),
            HipViewKickYawMin = SingleFromRawInt(values[29]),
            HipViewKickYawMax = SingleFromRawInt(values[30]),
            HipViewScatterMin = SingleFromRawInt(values[31]),
            HipViewScatterMax = SingleFromRawInt(values[32]),
            FightDistance = SingleFromRawInt(values[33]),
            MaxDistance = SingleFromRawInt(values[34])
        };
    }

    private static float SingleFromRawInt(int value)
    {
        return BitConverter.Int32BitsToSingle(value);
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static Vec3 ReadVec3(FastFileCursor cursor)
    {
        return new Vec3
        {
            X = ReadSingle(cursor),
            Y = ReadSingle(cursor),
            Z = ReadSingle(cursor)
        };
    }

    private static float[] ReadSingleArray(FastFileCursor cursor, int count)
    {
        var values = new float[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadSingle(cursor);

        return values;
    }

    private static int[] ReadInt32Array(FastFileCursor cursor, int count)
    {
        var values = new int[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadInt32();

        return values;
    }

    private static WeaponTailFlags ReadTailFlags(FastFileCursor cursor)
    {
        return new WeaponTailFlags
        {
            SharedAmmo = cursor.ReadByte(),
            LockonSupported = cursor.ReadByte(),
            RequireLockonToFire = cursor.ReadByte(),
            BigExplosion = cursor.ReadByte(),
            NoAdsWhenMagEmpty = cursor.ReadByte(),
            AvoidDropCleanup = cursor.ReadByte(),
            InheritsPerks = cursor.ReadByte(),
            CrosshairColorChange = cursor.ReadByte(),
            RifleBullet = cursor.ReadByte(),
            ArmorPiercing = cursor.ReadByte(),
            BoltAction = cursor.ReadByte(),
            AimDownSight = cursor.ReadByte(),
            RechamberWhileAds = cursor.ReadByte(),
            BulletExplosiveDamage = cursor.ReadByte(),
            CookOffHold = cursor.ReadByte(),
            ClipOnly = cursor.ReadByte(),
            NoAmmoPickup = cursor.ReadByte(),
            AdsFireOnly = cursor.ReadByte(),
            CancelAutoHolsterWhenEmpty = cursor.ReadByte(),
            DisableSwitchToWhenEmpty = cursor.ReadByte(),
            SuppressAmmoReserveDisplay = cursor.ReadByte(),
            LaserSightDuringNightvision = cursor.ReadByte(),
            MarkableViewmodel = cursor.ReadByte(),
            NoDualWield = cursor.ReadByte(),
            FlipKillIcon = cursor.ReadByte(),
            NoPartialReload = cursor.ReadByte(),
            SegmentedReload = cursor.ReadByte(),
            BlocksProne = cursor.ReadByte(),
            Silenced = cursor.ReadByte(),
            IsRollingGrenade = cursor.ReadByte(),
            ProjectileExplosionEffectForceNormalUp = cursor.ReadByte(),
            ProjectileImpactExplode = cursor.ReadByte(),
            StickToPlayers = cursor.ReadByte(),
            HasDetonator = cursor.ReadByte(),
            DisableFiring = cursor.ReadByte(),
            TimedDetonation = cursor.ReadByte(),
            Rotate = cursor.ReadByte(),
            HoldButtonToThrow = cursor.ReadByte(),
            FreezeMovementWhenFiring = cursor.ReadByte(),
            ThermalScope = cursor.ReadByte(),
            AltModeSameWeapon = cursor.ReadByte(),
            TurretBarrelSpinEnabled = cursor.ReadByte(),
            MissileConeSoundEnabled = cursor.ReadByte(),
            MissileConeSoundPitchShiftEnabled = cursor.ReadByte(),
            MissileConeSoundCrossfadeEnabled = cursor.ReadByte(),
            OffhandHoldIsCancelable = cursor.ReadByte(),
            ReservedPadding = cursor.ReadUInt16()
        };
    }

    private sealed record XStringArrayPayload(
        IReadOnlyList<XString> Pointers,
        IReadOnlyList<string?> Values);

    private sealed record XModelPointerArrayPayload(
        IReadOnlyList<XPointer<XModelAsset>> Pointers,
        IReadOnlyList<XModelAsset?> Models);

    private static void Seek(FastFileCursor cursor, int offset)
    {
        if (offset < cursor.Offset)
            throw new InvalidOperationException($"Cannot seek backwards from 0x{cursor.Offset:X} to 0x{offset:X}.");

        cursor.Skip(offset - cursor.Offset);
    }

}
