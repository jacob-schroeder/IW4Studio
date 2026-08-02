using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Serializer for the complete WeaponVariantDef/WeaponDef pair.
/// The emitter follows the loader's child-consumption sequence exactly; in
/// particular, sound aliases remain nested cells and every cross-asset
/// pointer is a symbolic, comma-prefixed external identity.</summary>
public sealed class WeaponBodyEmitter : IXAssetBodyEmitter
{
    private const int VariantSize = WeaponVariantDef.SerializedSize;
    private const int DefinitionSize = WeaponDef.SerializedSize;
    private const int XModelSize = 0x120;
    private const int FxSize = 0x20;
    private const int MaterialSize = 0xa8;
    private const int PhysCollmapSize = 0x48;
    private const int TracerSize = 0x70;

    public XAssetType AssetType => XAssetType.Weapon;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IWeaponBuildData data)
        {
            errors.Add(Error("body", "Weapon build data does not implement IWeaponBuildData.", rowIndex));
            return errors;
        }

        WeaponVariantDef variant = data.Variant ?? throw new InvalidDataException("Weapon variant cannot be null.");
        WeaponDef definition = data.Definition ?? throw new InvalidDataException("Weapon definition cannot be null.");
        WeaponReferenceBuildData links = data.References ?? throw new InvalidDataException("Weapon references cannot be null.");
        String(variant.InternalName, "variant.internalName", errors, rowIndex); String(variant.DisplayName, "variant.displayName", errors, rowIndex); String(variant.AlternateWeaponName, "variant.alternateWeaponName", errors, rowIndex);
        Check(variant.HideTags.Count, WeaponVariantDef.HideTagCount, "variant.hideTags", errors, rowIndex); Check(variant.AnimationNames.Count, WeaponVariantDef.WeaponAnimCount, "variant.animationNames", errors, rowIndex);
        Check(variant.AccuracyGraphKnots.Count, variant.AccuracyGraphKnotCount, "variant.accuracyGraphKnots", errors, rowIndex); Check(variant.OriginalAccuracyGraphKnots.Count, variant.OriginalAccuracyGraphKnotCount, "variant.originalAccuracyGraphKnots", errors, rowIndex);
        CheckLinks(links.GunModels, WeaponDef.GunModelCount, XAssetType.XModel, "references.gunModels", errors, rowIndex); Link(links.HandModel, XAssetType.XModel, "references.handModel", errors, rowIndex);
        CheckLinks(links.FlashEffects, 2, XAssetType.Fx, "references.flashEffects", errors, rowIndex); CheckLinks(links.Effects, 4, XAssetType.Fx, "references.effects", errors, rowIndex); CheckLinks(links.Materials, 2, XAssetType.Material, "references.materials", errors, rowIndex);
        CheckLinks(links.WorldGunModels, WeaponDef.GunModelCount, XAssetType.XModel, "references.worldGunModels", errors, rowIndex); CheckLinks(links.WorldModels, 4, XAssetType.XModel, "references.worldModels", errors, rowIndex);
        CheckLinks(links.IconMaterials, 3, XAssetType.Material, "references.iconMaterials", errors, rowIndex); CheckLinks(links.OverlayMaterials, 4, XAssetType.Material, "references.overlayMaterials", errors, rowIndex);
        Link(links.KillIcon, XAssetType.Material, "references.killIcon", errors, rowIndex); Link(links.DpadIcon, XAssetType.Material, "references.dpadIcon", errors, rowIndex); Link(links.PhysCollmap, XAssetType.PhysCollmap, "references.physCollmap", errors, rowIndex); Link(links.ProjectileModel, XAssetType.XModel, "references.projectileModel", errors, rowIndex); CheckLinks(links.ProjectileEffects, 2, XAssetType.Fx, "references.projectileEffects", errors, rowIndex); CheckLinks(links.ImpactEffects, 2, XAssetType.Fx, "references.impactEffects", errors, rowIndex); Link(links.IgnitionEffect, XAssetType.Fx, "references.ignitionEffect", errors, rowIndex); Link(links.Tracer, XAssetType.Tracer, "references.tracer", errors, rowIndex); Link(links.TurretOverheatEffect, XAssetType.Fx, "references.turretOverheatEffect", errors, rowIndex);
        Check(definition.GunModels.Count, WeaponDef.GunModelCount, "definition.gunModels", errors, rowIndex); Check(definition.WorldGunModels.Count, WeaponDef.GunModelCount, "definition.worldGunModels", errors, rowIndex); Check(definition.WorldModels.Count, 4, "definition.worldModels", errors, rowIndex); Check(definition.SoundAliasNames.Count, WeaponDef.WeaponSoundAliasCount, "definition.soundAliasNames", errors, rowIndex);
        if (definition.BounceSoundNames.Count is not 0 and not WeaponDef.SurfaceCount)
            errors.Add(Error("definition.bounceSoundNames", $"Requires either 0 elements for a null table or exactly {WeaponDef.SurfaceCount} elements.", rowIndex));
        Check(definition.RightHandAnimationNames.Count, WeaponDef.WeaponAnimCount, "definition.rightHandAnimationNames", errors, rowIndex); Check(definition.LeftHandAnimationNames.Count, WeaponDef.WeaponAnimCount, "definition.leftHandAnimationNames", errors, rowIndex); Check(definition.LocationDamageMultipliers.Count, WeaponDef.HitLocationCount, "definition.locationDamageMultipliers", errors, rowIndex); Check(definition.Projectile.ParallelBounce.Count, WeaponDef.SurfaceCount, "definition.projectile.parallelBounce", errors, rowIndex); Check(definition.Projectile.PerpendicularBounce.Count, WeaponDef.SurfaceCount, "definition.projectile.perpendicularBounce", errors, rowIndex); Check(definition.Turret.BarrelSpinUpSoundNames.Count, WeaponDef.TurretBarrelSpinSoundCount, "definition.turret.barrelSpinUpSoundNames", errors, rowIndex); Check(definition.Turret.BarrelSpinDownSoundNames.Count, WeaponDef.TurretBarrelSpinSoundCount, "definition.turret.barrelSpinDownSoundNames", errors, rowIndex);
        Check(definition.NoteTrackMaps.SoundMapKeys.Count, WeaponDef.NoteTrackMapCount, "definition.noteTrackMaps.soundMapKeys", errors, rowIndex); Check(definition.NoteTrackMaps.SoundMapValues.Count, WeaponDef.NoteTrackMapCount, "definition.noteTrackMaps.soundMapValues", errors, rowIndex); Check(definition.NoteTrackMaps.RumbleMapKeys.Count, WeaponDef.NoteTrackMapCount, "definition.noteTrackMaps.rumbleMapKeys", errors, rowIndex); Check(definition.NoteTrackMaps.RumbleMapValues.Count, WeaponDef.NoteTrackMapCount, "definition.noteTrackMaps.rumbleMapValues", errors, rowIndex);
        EnumValue(definition.WeaponType, "definition.weaponType", errors, rowIndex); EnumValue(definition.WeaponClass, "definition.weaponClass", errors, rowIndex); EnumValue(definition.PenetrateType, "definition.penetrateType", errors, rowIndex); EnumValue(definition.InventoryType, "definition.inventoryType", errors, rowIndex); EnumValue(definition.FireType, "definition.fireType", errors, rowIndex); EnumValue(definition.OffhandClass, "definition.offhandClass", errors, rowIndex); EnumValue(definition.Stance, "definition.stance", errors, rowIndex); EnumValue(definition.Reticle.ActiveType, "definition.reticle.activeType", errors, rowIndex); EnumValue(definition.Icons.AmmoCounterClip, "definition.icons.ammoCounterClip", errors, rowIndex); EnumValue(definition.Overlay.Reticle, "definition.overlay.reticle", errors, rowIndex); EnumValue(definition.Overlay.Interface, "definition.overlay.interface", errors, rowIndex); EnumValue(definition.Projectile.Explosion, "definition.projectile.explosion", errors, rowIndex); EnumValue(definition.Projectile.Stickiness, "definition.projectile.stickiness", errors, rowIndex); EnumValue(definition.Projectile.GuidedMissileType, "definition.projectile.guidedMissileType", errors, rowIndex);
        foreach ((string path, string? value) in Strings(variant, definition)) String(value, path, errors, rowIndex);
        foreach ((string path, float value) in Floats(variant, definition)) if (!float.IsFinite(value)) errors.Add(Error(path, "Value must be finite.", rowIndex));
        return errors;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IWeaponBuildData data = (IWeaponBuildData)buildData; WeaponVariantDef v = data.Variant; WeaponDef d = data.Definition; WeaponReferenceBuildData l = data.References;
        var all = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        try
        {
            EmissionAddress variantRoot = plan.Allocate(VariantSize, 4);
            plan.Push(XFileBlockType.LARGE);
            PlannedString? variantName;
            int variantNameSourceCount;
            DefinitionPlan definition;
            PlannedString? displayName;
            DirectRegionPlan hideTags;
            PointerTablePlan animationNames;
            PlannedString? alternateName;
            ExternalPlan? killIcon;
            ExternalPlan? dpadIcon;
            DirectRegionPlan? graphKnots;
            DirectRegionPlan? originalGraphKnots;
            try
            {
                variantName = StringPlan(v.InternalName, plan, all, source); variantNameSourceCount = source.Count;
                definition = PlanDefinition(d, l, v, data.LinkerProvenance, plan, all);
                displayName = StringPlan(v.DisplayName, plan, all, source);
                hideTags = ScriptStrings(
                    v.HideTags,
                    "variant.hideTags",
                    plan,
                    all,
                    data.LinkerProvenance.HideTagsStorage); Add(source, hideTags);
                animationNames = XStrings(v.AnimationNames, plan, all); Add(source, animationNames);
                alternateName = StringPlan(v.AlternateWeaponName, plan, all, source);
                killIcon = External(
                    l.KillIcon,
                    MaterialSize,
                    plan,
                    all,
                    Offset(variantRoot, 0x48),
                    data.LinkerProvenance.KillIconForm); Add(source, killIcon);
                dpadIcon = External(
                    l.DpadIcon,
                    MaterialSize,
                    plan,
                    all,
                    Offset(variantRoot, 0x4c),
                    data.LinkerProvenance.DpadIconForm); Add(source, dpadIcon);
                graphKnots = Vec2s(v.AccuracyGraphKnots, plan, all); Add(source, graphKnots); originalGraphKnots = Vec2s(v.OriginalAccuracyGraphKnots, plan, all); Add(source, originalGraphKnots);
            }
            finally
            {
                plan.Pop(XFileBlockType.LARGE);
            }

            var writer = new XSourceWriter();
            writer.WriteInt32(Pointer(variantName)); writer.WriteInt32(Pointer(definition)); writer.WriteInt32(Pointer(displayName)); writer.WriteInt32(Pointer(hideTags)); writer.WriteInt32(Pointer(animationNames));
            writer.WriteSingle(v.AdsZoomFov); writer.WriteInt32(v.AdsTransitionInTime); writer.WriteInt32(v.AdsTransitionOutTime); writer.WriteInt32(v.ClipSize); writer.WriteInt32(v.ImpactType); writer.WriteInt32(v.FireTime); writer.WriteInt32(v.DpadIconRatio); writer.WriteSingle(v.PenetrateMultiplier); writer.WriteSingle(v.AdsViewKickCenterSpeed); writer.WriteSingle(v.HipViewKickCenterSpeed);
            writer.WriteInt32(Pointer(alternateName)); writer.WriteUInt32(v.AlternateWeaponIndex); writer.WriteInt32(v.AlternateRaiseTime); writer.WriteInt32(Pointer(killIcon)); writer.WriteInt32(Pointer(dpadIcon)); writer.WriteInt32(v.DropAmmoMin); writer.WriteInt32(v.FirstRaiseTime); writer.WriteInt32(v.DropAmmoMax); writer.WriteSingle(v.AdsDofStart); writer.WriteSingle(v.AdsDofEnd); writer.WriteUInt16(v.AccuracyGraphKnotCount); writer.WriteUInt16(v.OriginalAccuracyGraphKnotCount); writer.WriteInt32(Pointer(graphKnots)); writer.WriteInt32(Pointer(originalGraphKnots)); writer.WriteByte(v.MotionTracker); writer.WriteByte(v.Enhanced); writer.WriteByte(v.DpadIconShowsAmmo); writer.WriteByte(v.Padding73);
            Exact(writer, VariantSize, "WeaponVariantDef"); EmissionBlockSegment variant = new(variantRoot, writer.ToArray()); all.Add(variant);
            // The variant root is followed by its first string and then the
            // nested WeaponDef exactly as ReadWeaponVariantChildren consumes it.
            var ordered = new List<EmissionBlockSegment> { variant };
            ordered.AddRange(source.Take(variantNameSourceCount));
            ordered.AddRange(definition.Source);
            ordered.AddRange(source.Skip(variantNameSourceCount));
            return new AssetBodyEmission(AssetType, variantRoot, all, ordered);
        }
        finally
        {
            plan.Pop(XFileBlockType.TEMP);
        }
    }

    private static DefinitionPlan PlanDefinition(
        WeaponDef d,
        WeaponReferenceBuildData l,
        WeaponVariantDef v,
        WeaponLinkerProvenance provenance,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        const string serializedView = "WeaponDef:0x684";
        if (plan.TryGetPersistentObjectAlias(d, serializedView, out EmissionAddress existing))
            return new DefinitionPlan(null, [], existing.ToPackedPointer());
        EmissionAddress rootAddress = plan.Allocate(DefinitionSize, 4);
        plan.RegisterPersistentObjectAlias(d, serializedView, rootAddress);
        var source = new List<EmissionBlockSegment>();
        PlannedString? name = StringPlan(d.InternalName, plan, all, source);
        PointerTablePlan gunModels = Externals(l.GunModels, XModelSize, plan, all); Add(source, gunModels);
        ExternalPlan? handModel = External(l.HandModel, XModelSize, plan, all, Offset(rootAddress, 0x008)); Add(source, handModel);
        PointerTablePlan rightAnims = XStrings(d.RightHandAnimationNames, plan, all); Add(source, rightAnims);
        PointerTablePlan leftAnims = XStrings(d.LeftHandAnimationNames, plan, all); Add(source, leftAnims);
        PlannedString? mode = StringPlan(d.ModeName, plan, all, source);
        DirectRegionPlan soundKeys = ScriptStrings(d.NoteTrackMaps.SoundMapKeys, "definition.noteTrackMaps.soundMapKeys", plan, all); Add(source, soundKeys); DirectRegionPlan soundValues = ScriptStrings(d.NoteTrackMaps.SoundMapValues, "definition.noteTrackMaps.soundMapValues", plan, all); Add(source, soundValues); DirectRegionPlan rumbleKeys = ScriptStrings(d.NoteTrackMaps.RumbleMapKeys, "definition.noteTrackMaps.rumbleMapKeys", plan, all); Add(source, rumbleKeys); DirectRegionPlan rumbleValues = ScriptStrings(d.NoteTrackMaps.RumbleMapValues, "definition.noteTrackMaps.rumbleMapValues", plan, all); Add(source, rumbleValues);
        IReadOnlyList<ExternalPlan?> flashEffects = ExternalList(l.FlashEffects, FxSize, plan, all, Offset(rootAddress, 0x048)); foreach (ExternalPlan? value in flashEffects) Add(source, value);
        IReadOnlyList<SoundCellPlan?> weaponSounds = SoundCells(d.SoundAliasNames, plan, all); foreach (SoundCellPlan? value in weaponSounds) Add(source, value);
        PointerTablePlan bounce = SoundCellTable(d.BounceSoundNames, plan, all); Add(source, bounce);
        IReadOnlyList<ExternalPlan?> effects = ExternalList(l.Effects, FxSize, plan, all, Offset(rootAddress, 0x110)); foreach (ExternalPlan? value in effects) Add(source, value); IReadOnlyList<ExternalPlan?> materials = ExternalList(l.Materials, MaterialSize, plan, all, Offset(rootAddress, 0x120)); foreach (ExternalPlan? value in materials) Add(source, value);
        PointerTablePlan worldGuns = Externals(
            l.WorldGunModels,
            XModelSize,
            plan,
            all,
            provenance.WorldGunModelsStorage); Add(source, worldGuns); IReadOnlyList<ExternalPlan?> worldModels = ExternalList(l.WorldModels, XModelSize, plan, all, Offset(rootAddress, 0x1dc)); foreach (ExternalPlan? value in worldModels) Add(source, value); IReadOnlyList<ExternalPlan?> iconMaterials = ExternalListAtOffsets(l.IconMaterials, MaterialSize, plan, all, rootAddress, [0x1ec, 0x1f4, 0x1fc]); foreach (ExternalPlan? value in iconMaterials) Add(source, value);
        PlannedString? ammo = StringPlan(d.Ammo.AmmoName, plan, all, source); PlannedString? clip = StringPlan(d.Ammo.ClipName, plan, all, source); PlannedString? sharedAmmo = StringPlan(d.Ammo.SharedAmmoCapName, plan, all, source);
        IReadOnlyList<ExternalPlan?> overlayMaterials = ExternalList(l.OverlayMaterials, MaterialSize, plan, all, Offset(rootAddress, 0x308)); foreach (ExternalPlan? value in overlayMaterials) Add(source, value); ExternalPlan? collmap = External(l.PhysCollmap, PhysCollmapSize, plan, all, Offset(rootAddress, 0x3c8)); Add(source, collmap);
        ExternalPlan? projectileModel = External(l.ProjectileModel, XModelSize, plan, all, Offset(rootAddress, 0x420)); Add(source, projectileModel); IReadOnlyList<ExternalPlan?> projectileEffects = ExternalList(l.ProjectileEffects, FxSize, plan, all, Offset(rootAddress, 0x428)); foreach (ExternalPlan? value in projectileEffects) Add(source, value); IReadOnlyList<SoundCellPlan?> projectileSounds = SoundCells([d.Projectile.ExplosionSound, d.Projectile.DudSound], plan, all); foreach (SoundCellPlan? value in projectileSounds) Add(source, value); DirectRegionPlan parallel = Floats(d.Projectile.ParallelBounce, 4, plan, all); Add(source, parallel); DirectRegionPlan perpendicular = Floats(d.Projectile.PerpendicularBounce, 4, plan, all); Add(source, perpendicular); IReadOnlyList<ExternalPlan?> impactEffects = ExternalList(l.ImpactEffects, FxSize, plan, all, Offset(rootAddress, 0x44c)); foreach (ExternalPlan? value in impactEffects) Add(source, value); ExternalPlan? ignitionEffect = External(l.IgnitionEffect, FxSize, plan, all, Offset(rootAddress, 0x46c)); Add(source, ignitionEffect); SoundCellPlan? ignitionSound = SoundCell(d.Projectile.IgnitionSound, plan, all); Add(source, ignitionSound);
        PlannedString? graph0 = StringPlan(d.Accuracy.GraphName0, plan, all, source); DirectRegionPlan? definitionGraph0 = Vec2s(d.Accuracy.GraphKnots, plan, all); Add(source, definitionGraph0); PlannedString? graph1 = StringPlan(d.Accuracy.GraphName1, plan, all, source); DirectRegionPlan? definitionGraph1 = Vec2s(d.Accuracy.OriginalGraphKnots, plan, all); Add(source, definitionGraph1);
        PlannedString? useHint = StringPlan(d.Hints.UseHintString, plan, all, source); PlannedString? dropHint = StringPlan(d.Hints.DropHintString, plan, all, source); PlannedString? script = StringPlan(d.ScriptName, plan, all, source); DirectRegionPlan locationDamage = Floats(d.LocationDamageMultipliers, 4, plan, all); Add(source, locationDamage); PlannedString? fireRumble = StringPlan(d.Rumble.FireRumble, plan, all, source); PlannedString? meleeRumble = StringPlan(d.Rumble.MeleeImpactRumble, plan, all, source); ExternalPlan? tracer = External(l.Tracer, TracerSize, plan, all, Offset(rootAddress, 0x5c0)); Add(source, tracer);
        SoundCellPlan? overheatSound = SoundCell(d.Turret.OverheatSound, plan, all); Add(source, overheatSound); ExternalPlan? overheatFx = External(l.TurretOverheatEffect, FxSize, plan, all, Offset(rootAddress, 0x5e0)); Add(source, overheatFx); PlannedString? barrelRumble = StringPlan(d.Turret.BarrelSpinRumble, plan, all, source); SoundCellPlan? maxSound = SoundCell(d.Turret.BarrelSpinMaxSound, plan, all); Add(source, maxSound); IReadOnlyList<SoundCellPlan?> barrelUp = SoundCells(d.Turret.BarrelSpinUpSoundNames, plan, all); foreach (SoundCellPlan? value in barrelUp) Add(source, value); IReadOnlyList<SoundCellPlan?> barrelDown = SoundCells(d.Turret.BarrelSpinDownSoundNames, plan, all); foreach (SoundCellPlan? value in barrelDown) Add(source, value); SoundCellPlan? missile = SoundCell(d.MissileConeSound.Alias, plan, all); Add(source, missile); SoundCellPlan? missileBase = SoundCell(d.MissileConeSound.AliasAtBase, plan, all); Add(source, missileBase);

        var w = new XSourceWriter();
        w.WriteInt32(Pointer(name)); w.WriteInt32(Pointer(gunModels)); w.WriteInt32(Pointer(handModel)); w.WriteInt32(Pointer(rightAnims)); w.WriteInt32(Pointer(leftAnims)); w.WriteInt32(Pointer(mode)); w.WriteInt32(Pointer(soundKeys)); w.WriteInt32(Pointer(soundValues)); w.WriteInt32(Pointer(rumbleKeys)); w.WriteInt32(Pointer(rumbleValues));
        w.WriteInt32(d.PlayerAnimType); w.WriteInt32((int)d.WeaponType); w.WriteInt32((int)d.WeaponClass); w.WriteInt32((int)d.PenetrateType); w.WriteInt32((int)d.InventoryType); w.WriteInt32((int)d.FireType); w.WriteInt32((int)d.OffhandClass); w.WriteInt32((int)d.Stance);
        foreach (ExternalPlan? value in flashEffects) w.WriteInt32(Pointer(value));
        foreach (SoundCellPlan? value in weaponSounds) w.WriteInt32(Pointer(value));
        w.WriteInt32(Pointer(bounce)); foreach (ExternalPlan? value in effects) w.WriteInt32(Pointer(value)); foreach (ExternalPlan? value in materials) w.WriteInt32(Pointer(value));
        WriteReticle(w, d.Reticle); WriteView(w, d.ViewMovement); WritePositional(w, d.PositionalMovement); w.WriteInt32(Pointer(worldGuns)); foreach (ExternalPlan? value in worldModels) w.WriteInt32(Pointer(value)); WriteIcons(w, d.Icons, iconMaterials); WriteAmmo(w, d.Ammo, ammo, clip, sharedAmmo); WriteTiming(w, d.Timing); WriteAim(w, d.AimMovementTuning); foreach (ExternalPlan? value in overlayMaterials) w.WriteInt32(Pointer(value)); w.WriteInt32((int)d.Overlay.Reticle); w.WriteInt32((int)d.Overlay.Interface); w.WriteInt32(d.Overlay.Width); w.WriteInt32(d.Overlay.Height); w.WriteInt32(d.Overlay.WidthSplitscreen); w.WriteInt32(d.Overlay.HeightSplitscreen); WriteAds(w, d.AdsViewAndSpread); w.WriteInt32(Pointer(collmap)); WritePhysics(w, d.Physics);
        WriteProjectile(w, d.Projectile, projectileModel, projectileEffects, projectileSounds, parallel, perpendicular, impactEffects, ignitionEffect, ignitionSound); WriteAccuracy(w, d.Accuracy, graph0, graph1, definitionGraph0, definitionGraph1); WriteTurn(w, d.TurnSpeedAndRange); WriteHints(w, d.Hints, useHint, dropHint); w.WriteInt32(Pointer(script)); w.WriteSingle(d.OOPosAnimLength); w.WriteSingle(d.MinDamage); w.WriteInt32(d.MinPlayerDamage); w.WriteSingle(d.MaxDamageRange); w.WriteSingle(d.MinDamageRange); w.WriteSingle(d.DestabilizationRateTime); w.WriteSingle(d.DestabilizationCurvatureMax); w.WriteSingle(d.DestabilizeDistance); w.WriteInt32(d.DestabilizeDistanceToTimeScale); w.WriteInt32(Pointer(locationDamage)); w.WriteInt32(Pointer(fireRumble)); w.WriteInt32(Pointer(meleeRumble)); w.WriteInt32(Pointer(tracer)); w.WriteSingle(d.TurretScopeZoomRate); w.WriteSingle(d.TurretScopeZoomMin); w.WriteSingle(d.TurretScopeZoomMax); w.WriteSingle(d.TurretOverheatUpRate); w.WriteSingle(d.TurretOverheatDownRate); w.WriteSingle(d.TurretOverheatPenalty); WriteTurret(w, d.Turret, overheatSound, overheatFx, barrelRumble, maxSound, barrelUp, barrelDown); WriteMissile(w, d.MissileConeSound, missile, missileBase); WriteTail(w, d.TailFlags);
        Exact(w, DefinitionSize, "WeaponDef"); EmissionBlockSegment root = new(rootAddress, w.ToArray()); all.Add(root); return new DefinitionPlan(root, [root, .. source], -1);
    }

    // Plan helpers below intentionally retain source order rather than sorting addresses.
    private static PlannedString? StringPlan(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source) { int before = all.Count; PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases); source.AddRange(all.Skip(before)); return result; }
    private static ExternalPlan? External(
        SymbolicXAssetReference? reference,
        int size,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        EmissionAddress ownerCell,
        WeaponNestedPointerSourceForm sourceForm = WeaponNestedPointerSourceForm.Inline)
    {
        if (reference is null)
            return null;
        string aliasKey = $"{(int)reference.AssetType}\u0000{reference.OriginalSerializedName.TrimStart(',')}";
        bool insert = sourceForm == WeaponNestedPointerSourceForm.Insert;
        if (!insert &&
            plan.PersistentXAssetAliasCells.TryGetValue(aliasKey, out EmissionAddress existingCell))
        {
            return new ExternalPlan(null, [], existingCell.ToPackedPointer());
        }
        if (insert)
        {
            plan.AllocateInsertPointerCell(
                "Weapon",
                $"insert:{reference.AssetType}:{reference.OriginalSerializedName}");
        }

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(size, 4);
        plan.Push(XFileBlockType.LARGE);
        int before = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(
            reference.OriginalSerializedName,
            plan,
            all,
            plan.StringAliases);
        EmissionBlockSegment[] strings = all.Skip(before).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var w = new XSourceWriter();
        w.WriteInt32(Pointer(name));
        w.Reserve(size - sizeof(int));
        var segment = new EmissionBlockSegment(root, w.ToArray());
        all.Add(segment);
        if (ownerCell.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new ExternalPlan(
            segment,
            [segment, .. strings],
            insert ? -2 : -1);
    }
    private static PointerTablePlan Externals(
        IReadOnlyList<SymbolicXAssetReference?> values,
        int size,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        WeaponReusableStorageToken? reusableStorage = null)
    {
        byte[]? nullTableFingerprint = values.All(value => value is null)
            ? new byte[checked(values.Count * sizeof(int))]
            : null;
        if (reusableStorage is { } token &&
            nullTableFingerprint is not null &&
            plan.TryGetReusableStorage(
                token.Value,
                nullTableFingerprint,
                out EmissionAddress reusable))
        {
            return new PointerTablePlan(null, [], reusable.ToPackedPointer());
        }
        string view = $"XAssetAliasCell[{values.Count}]:0x{size:X}";
        if (plan.TryGetPersistentObjectAlias(values, view, out EmissionAddress existing))
            return new PointerTablePlan(null, [], existing.ToPackedPointer());
        EmissionAddress address = plan.Allocate(checked(values.Count * 4), 4);
        plan.RegisterPersistentObjectAlias(values, view, address);
        ExternalPlan?[] children = values.Select((value, index) => External(value, size, plan, all, Offset(address, index * sizeof(int)))).ToArray();
        var w = new XSourceWriter();
        foreach (ExternalPlan? child in children) w.WriteInt32(Pointer(child));
        var table = new EmissionBlockSegment(address, w.ToArray());
        all.Add(table);
        if (reusableStorage is { } createdToken)
            plan.RegisterReusableStorage(createdToken.Value, table.Bytes.Span, address);
        return new PointerTablePlan(table, [table, .. children.SelectMany(value => value?.Source ?? [])], -1);
    }
    private static IReadOnlyList<ExternalPlan?> ExternalList(IReadOnlyList<SymbolicXAssetReference?> values, int size, EmissionPlan plan, List<EmissionBlockSegment> all, EmissionAddress firstOwnerCell) => values.Select((value, index) => External(value, size, plan, all, Offset(firstOwnerCell, index * sizeof(int)))).ToArray();
    private static IReadOnlyList<ExternalPlan?> ExternalListAtOffsets(IReadOnlyList<SymbolicXAssetReference?> values, int size, EmissionPlan plan, List<EmissionBlockSegment> all, EmissionAddress ownerRoot, IReadOnlyList<int> offsets) { if (values.Count != offsets.Count) throw new InvalidDataException("Weapon external offset map does not match its value count."); return values.Select((value, index) => External(value, size, plan, all, Offset(ownerRoot, offsets[index]))).ToArray(); }
    private static PointerTablePlan XStrings(IReadOnlyList<string?> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        string view = $"XString[{values.Count}]";
        if (plan.TryGetPersistentObjectAlias(values, view, out EmissionAddress existing))
            return new PointerTablePlan(null, [], existing.ToPackedPointer());
        EmissionAddress address = plan.Allocate(checked(values.Count * 4), 4);
        plan.RegisterPersistentObjectAlias(values, view, address);
        var strings = new List<EmissionBlockSegment>();
        var planned = new PlannedString?[values.Count];
        for (int i = 0; i < values.Count; i++) planned[i] = StringPlan(values[i], plan, all, strings);
        var w = new XSourceWriter();
        foreach (PlannedString? value in planned) w.WriteInt32(Pointer(value));
        var table = new EmissionBlockSegment(address, w.ToArray());
        all.Add(table);
        return new PointerTablePlan(table, [table, .. strings], -1);
    }
    private static PointerTablePlan SoundCellTable(IReadOnlyList<string?> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0)
            return new PointerTablePlan(null, [], 0);
        string view = $"SoundAliasCell[{values.Count}]";
        if (plan.TryGetPersistentObjectAlias(values, view, out EmissionAddress existing))
            return new PointerTablePlan(null, [], existing.ToPackedPointer());
        EmissionAddress address = plan.Allocate(checked(values.Count * 4), 4);
        plan.RegisterPersistentObjectAlias(values, view, address);
        SoundCellPlan?[] cells = values.Select(value => SoundCell(value, plan, all)).ToArray();
        var w = new XSourceWriter();
        foreach (SoundCellPlan? cell in cells) w.WriteInt32(Pointer(cell));
        var table = new EmissionBlockSegment(address, w.ToArray());
        all.Add(table);
        return new PointerTablePlan(table, [table, .. cells.SelectMany(value => value?.Source ?? [])], -1);
    }
    private static SoundCellPlan? SoundCell(string? value, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (value is null)
            return null;
        if (plan.PersistentSoundAliasCells.TryGetValue(value, out EmissionAddress existing))
            return new SoundCellPlan(null, [], existing.ToPackedPointer());

        EmissionAddress address = plan.Allocate(4, 4);
        int before = all.Count;
        PlannedString? text = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases);
        EmissionBlockSegment[] strings = all.Skip(before).ToArray();
        var w = new XSourceWriter();
        w.WriteInt32(Pointer(text));
        var cell = new EmissionBlockSegment(address, w.ToArray());
        all.Add(cell);
        if (address.Block != XFileBlockType.TEMP)
            plan.PersistentSoundAliasCells.TryAdd(value, address);
        return new SoundCellPlan(cell, [cell, .. strings], -1);
    }
    private static IReadOnlyList<SoundCellPlan?> SoundCells(IReadOnlyList<string?> values, EmissionPlan plan, List<EmissionBlockSegment> all) => values.Select(value => SoundCell(value, plan, all)).ToArray();
    private static DirectRegionPlan ScriptStrings(
        IReadOnlyList<ScriptStringReference> values,
        string path,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        WeaponReusableStorageToken? reusableStorage = null) =>
        Region(
            values,
            $"ScriptString[{values.Count}]",
            sizeof(ushort),
            2,
            plan,
            all,
            (writer, index) =>
                writer.WriteUInt16(ScriptStringEmissionScope.Resolve(values[index], $"{path}[{index}]")),
            reusableStorage);
    private static DirectRegionPlan Floats(IReadOnlyList<float> values, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all) =>
        Region(values, $"Single[{values.Count}]", sizeof(float), alignment, plan, all, (writer, index) => writer.WriteSingle(values[index]));
    private static DirectRegionPlan? Vec2s(IReadOnlyList<Vec2> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        return Region(values, $"Vec2[{values.Count}]", 2 * sizeof(float), 4, plan, all, (writer, index) =>
        {
            writer.WriteSingle(values[index].a);
            writer.WriteSingle(values[index].b);
        });
    }
    private static DirectRegionPlan Region<T>(
        IReadOnlyList<T> values,
        string view,
        int elementSize,
        int alignment,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        Action<XSourceWriter, int> write,
        WeaponReusableStorageToken? reusableStorage = null)
    {
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
            write(writer, index);
        byte[] bytes = writer.ToArray();
        if (reusableStorage is { } token &&
            plan.TryGetReusableStorage(
                token.Value,
                bytes,
                out EmissionAddress reusable))
        {
            return new DirectRegionPlan(null, [], reusable.ToPackedPointer());
        }
        if (plan.TryGetPersistentObjectAlias(values, view, out EmissionAddress existing))
            return new DirectRegionPlan(null, [], existing.ToPackedPointer());
        EmissionAddress address = plan.Allocate(checked(values.Count * elementSize), alignment);
        plan.RegisterPersistentObjectAlias(values, view, address);
        var segment = new EmissionBlockSegment(address, bytes);
        all.Add(segment);
        if (reusableStorage is { } createdToken)
            plan.RegisterReusableStorage(createdToken.Value, bytes, address);
        return new DirectRegionPlan(segment, [segment], -1);
    }
    private static int Pointer(PlannedString? value) => AssetBodyEmitterHelpers.SourcePointer(value);
    private static int Pointer(DirectRegionPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(PointerTablePlan value) => value.PointerRaw;
    private static int Pointer(DefinitionPlan value) => value.PointerRaw;
    private static int Pointer(ExternalPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(SoundCellPlan? value) => value?.PointerRaw ?? 0;
    private static void Add(List<EmissionBlockSegment> source, ExternalPlan? value) { if (value is not null) source.AddRange(value.Source); }
    private static void Add(List<EmissionBlockSegment> source, SoundCellPlan? value) { if (value is not null) source.AddRange(value.Source); }
    private static void Add(List<EmissionBlockSegment> source, DirectRegionPlan? value) { if (value is not null) source.AddRange(value.Source); }
    private static void Add(List<EmissionBlockSegment> source, PointerTablePlan value) => source.AddRange(value.Source);
    private static void WriteReticle(XSourceWriter w, WeaponReticleFields x) { w.WriteInt32(x.CenterSize); w.WriteInt32(x.SideSize); w.WriteInt32(x.MinOffset); w.WriteInt32((int)x.ActiveType); }
    private static void WriteView(XSourceWriter w, WeaponViewMovementFields x) { foreach (Vec3 value in new[] { x.StandMove, x.StandRotation, x.StrafeMove, x.StrafeRotation, x.DuckedOffset, x.DuckedMove, x.DuckedRotation, x.ProneOffset, x.ProneMove, x.ProneRotation }) Vec3(w, value); }
    private static void WritePositional(XSourceWriter w, WeaponPositionalMovementFields x) { foreach (float value in new[] { x.PositionMoveRate, x.PositionProneMoveRate, x.StandMoveMinSpeed, x.DuckedMoveMinSpeed, x.ProneMoveMinSpeed, x.PositionRotationRate, x.PositionProneRotationRate, x.StandRotationMinSpeed, x.DuckedRotationMinSpeed, x.ProneRotationMinSpeed }) w.WriteSingle(value); }
    private static void WriteIcons(XSourceWriter w, WeaponIconPointers x, IReadOnlyList<ExternalPlan?> links) { w.WriteInt32(Pointer(links[0])); w.WriteInt32(x.HudIconRatio); w.WriteInt32(Pointer(links[1])); w.WriteInt32(x.PickupIconRatio); w.WriteInt32(Pointer(links[2])); w.WriteInt32(x.AmmoCounterIconRatio); w.WriteInt32((int)x.AmmoCounterClip); w.WriteInt32(x.StartAmmo); }
    private static void WriteAmmo(XSourceWriter w, WeaponAmmoFields x, PlannedString? ammo, PlannedString? clip, PlannedString? shared) { w.WriteInt32(Pointer(ammo)); w.WriteInt32(x.AmmoIndex); w.WriteInt32(Pointer(clip)); w.WriteInt32(x.ClipIndex); w.WriteInt32(x.MaxAmmo); w.WriteInt32(x.ShotCount); w.WriteInt32(Pointer(shared)); w.WriteInt32(x.SharedAmmoCapIndex); w.WriteInt32(x.SharedAmmoCap); w.WriteInt32(x.Damage); w.WriteInt32(x.PlayerDamage); w.WriteInt32(x.MeleeDamage); w.WriteInt32(x.DamageType); }
    private static void WriteTiming(XSourceWriter w, WeaponTimingFields x) { foreach (int value in new[] { x.FireDelay, x.MeleeDelay, x.MeleeChargeDelay, x.DetonateDelay, x.RechamberTime, x.RechamberTimeOneHanded, x.RechamberBoltTime, x.HoldFireTime, x.DetonateTime, x.MeleeTime, x.MeleeChargeTime, x.ReloadTime, x.ReloadShowRocketTime, x.ReloadEmptyTime, x.ReloadAddTime, x.ReloadStartTime, x.ReloadStartAddTime, x.ReloadEndTime, x.DropTime, x.RaiseTime, x.AltDropTime, x.QuickDropTime, x.QuickRaiseTime, x.BreachRaiseTime, x.EmptyRaiseTime, x.EmptyDropTime, x.SprintInTime, x.SprintLoopTime, x.SprintOutTime, x.StunnedTimeBegin, x.StunnedTimeLoop, x.StunnedTimeEnd, x.NightVisionWearTime, x.NightVisionWearTimeFadeOutEnd, x.NightVisionWearTimePowerUp, x.NightVisionRemoveTime, x.NightVisionRemoveTimePowerDown, x.NightVisionRemoveTimeFadeInStart, x.FuseTime, x.AiFuseTime }) w.WriteInt32(value); }
    private static void WriteAim(XSourceWriter w, WeaponAimMovementTuningFields x) { foreach (float value in new[] { x.AutoAimRange, x.AimAssistRange, x.AimAssistRangeAds, x.AimPadding, x.EnemyCrosshairRange, x.MoveSpeedScale, x.AdsMoveSpeedScale, x.SprintDurationScale, x.AdsZoomInFraction, x.AdsZoomOutFraction }) w.WriteSingle(value); }
    private static void WriteAds(XSourceWriter w, WeaponAdsViewAndSpreadFields x) { foreach (float value in new[] { x.AdsBobFactor, x.AdsViewBobMultiplier, x.HipSpreadStandMin, x.HipSpreadDuckedMin, x.HipSpreadProneMin, x.HipSpreadStandMax, x.HipSpreadDuckedMax, x.HipSpreadProneMax, x.HipSpreadDecayRate, x.HipSpreadFireAdd, x.HipSpreadTurnAdd, x.HipSpreadMoveAdd, x.HipSpreadDuckedDecay, x.HipSpreadProneDecay, x.HipReticleSidePosition, x.AdsIdleAmount, x.HipIdleAmount, x.AdsIdleSpeed, x.HipIdleSpeed, x.IdleCrouchFactor, x.IdleProneFactor, x.GunMaxPitch, x.GunMaxYaw, x.SwayMaxAngle, x.SwayLerpSpeed, x.SwayPitchScale, x.SwayYawScale, x.SwayHorizontalScale, x.SwayVerticalScale, x.SwayShellShockScale, x.AdsSwayMaxAngle, x.AdsSwayLerpSpeed, x.AdsSwayPitchScale, x.AdsSwayYawScale, x.AdsSwayHorizontalScale, x.AdsSwayVerticalScale, x.AdsViewErrorMin, x.AdsViewErrorMax }) w.WriteSingle(value); }
    private static void WritePhysics(XSourceWriter w, WeaponPhysicsFields x) { w.WriteSingle(x.DualWieldViewModelOffset); foreach (int value in new[] { x.KillIconRatio, x.ReloadAmmoAdd, x.ReloadStartAdd, x.AmmoDropStockMin }) w.WriteInt32(value); w.WriteSingle(x.AmmoDropClipPercentMin); w.WriteSingle(x.AmmoDropClipPercentMax); foreach (int value in new[] { x.ExplosionRadius, x.ExplosionRadiusMin, x.ExplosionInnerDamage, x.ExplosionOuterDamage }) w.WriteInt32(value); w.WriteSingle(x.DamageConeAngle); w.WriteSingle(x.BulletExplosionDamageMultiplier); w.WriteSingle(x.BulletExplosionRadiusMultiplier); foreach (int value in new[] { x.ProjectileSpeed, x.ProjectileSpeedUp, x.ProjectileSpeedForward, x.ProjectileActivateDistance, x.ProjectileLifetime, x.TimeToAccelerate }) w.WriteInt32(value); w.WriteSingle(x.ProjectileCurvature); }
    private static void WriteProjectile(XSourceWriter w, WeaponProjectileFields x, ExternalPlan? model, IReadOnlyList<ExternalPlan?> effects, IReadOnlyList<SoundCellPlan?> sounds, DirectRegionPlan parallel, DirectRegionPlan perpendicular, IReadOnlyList<ExternalPlan?> impact, ExternalPlan? ignition, SoundCellPlan? sound) { w.WriteInt32(Pointer(model)); w.WriteInt32((int)x.Explosion); w.WriteInt32(Pointer(effects[0])); w.WriteInt32(Pointer(effects[1])); w.WriteInt32(Pointer(sounds[0])); w.WriteInt32(Pointer(sounds[1])); w.WriteInt32((int)x.Stickiness); w.WriteInt32(x.LowAmmoWarningThreshold); w.WriteSingle(x.RicochetChance); w.WriteInt32(Pointer(parallel)); w.WriteInt32(Pointer(perpendicular)); w.WriteInt32(Pointer(impact[0])); w.WriteInt32(Pointer(impact[1])); Vec3(w, x.ProjectileColor); w.WriteInt32((int)x.GuidedMissileType); w.WriteSingle(x.MaxSteeringAcceleration); w.WriteInt32(x.IgnitionDelay); w.WriteInt32(Pointer(ignition)); w.WriteInt32(Pointer(sound)); w.WriteSingle(x.AdsAimPitch); w.WriteSingle(x.AdsCrosshairInFraction); w.WriteSingle(x.AdsCrosshairOutFraction); WriteKick(w, x.GunKickAndDistance); }
    private static void WriteKick(XSourceWriter w, WeaponGunKickAndDistanceFields x) { w.WriteInt32(x.AdsGunKickReducedKickBullets); foreach (float value in new[] { x.AdsGunKickReducedKickPercent, x.AdsGunKickPitchMin, x.AdsGunKickPitchMax, x.AdsGunKickYawMin, x.AdsGunKickYawMax, x.AdsGunKickAcceleration, x.AdsGunKickSpeedMax, x.AdsGunKickSpeedDecay, x.AdsGunKickStaticDecay, x.AdsViewKickPitchMin, x.AdsViewKickPitchMax, x.AdsViewKickYawMin, x.AdsViewKickYawMax, x.AdsViewScatterMin, x.AdsViewScatterMax, x.AdsSpread }) w.WriteSingle(value); w.WriteInt32(x.HipGunKickReducedKickBullets); foreach (float value in new[] { x.HipGunKickReducedKickPercent, x.HipGunKickPitchMin, x.HipGunKickPitchMax, x.HipGunKickYawMin, x.HipGunKickYawMax, x.HipGunKickAcceleration, x.HipGunKickSpeedMax, x.HipGunKickSpeedDecay, x.HipGunKickStaticDecay, x.HipViewKickPitchMin, x.HipViewKickPitchMax, x.HipViewKickYawMin, x.HipViewKickYawMax, x.HipViewScatterMin, x.HipViewScatterMax, x.FightDistance, x.MaxDistance }) w.WriteSingle(value); }
    private static void WriteAccuracy(XSourceWriter w, WeaponAccuracyFields x, PlannedString? name0, PlannedString? name1, DirectRegionPlan? knots, DirectRegionPlan? original) { w.WriteInt32(Pointer(name0)); w.WriteInt32(Pointer(name1)); w.WriteInt32(Pointer(knots)); w.WriteInt32(Pointer(original)); w.WriteUInt16(x.LocalGraphKnotCount); w.WriteUInt16(x.LocalOriginalGraphKnotCount); w.WriteInt32(x.AnimationNotifyComparison); foreach (float value in new[] { x.LeftArc, x.RightArc, x.TopArc, x.BottomArc, x.Accuracy, x.AiSpread, x.PlayerSpread }) w.WriteSingle(value); }
    private static void WriteTurn(XSourceWriter w, WeaponTurnSpeedAndRangeFields x) { foreach (float value in new[] { x.MinTurnSpeed, x.MaxTurnSpeed, x.PitchConvergenceTime, x.YawConvergenceTime, x.SuppressTime, x.MaxRange, x.AnimationHorizontalRotateIncrement, x.PlayerPositionDistance, x.ScanSpeed, x.ScanAcceleration }) w.WriteSingle(value); }
    private static void WriteHints(XSourceWriter w, WeaponHintFields x, PlannedString? use, PlannedString? drop) { w.WriteInt32(Pointer(use)); w.WriteInt32(Pointer(drop)); w.WriteInt32(x.UseHintStringIndex); w.WriteInt32(x.DropHintStringIndex); w.WriteSingle(x.HorizontalViewJitter); w.WriteSingle(x.VerticalViewJitter); w.WriteSingle(x.ScanSpeed); w.WriteSingle(x.ScanAcceleration); w.WriteInt32(x.ScanPauseTime); }
    private static void WriteTurret(XSourceWriter w, WeaponTurretFields x, SoundCellPlan? overheat, ExternalPlan? effect, PlannedString? rumble, SoundCellPlan? max, IReadOnlyList<SoundCellPlan?> up, IReadOnlyList<SoundCellPlan?> down) { w.WriteInt32(Pointer(overheat)); w.WriteInt32(Pointer(effect)); w.WriteInt32(Pointer(rumble)); w.WriteSingle(x.BarrelSpinSpeed); w.WriteSingle(x.BarrelSpinUpTime); w.WriteSingle(x.BarrelSpinDownTime); w.WriteInt32(Pointer(max)); foreach (SoundCellPlan? value in up) w.WriteInt32(Pointer(value)); foreach (SoundCellPlan? value in down) w.WriteInt32(Pointer(value)); }
    private static void WriteMissile(XSourceWriter w, WeaponMissileConeSoundFields x, SoundCellPlan? alias, SoundCellPlan? baseAlias) { w.WriteInt32(Pointer(alias)); w.WriteInt32(Pointer(baseAlias)); foreach (float value in new[] { x.RadiusAtTop, x.RadiusAtBase, x.Height, x.OriginOffset, x.VolumeScaleAtCore, x.VolumeScaleAtEdge, x.VolumeScaleCoreSize, x.PitchAtTop, x.PitchAtBottom, x.PitchTopSize, x.PitchBottomSize, x.CrossfadeTopSize, x.CrossfadeBottomSize }) w.WriteSingle(value); }
    private static void WriteTail(XSourceWriter w, WeaponTailFlags x) { foreach (byte value in new[] { x.SharedAmmo, x.LockonSupported, x.RequireLockonToFire, x.BigExplosion, x.NoAdsWhenMagEmpty, x.AvoidDropCleanup, x.InheritsPerks, x.CrosshairColorChange, x.RifleBullet, x.ArmorPiercing, x.BoltAction, x.AimDownSight, x.RechamberWhileAds, x.BulletExplosiveDamage, x.CookOffHold, x.ClipOnly, x.NoAmmoPickup, x.AdsFireOnly, x.CancelAutoHolsterWhenEmpty, x.DisableSwitchToWhenEmpty, x.SuppressAmmoReserveDisplay, x.LaserSightDuringNightvision, x.MarkableViewmodel, x.NoDualWield, x.FlipKillIcon, x.NoPartialReload, x.SegmentedReload, x.BlocksProne, x.Silenced, x.IsRollingGrenade, x.ProjectileExplosionEffectForceNormalUp, x.ProjectileImpactExplode, x.StickToPlayers, x.HasDetonator, x.DisableFiring, x.TimedDetonation, x.Rotate, x.HoldButtonToThrow, x.FreezeMovementWhenFiring, x.ThermalScope, x.AltModeSameWeapon, x.TurretBarrelSpinEnabled, x.MissileConeSoundEnabled, x.MissileConeSoundPitchShiftEnabled, x.MissileConeSoundCrossfadeEnabled, x.OffhandHoldIsCancelable }) w.WriteByte(value); w.WriteUInt16(x.ReservedPadding); }
    private static void Vec3(XSourceWriter w, Vec3 value) { w.WriteSingle(value.X); w.WriteSingle(value.Y); w.WriteSingle(value.Z); }
    private static void Exact(XSourceWriter writer, int size, string name) { if (writer.Position != size) throw new InvalidDataException($"{name} emission produced 0x{writer.Position:X} bytes instead of 0x{size:X}."); }
    private static void Check(int actual, int expected, string path, List<EmissionError> errors, int? row) { if (actual != expected) errors.Add(Error(path, $"Requires exactly {expected} elements.", row)); }
    private static void CheckLinks(IReadOnlyList<SymbolicXAssetReference?> values, int count, XAssetType type, string path, List<EmissionError> errors, int? row) { Check(values.Count, count, path, errors, row); for (int i = 0; i < values.Count; i++) Link(values[i], type, $"{path}[{i}]", errors, row); }
    private static void Link(SymbolicXAssetReference? value, XAssetType type, string path, List<EmissionError> errors, int? row) { if (value is null) return; if (value.AssetType != type || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName)) errors.Add(Error(path, $"Requires a Latin-1 comma-prefixed {type} external reference.", row)); }
    private static void String(string? value, string path, List<EmissionError> errors, int? row) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "XString must be a Latin-1 C string.", row)); }
    private static void EnumValue<T>(T value, string path, List<EmissionError> errors, int? row) where T : struct, Enum { if (!Enum.IsDefined(value)) errors.Add(Error(path, "Invalid serialized enum value.", row)); }
    private static IEnumerable<(string, string?)> Strings(WeaponVariantDef v, WeaponDef d) { yield return ("variant.internalName", v.InternalName); yield return ("variant.displayName", v.DisplayName); yield return ("variant.alternateWeaponName", v.AlternateWeaponName); foreach ((string? value, int i) in v.AnimationNames.Select((value, i) => (value, i))) yield return ($"variant.animationNames[{i}]", value); yield return ("definition.internalName", d.InternalName); yield return ("definition.modeName", d.ModeName); foreach ((string? value, int i) in d.RightHandAnimationNames.Select((value, i) => (value, i))) yield return ($"definition.rightHandAnimationNames[{i}]", value); foreach ((string? value, int i) in d.LeftHandAnimationNames.Select((value, i) => (value, i))) yield return ($"definition.leftHandAnimationNames[{i}]", value); foreach ((string? value, int i) in d.SoundAliasNames.Select((value, i) => (value, i))) yield return ($"definition.soundAliasNames[{i}]", value); foreach ((string? value, int i) in d.BounceSoundNames.Select((value, i) => (value, i))) yield return ($"definition.bounceSoundNames[{i}]", value); yield return ("definition.ammo.ammoName", d.Ammo.AmmoName); yield return ("definition.ammo.clipName", d.Ammo.ClipName); yield return ("definition.ammo.sharedAmmoCapName", d.Ammo.SharedAmmoCapName); yield return ("definition.projectile.explosionSound", d.Projectile.ExplosionSound); yield return ("definition.projectile.dudSound", d.Projectile.DudSound); yield return ("definition.projectile.ignitionSound", d.Projectile.IgnitionSound); yield return ("definition.accuracy.graphName0", d.Accuracy.GraphName0); yield return ("definition.accuracy.graphName1", d.Accuracy.GraphName1); yield return ("definition.hints.use", d.Hints.UseHintString); yield return ("definition.hints.drop", d.Hints.DropHintString); yield return ("definition.scriptName", d.ScriptName); yield return ("definition.rumble.fire", d.Rumble.FireRumble); yield return ("definition.rumble.melee", d.Rumble.MeleeImpactRumble); yield return ("definition.turret.overheat", d.Turret.OverheatSound); yield return ("definition.turret.rumble", d.Turret.BarrelSpinRumble); yield return ("definition.turret.max", d.Turret.BarrelSpinMaxSound); foreach ((string? value, int i) in d.Turret.BarrelSpinUpSoundNames.Select((value, i) => (value, i))) yield return ($"definition.turret.up[{i}]", value); foreach ((string? value, int i) in d.Turret.BarrelSpinDownSoundNames.Select((value, i) => (value, i))) yield return ($"definition.turret.down[{i}]", value); yield return ("definition.missile.alias", d.MissileConeSound.Alias); yield return ("definition.missile.base", d.MissileConeSound.AliasAtBase); }
    private static IEnumerable<(string, float)> Floats(WeaponVariantDef v, WeaponDef d) => new[] { ("variant.adsZoomFov", v.AdsZoomFov), ("variant.penetrateMultiplier", v.PenetrateMultiplier), ("variant.adsViewKickCenterSpeed", v.AdsViewKickCenterSpeed), ("variant.hipViewKickCenterSpeed", v.HipViewKickCenterSpeed), ("variant.adsDofStart", v.AdsDofStart), ("variant.adsDofEnd", v.AdsDofEnd) }.Concat(d.Projectile.ParallelBounce.Select((value, i) => ($"definition.projectile.parallelBounce[{i}]", value))).Concat(d.Projectile.PerpendicularBounce.Select((value, i) => ($"definition.projectile.perpendicularBounce[{i}]", value))).Concat(d.LocationDamageMultipliers.Select((value, i) => ($"definition.locationDamageMultipliers[{i}]", value)));
    private static EmissionError Error(string path, string message, int? row) => new(path, message, row, XAssetType.Weapon);
    private static EmissionAddress Offset(EmissionAddress owner, int byteOffset) => new(owner.Block, checked(owner.Offset + byteOffset));
    private sealed record ExternalPlan(EmissionBlockSegment? Root, IReadOnlyList<EmissionBlockSegment> Source, int PointerRaw = -1);
    private sealed record SoundCellPlan(EmissionBlockSegment? Cell, IReadOnlyList<EmissionBlockSegment> Source, int PointerRaw);
    private sealed record DirectRegionPlan(EmissionBlockSegment? Root, IReadOnlyList<EmissionBlockSegment> Source, int PointerRaw);
    private sealed record PointerTablePlan(EmissionBlockSegment? Table, IReadOnlyList<EmissionBlockSegment> Source, int PointerRaw);
    private sealed record DefinitionPlan(EmissionBlockSegment? Root, IReadOnlyList<EmissionBlockSegment> Source, int PointerRaw);
}
