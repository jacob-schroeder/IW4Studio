using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Capture-time, detached Weapon projection. Runtime pointers and
/// pool objects are replaced with symbolic links before a draft is exposed.</summary>
public sealed class WeaponAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal WeaponAuthoredSnapshot(WeaponBuildData data) => Data = data.Copy();
    private WeaponAuthoredSnapshot(WeaponBuildData data, bool takeOwnership)
    {
        if (!takeOwnership)
            throw new ArgumentException(
                "Detached Weapon snapshot ownership must be explicit.",
                nameof(takeOwnership));
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
    internal WeaponBuildData Data { get; }
    public XAssetType AssetType => XAssetType.Weapon;
    internal static WeaponAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is WeaponAuthoredSnapshot snapshot
            ? snapshot : throw new InvalidDataException("Weapon editing requires a capture-time detached semantic snapshot.");
    internal static WeaponAuthoredSnapshot FromLoaded(WeaponAsset asset) =>
        FromLoaded(asset, new WeaponGraphClone());
    internal static WeaponAuthoredSnapshot FromLoaded(
        WeaponAsset asset,
        WeaponGraphClone graph) =>
        new(WeaponBuildData.FromLoaded(asset, graph), takeOwnership: true);
}

public sealed class WeaponBuildData : IWeaponBuildData
{
    internal static readonly JsonSerializerOptions CopyOptions = new() { IncludeFields = true };
    public XAssetType AssetType => XAssetType.Weapon;
    public WeaponVariantDef Variant { get; init; } = new();
    public WeaponDef Definition { get; init; } = new();
    public WeaponReferenceBuildData References { get; init; } = new();
    [JsonIgnore]
    public WeaponLinkerProvenance LinkerProvenance { get; init; } = WeaponLinkerProvenance.Empty;

    internal WeaponBuildData Copy() => Copy(new WeaponGraphClone());
    internal WeaponBuildData Copy(WeaponGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new()
        {
            Variant = VariantProjection(Variant, graph),
            Definition = DefinitionProjection(Definition, graph),
            References = CopyReferences(References, graph),
            LinkerProvenance = LinkerProvenance
        };
    }

    internal static WeaponBuildData FromLoaded(WeaponAsset asset) =>
        FromLoaded(asset, new WeaponGraphClone());

    internal static WeaponBuildData FromLoaded(
        WeaponAsset asset,
        WeaponGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(graph);
        WeaponVariantDef variant = asset.Variant ?? throw new InvalidDataException("Weapon has no variant root.");
        WeaponDef definition = variant.Definition ?? throw new InvalidDataException("Weapon has no definition root.");
        return new()
        {
            Variant = VariantProjection(variant, graph),
            Definition = DefinitionProjection(definition, graph),
            References = LoadedReferences(variant, definition, graph),
            LinkerProvenance = LoadedLinkerProvenance(variant, definition, graph)
        };
    }

    private static WeaponLinkerProvenance LoadedLinkerProvenance(
        WeaponVariantDef variant,
        WeaponDef definition,
        WeaponGraphClone graph)
    {
        WeaponReusableStorageToken? hideTags = null;
        if (variant.HideTagsPointer.Type != PointerType.Null && variant.HideTags.Count != 0)
            hideTags = graph.ReusableStorageToken(variant.HideTags[0].DestinationCellAddress);

        XBlockAddress? worldGunModelsAddress =
            definition.WorldGunModelsPointer.PackedAddress;
        if (worldGunModelsAddress is null &&
            definition.WorldGunModelsPointer.Type is PointerType.Inline or PointerType.Insert &&
            definition.WorldGunModelPointers.Count != 0)
        {
            worldGunModelsAddress = definition.WorldGunModelPointers[0].CellAddress;
        }

        return new WeaponLinkerProvenance(
            HideTagsStorage: hideTags,
            WorldGunModelsStorage: worldGunModelsAddress is { } address
                ? graph.ReusableStorageToken(address)
                : null,
            KillIconForm: SourceForm(variant.KillIconPointer.Type),
            DpadIconForm: SourceForm(variant.DpadIconPointer.Type));
    }

    private static WeaponNestedPointerSourceForm SourceForm(PointerType type) =>
        type == PointerType.Insert
            ? WeaponNestedPointerSourceForm.Insert
            : WeaponNestedPointerSourceForm.Inline;

    private static WeaponVariantDef VariantProjection(
        WeaponVariantDef value,
        WeaponGraphClone graph)
    {
        if (graph.TryGet(value, out WeaponVariantDef? existing))
            return existing!;
        var clone = new WeaponVariantDef
        {
        InternalName = value.InternalName, DisplayName = value.DisplayName,
        HideTags = graph.Region(value.HideTags, $"ScriptString[{WeaponVariantDef.HideTagCount}]", Script),
        AnimationNames = graph.Region(value.AnimationNames, $"XString[{WeaponVariantDef.WeaponAnimCount}]", static item => item),
        AdsZoomFov = value.AdsZoomFov, AdsTransitionInTime = value.AdsTransitionInTime, AdsTransitionOutTime = value.AdsTransitionOutTime, ClipSize = value.ClipSize, ImpactType = value.ImpactType, FireTime = value.FireTime, DpadIconRatio = value.DpadIconRatio, PenetrateMultiplier = value.PenetrateMultiplier, AdsViewKickCenterSpeed = value.AdsViewKickCenterSpeed, HipViewKickCenterSpeed = value.HipViewKickCenterSpeed,
        AlternateWeaponName = value.AlternateWeaponName, AlternateWeaponIndex = value.AlternateWeaponIndex, AlternateRaiseTime = value.AlternateRaiseTime, DropAmmoMin = value.DropAmmoMin, FirstRaiseTime = value.FirstRaiseTime, DropAmmoMax = value.DropAmmoMax, AdsDofStart = value.AdsDofStart, AdsDofEnd = value.AdsDofEnd,
        AccuracyGraphKnotCount = value.AccuracyGraphKnotCount, OriginalAccuracyGraphKnotCount = value.OriginalAccuracyGraphKnotCount,
        AccuracyGraphKnots = graph.Region(value.AccuracyGraphKnots, $"Vec2[{value.AccuracyGraphKnotCount}]", Copy),
        OriginalAccuracyGraphKnots = graph.Region(value.OriginalAccuracyGraphKnots, $"Vec2[{value.OriginalAccuracyGraphKnotCount}]", Copy),
        MotionTracker = value.MotionTracker, Enhanced = value.Enhanced, DpadIconShowsAmmo = value.DpadIconShowsAmmo, Padding73 = value.Padding73
        };
        graph.Add(value, clone);
        return clone;
    }

    private static WeaponDef DefinitionProjection(
        WeaponDef value,
        WeaponGraphClone graph)
    {
        if (graph.TryGet(value, out WeaponDef? existing))
            return existing!;
        var clone = new WeaponDef
        {
        InternalName = value.InternalName, GunModels = Nulls<IW4.Assets.Assets.XModel.XModelAsset>(WeaponDef.GunModelCount),
        RightHandAnimationNames = graph.Region(value.RightHandAnimationNames, $"XString[{WeaponDef.WeaponAnimCount}]", static item => item),
        LeftHandAnimationNames = graph.Region(value.LeftHandAnimationNames, $"XString[{WeaponDef.WeaponAnimCount}]", static item => item),
        ModeName = value.ModeName,
        NoteTrackMaps = new WeaponNoteTrackMaps {
            SoundMapKeys = graph.Region(value.NoteTrackMaps.SoundMapKeys, $"ScriptString[{WeaponDef.NoteTrackMapCount}]", Script),
            SoundMapValues = graph.Region(value.NoteTrackMaps.SoundMapValues, $"ScriptString[{WeaponDef.NoteTrackMapCount}]", Script),
            RumbleMapKeys = graph.Region(value.NoteTrackMaps.RumbleMapKeys, $"ScriptString[{WeaponDef.NoteTrackMapCount}]", Script),
            RumbleMapValues = graph.Region(value.NoteTrackMaps.RumbleMapValues, $"ScriptString[{WeaponDef.NoteTrackMapCount}]", Script) },
        PlayerAnimType = value.PlayerAnimType, WeaponType = value.WeaponType, WeaponClass = value.WeaponClass, PenetrateType = value.PenetrateType, InventoryType = value.InventoryType, FireType = value.FireType, OffhandClass = value.OffhandClass, Stance = value.Stance,
        SoundAliasNames = graph.Region(value.SoundAliasNames, $"SoundAliasCell[{WeaponDef.WeaponSoundAliasCount}]", static item => item),
        BounceSoundNames = graph.Region(value.BounceSoundNames, $"SoundAliasCellTable[{value.BounceSoundNames.Count}]", static item => item),
        Reticle = Copy(value.Reticle), ViewMovement = Copy(value.ViewMovement), PositionalMovement = Copy(value.PositionalMovement), WorldGunModels = Nulls<IW4.Assets.Assets.XModel.XModelAsset>(WeaponDef.GunModelCount), WorldModels = Nulls<IW4.Assets.Assets.XModel.XModelAsset>(4), Icons = IconProjection(value.Icons),
        Ammo = AmmoProjection(value.Ammo), Timing = Copy(value.Timing), AimMovementTuning = Copy(value.AimMovementTuning), Overlay = OverlayProjection(value.Overlay), AdsViewAndSpread = Copy(value.AdsViewAndSpread), Physics = Copy(value.Physics),
        Projectile = ProjectileProjection(value.Projectile, graph), Accuracy = AccuracyProjection(value.Accuracy, graph), TurnSpeedAndRange = Copy(value.TurnSpeedAndRange), Hints = HintProjection(value.Hints), ScriptName = value.ScriptName, OOPosAnimLength = value.OOPosAnimLength, MinDamage = value.MinDamage, MinPlayerDamage = value.MinPlayerDamage, MaxDamageRange = value.MaxDamageRange, MinDamageRange = value.MinDamageRange, DestabilizationRateTime = value.DestabilizationRateTime, DestabilizationCurvatureMax = value.DestabilizationCurvatureMax, DestabilizeDistance = value.DestabilizeDistance, DestabilizeDistanceToTimeScale = value.DestabilizeDistanceToTimeScale,
        LocationDamageMultipliers = graph.Region(value.LocationDamageMultipliers, $"Single[{WeaponDef.HitLocationCount}]", static item => item),
        Rumble = RumbleProjection(value.Rumble), TurretScopeZoomRate = value.TurretScopeZoomRate, TurretScopeZoomMin = value.TurretScopeZoomMin, TurretScopeZoomMax = value.TurretScopeZoomMax, TurretOverheatUpRate = value.TurretOverheatUpRate, TurretOverheatDownRate = value.TurretOverheatDownRate, TurretOverheatPenalty = value.TurretOverheatPenalty, Turret = TurretProjection(value.Turret, graph), MissileConeSound = Copy(value.MissileConeSound), TailFlags = Copy(value.TailFlags)
        };
        graph.Add(value, clone);
        return clone;
    }

    private static WeaponProjectileFields ProjectileProjection(
        WeaponProjectileFields value,
        WeaponGraphClone graph) => new()
    {
        Explosion = value.Explosion, ExplosionSound = value.ExplosionSound, DudSound = value.DudSound, Stickiness = value.Stickiness, LowAmmoWarningThreshold = value.LowAmmoWarningThreshold, RicochetChance = value.RicochetChance,
        ParallelBounce = graph.Region(value.ParallelBounce, $"Single[{WeaponDef.SurfaceCount}]", static item => item),
        PerpendicularBounce = graph.Region(value.PerpendicularBounce, $"Single[{WeaponDef.SurfaceCount}]", static item => item),
        ProjectileColor = Copy(value.ProjectileColor), GuidedMissileType = value.GuidedMissileType, MaxSteeringAcceleration = value.MaxSteeringAcceleration, IgnitionDelay = value.IgnitionDelay, IgnitionSound = value.IgnitionSound, AdsAimPitch = value.AdsAimPitch, AdsCrosshairInFraction = value.AdsCrosshairInFraction, AdsCrosshairOutFraction = value.AdsCrosshairOutFraction, GunKickAndDistance = Copy(value.GunKickAndDistance)
    };
    private static WeaponIconPointers IconProjection(WeaponIconPointers value) => new() { HudIconRatio = value.HudIconRatio, PickupIconRatio = value.PickupIconRatio, AmmoCounterIconRatio = value.AmmoCounterIconRatio, AmmoCounterClip = value.AmmoCounterClip, StartAmmo = value.StartAmmo };
    private static WeaponAmmoFields AmmoProjection(WeaponAmmoFields value) => new() { AmmoName = value.AmmoName, AmmoIndex = value.AmmoIndex, ClipName = value.ClipName, ClipIndex = value.ClipIndex, MaxAmmo = value.MaxAmmo, ShotCount = value.ShotCount, SharedAmmoCapName = value.SharedAmmoCapName, SharedAmmoCapIndex = value.SharedAmmoCapIndex, SharedAmmoCap = value.SharedAmmoCap, Damage = value.Damage, PlayerDamage = value.PlayerDamage, MeleeDamage = value.MeleeDamage, DamageType = value.DamageType };
    private static WeaponOverlayFields OverlayProjection(WeaponOverlayFields value) => new() { OverlayMaterials = [], Reticle = value.Reticle, Interface = value.Interface, Width = value.Width, Height = value.Height, WidthSplitscreen = value.WidthSplitscreen, HeightSplitscreen = value.HeightSplitscreen };
    private static WeaponHintFields HintProjection(WeaponHintFields value) => new() { UseHintString = value.UseHintString, DropHintString = value.DropHintString, UseHintStringIndex = value.UseHintStringIndex, DropHintStringIndex = value.DropHintStringIndex, HorizontalViewJitter = value.HorizontalViewJitter, VerticalViewJitter = value.VerticalViewJitter, ScanSpeed = value.ScanSpeed, ScanAcceleration = value.ScanAcceleration, ScanPauseTime = value.ScanPauseTime };
    private static WeaponRumbleFields RumbleProjection(WeaponRumbleFields value) => new() { FireRumble = value.FireRumble, MeleeImpactRumble = value.MeleeImpactRumble };
    private static WeaponAccuracyFields AccuracyProjection(WeaponAccuracyFields value, WeaponGraphClone graph) => new() { GraphName0 = value.GraphName0, GraphName1 = value.GraphName1, GraphKnots = graph.Region(value.GraphKnots, $"Vec2[{value.GraphKnots.Count}]", Copy), OriginalGraphKnots = graph.Region(value.OriginalGraphKnots, $"Vec2[{value.OriginalGraphKnots.Count}]", Copy), LocalGraphKnotCount = value.LocalGraphKnotCount, LocalOriginalGraphKnotCount = value.LocalOriginalGraphKnotCount, AnimationNotifyComparison = value.AnimationNotifyComparison, LeftArc = value.LeftArc, RightArc = value.RightArc, TopArc = value.TopArc, BottomArc = value.BottomArc, Accuracy = value.Accuracy, AiSpread = value.AiSpread, PlayerSpread = value.PlayerSpread };
    private static WeaponTurretFields TurretProjection(WeaponTurretFields value, WeaponGraphClone graph) => new() { OverheatSound = value.OverheatSound, BarrelSpinRumble = value.BarrelSpinRumble, BarrelSpinSpeed = value.BarrelSpinSpeed, BarrelSpinUpTime = value.BarrelSpinUpTime, BarrelSpinDownTime = value.BarrelSpinDownTime, BarrelSpinMaxSound = value.BarrelSpinMaxSound, BarrelSpinUpSoundNames = graph.Region(value.BarrelSpinUpSoundNames, $"SoundAliasCell[{WeaponDef.TurretBarrelSpinSoundCount}]", static item => item), BarrelSpinDownSoundNames = graph.Region(value.BarrelSpinDownSoundNames, $"SoundAliasCell[{WeaponDef.TurretBarrelSpinSoundCount}]", static item => item) };
    private static WeaponReferenceBuildData CopyReferences(WeaponReferenceBuildData value, WeaponGraphClone graph)
    {
        if (graph.TryGet(value, out WeaponReferenceBuildData? existing))
            return existing!;
        var clone = new WeaponReferenceBuildData { KillIcon = value.KillIcon, DpadIcon = value.DpadIcon, GunModels = graph.Region(value.GunModels, $"XModelPtr[{WeaponDef.GunModelCount}]", static item => item), HandModel = value.HandModel, FlashEffects = graph.Region(value.FlashEffects, "FxPtr[2]", static item => item), Materials = graph.Region(value.Materials, "MaterialPtr[2]", static item => item), Effects = graph.Region(value.Effects, "FxPtr[4]", static item => item), WorldGunModels = graph.Region(value.WorldGunModels, $"XModelPtr[{WeaponDef.GunModelCount}]", static item => item), WorldModels = graph.Region(value.WorldModels, "XModelPtr[4]", static item => item), IconMaterials = graph.Region(value.IconMaterials, "MaterialPtr[3]", static item => item), OverlayMaterials = graph.Region(value.OverlayMaterials, "MaterialPtr[4]", static item => item), PhysCollmap = value.PhysCollmap, ProjectileModel = value.ProjectileModel, ProjectileEffects = graph.Region(value.ProjectileEffects, "FxPtr[2]", static item => item), ImpactEffects = graph.Region(value.ImpactEffects, "FxPtr[2]", static item => item), IgnitionEffect = value.IgnitionEffect, Tracer = value.Tracer, TurretOverheatEffect = value.TurretOverheatEffect };
        graph.Add(value, clone);
        return clone;
    }
    private static WeaponReferenceBuildData LoadedReferences(
        WeaponVariantDef variant,
        WeaponDef definition,
        WeaponGraphClone graph) => new()
    {
        KillIcon = Link(XAssetType.Material, variant.KillIcon?.Info.Name),
        DpadIcon = Link(XAssetType.Material, variant.DpadIcon?.Info.Name),
        GunModels = graph.Region(
            definition.GunModels,
            $"XModelPtr[{WeaponDef.GunModelCount}]",
            value => Link(XAssetType.XModel, value?.Name),
            WeaponDef.GunModelCount),
        HandModel = Link(XAssetType.XModel, definition.HandModel?.Name),
        FlashEffects = graph.Region(
            definition.FlashEffects,
            "FxPtr[2]",
            value => Link(XAssetType.Fx, value?.Name),
            2),
        Effects = graph.Region(
            definition.Effects,
            "FxPtr[4]",
            value => Link(XAssetType.Fx, value?.Name),
            4),
        Materials = graph.Region(
            definition.Materials,
            "MaterialPtr[2]",
            value => Link(XAssetType.Material, value?.Info.Name),
            2),
        WorldGunModels = graph.Region(
            definition.WorldGunModels,
            $"XModelPtr[{WeaponDef.GunModelCount}]",
            value => Link(XAssetType.XModel, value?.Name),
            WeaponDef.GunModelCount),
        WorldModels = graph.Region(
            definition.WorldModels,
            "XModelPtr[4]",
            value => Link(XAssetType.XModel, value?.Name),
            4),
        IconMaterials = graph.Region(
            definition.IconMaterials,
            "MaterialPtr[3]",
            value => Link(XAssetType.Material, value?.Info.Name),
            3),
        OverlayMaterials = graph.Region(
            definition.OverlayMaterials,
            "MaterialPtr[4]",
            value => Link(XAssetType.Material, value?.Info.Name),
            4),
        PhysCollmap = Link(XAssetType.PhysCollmap, definition.PhysCollmapName),
        ProjectileModel = Link(XAssetType.XModel, definition.Projectile.Model?.Name),
        ProjectileEffects = graph.Region(
            definition.ProjectileEffects,
            "FxPtr[2]",
            value => Link(XAssetType.Fx, value?.Name),
            2),
        ImpactEffects = graph.Region(
            definition.ImpactEffects,
            "FxPtr[2]",
            value => Link(XAssetType.Fx, value?.Name),
            2),
        IgnitionEffect = Link(XAssetType.Fx, definition.ViewShellEjectEffect?.Name),
        Tracer = Link(XAssetType.Tracer, definition.Tracer?.Name),
        TurretOverheatEffect = Link(XAssetType.Fx, definition.TurretOverheatEffect?.Name)
    };
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CopyOptions), CopyOptions) ?? throw new InvalidDataException($"Unable to detach {typeof(T).Name}.");
    private static T?[] Fixed<T>(IReadOnlyList<T?> values, int count) =>
        Enumerable.Range(0, count).Select(index => index < values.Count ? values[index] : default).ToArray();
    private static T?[] Nulls<T>(int count) => Enumerable.Repeat<T?>(default, count).ToArray();
    private static SymbolicXAssetReference?[] Links<T>(IReadOnlyList<T?> values, int count, XAssetType type, Func<T?, string?> name) =>
        Enumerable.Range(0, count).Select(index => Link(type, name(index < values.Count ? values[index] : default))).ToArray();
    private static IW4.FastFiles.Strings.ScriptStringReference Script(IW4.FastFiles.Strings.ScriptStringReference value) => new(value.RawLocalIndex, value.Text, default, default);
    private static SymbolicXAssetReference? Link(XAssetType type, string? name) => name is null ? null : new(type, name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");
}

/// <summary>
/// Copies the detached Weapon graph while retaining only managed reference
/// identity. Equal-valued but distinct roots and regions stay distinct.
/// Serialized views are part of a region key so one backing object cannot be
/// reused through an incompatible element layout or length.
/// </summary>
internal sealed class WeaponGraphClone
{
    private readonly Dictionary<object, object> _objects =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, object>> _regions =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<XBlockAddress, WeaponReusableStorageToken> _reusableStorage = [];
    private int _nextReusableStorageToken = 1;

    internal WeaponReusableStorageToken ReusableStorageToken(XBlockAddress address)
    {
        if (_reusableStorage.TryGetValue(address, out WeaponReusableStorageToken existing))
            return existing;
        var created = new WeaponReusableStorageToken(_nextReusableStorageToken++);
        _reusableStorage.Add(address, created);
        return created;
    }

    internal bool TryGet<T>(object value, out T? clone)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_objects.TryGetValue(value, out object? stored) && stored is T typed)
        {
            clone = typed;
            return true;
        }
        clone = null;
        return false;
    }

    internal void Add<T>(object value, T clone)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(clone);
        if (_objects.TryGetValue(value, out object? existing))
        {
            if (!ReferenceEquals(existing, clone))
                throw new InvalidDataException(
                    "One Weapon semantic object identity was projected more than once.");
            return;
        }
        _objects.Add(value, clone);
    }

    internal IReadOnlyList<TResult> Region<TSource, TResult>(
        IReadOnlyList<TSource> values,
        string serializedView,
        Func<TSource, TResult> project,
        int? fixedCount = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrEmpty(serializedView);
        ArgumentNullException.ThrowIfNull(project);
        if (!_regions.TryGetValue(values, out Dictionary<string, object>? views))
        {
            views = new Dictionary<string, object>(StringComparer.Ordinal);
            _regions.Add(values, views);
        }
        if (views.TryGetValue(serializedView, out object? existing))
            return (IReadOnlyList<TResult>)existing;

        int count = fixedCount ?? values.Count;
        TResult[] clone = Enumerable.Range(0, count)
            .Select(index => index < values.Count ? project(values[index]) : default!)
            .ToArray();
        views.Add(serializedView, clone);
        return clone;
    }
}

public sealed class WeaponDraft
{
    private WeaponBuildData _data;
    internal WeaponDraft(WeaponBuildData data) => _data = data.Copy();
    public WeaponBuildData Data => _data.Copy();
    public void Replace(WeaponBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = value.Copy(); }
    internal WeaponDraft Clone() => new(_data);
}

public sealed class WeaponAuthoringAdapter : AssetAuthoringAdapter<WeaponAuthoredSnapshot, WeaponDraft, WeaponBuildData>
{
    private static readonly WeaponBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Weapon;
    public override WeaponAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => WeaponAuthoredSnapshot.Import(source);
    public override WeaponDraft CreateDraft(WeaponAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override WeaponDraft CloneDraft(WeaponDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(WeaponDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(WeaponDraft left, WeaponDraft right) => JsonSerializer.Serialize(left.Data, WeaponBuildData.CopyOptions) == JsonSerializer.Serialize(right.Data, WeaponBuildData.CopyOptions);
    public override WeaponBuildData ExportBuildData(WeaponDraft draft) { WeaponBuildData value = draft.Data; if (Validator.Validate(value).Count != 0) throw new InvalidOperationException("Weapon draft has validation errors and cannot produce build data."); return value; }
}
