using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.FastFiles.Emitters.Linking;

/// <summary>
/// Explicit, type-owned dependency discovery. Collectors enumerate only
/// semantic fields that their corresponding body emitters consume as nested
/// XAsset references; this registry never walks arbitrary object graphs.
/// </summary>
public sealed class ZoneAssetDependencyCollectorRegistry
{
    private delegate IReadOnlyList<ZoneAssetDependency> Collector(
        IXAssetBuildData buildData);

    private readonly Dictionary<XAssetType, Collector> _collectors = [];

    public static ZoneAssetDependencyCollectorRegistry Default { get; } =
        CreateDefault();

    /// <summary>Registers the dependency collectors used by the linker.</summary>
    public static ZoneAssetDependencyCollectorRegistry CreateDefault()
    {
        var registry = new ZoneAssetDependencyCollectorRegistry();
        registry.RegisterNone(XAssetType.RawFile);
        registry.RegisterNone(XAssetType.Localize);
        registry.RegisterNone(XAssetType.StringTable);
        registry.RegisterNone(XAssetType.StructuredDataDef);
        registry.RegisterNone(XAssetType.XAnim);
        registry.RegisterNone(XAssetType.ComMap);
        registry.RegisterNone(XAssetType.GameMapMp);
        registry.RegisterNone(XAssetType.MapEnts);
        registry.Register(XAssetType.XModel, CollectXModel);
        registry.Register(XAssetType.Material, CollectMaterial);
        registry.Register(XAssetType.Sound, CollectSound);
        registry.Register(XAssetType.ColMapSp, CollectClipMap);
        registry.Register(XAssetType.ColMapMp, CollectClipMap);
        registry.Register(XAssetType.FxMap, CollectFxWorld);
        registry.Register(XAssetType.GfxMap, CollectGfxWorld);
        registry.Register(XAssetType.LightDef, CollectLightDef);
        registry.Register(XAssetType.Fx, CollectFxEffect);
        registry.Register(XAssetType.ImpactFx, CollectImpactFx);
        registry.Register(XAssetType.Menu, CollectMenu);
        registry.Register(XAssetType.MenuFile, CollectMenuFile);
        registry.Register(XAssetType.Techset, CollectTechniqueSet);
        registry.Register(XAssetType.Weapon, CollectWeapon);
        return registry;
    }

    /// <summary>
    /// Collects a registered type. Unregistered types return false rather than
    /// being treated as dependency-free.
    /// </summary>
    public bool TryCollect(
        IXAssetBuildData buildData,
        out IReadOnlyList<ZoneAssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        if (!_collectors.TryGetValue(buildData.AssetType, out Collector? collector))
        {
            dependencies = [];
            return false;
        }

        dependencies = Normalize(collector(buildData));
        return true;
    }

    public IReadOnlyList<ZoneAssetDependency> RequireCollect(
        IXAssetBuildData buildData)
    {
        if (!TryCollect(buildData, out IReadOnlyList<ZoneAssetDependency>? dependencies))
        {
            throw new InvalidDataException(
                $"No explicit nested-XAsset dependency collector is registered for '{buildData.AssetType}'.");
        }
        return dependencies;
    }

    internal IReadOnlyList<ZoneAssetDependency> CollectKnown(
        IXAssetBuildData buildData) =>
        TryCollect(buildData, out IReadOnlyList<ZoneAssetDependency>? dependencies)
            ? dependencies
            : [];

    private void RegisterNone(XAssetType assetType) =>
        Register(assetType, _ => []);

    private void Register(XAssetType assetType, Collector collector)
    {
        if (!Enum.IsDefined(assetType))
            throw new ArgumentOutOfRangeException(nameof(assetType));
        ArgumentNullException.ThrowIfNull(collector);
        if (!_collectors.TryAdd(assetType, collector))
        {
            throw new InvalidOperationException(
                $"A dependency collector is already registered for '{assetType}'.");
        }
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectXModel(
        IXAssetBuildData buildData)
    {
        if (buildData is not IXModelBuildData model)
            throw WrongBuildData(buildData, nameof(IXModelBuildData));

        var result = new List<ZoneAssetDependency>();
        Add(
            result,
            model.MaterialReferences,
            XAssetType.Material,
            "materialReferences");
        Add(
            result,
            model.PhysPresetReference,
            XAssetType.PhysPreset,
            "physPreset");
        Add(
            result,
            model.PhysCollmapReference,
            XAssetType.PhysCollmap,
            "physCollmap");
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectMaterial(
        IXAssetBuildData buildData)
    {
        if (buildData is not IMaterialBuildData material)
            throw WrongBuildData(buildData, nameof(IMaterialBuildData));

        var result = new List<ZoneAssetDependency>();
        Add(
            result,
            material.TechniqueSetReference,
            XAssetType.Techset,
            "techniqueSet");

        IReadOnlyList<MaterialTextureBuildData> textures = material.Textures ??
            throw new InvalidDataException(
                "Material dependency discovery requires a non-null texture table.");
        for (int index = 0; index < textures.Count; index++)
        {
            MaterialTextureBuildData texture = textures[index] ??
                throw new InvalidDataException(
                    $"Material dependency discovery found a null texture at textures[{index}].");
            if (texture.Water is { } water)
            {
                Add(
                    result,
                    water.ImageReference,
                    XAssetType.Image,
                    $"textures[{index}].water.image");
            }
            else
            {
                Add(
                    result,
                    texture.ImageReference,
                    XAssetType.Image,
                    $"textures[{index}].image");
            }
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectSound(
        IXAssetBuildData buildData)
    {
        if (buildData is not ISoundAliasListBuildData sound)
            throw WrongBuildData(buildData, nameof(ISoundAliasListBuildData));

        var result = new List<ZoneAssetDependency>();
        IReadOnlyList<SoundAliasBuildData> aliases = sound.Aliases ??
            throw new InvalidDataException(
                "Sound dependency discovery requires a non-null alias table.");
        for (int index = 0; index < aliases.Count; index++)
        {
            SoundAliasBuildData alias = aliases[index] ??
                throw new InvalidDataException(
                    $"Sound dependency discovery found a null alias at aliases[{index}].");
            if (alias.SoundFile is
                {
                    Kind: SndAliasTypeBuildKind.Loaded,
                    LoadedSoundReference: { } loaded
                })
            {
                Add(
                    result,
                    loaded,
                    XAssetType.LoadedSound,
                    $"aliases[{index}].soundFile.loadedSound");
            }
            Add(
                result,
                alias.VolumeFalloffCurveReference,
                XAssetType.SndCurve,
                $"aliases[{index}].volumeFalloffCurve");
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectClipMap(
        IXAssetBuildData buildData)
    {
        if (buildData is not IClipMapBuildData clipMap)
            throw WrongBuildData(buildData, nameof(IClipMapBuildData));

        ClipMapReferenceBuildData references = clipMap.References ??
            throw new InvalidDataException(
                "ClipMap dependency discovery requires a non-null references table.");
        var result = new List<ZoneAssetDependency>();
        Add(
            result,
            references.StaticModels,
            XAssetType.XModel,
            "references.staticModels");

        IReadOnlyList<IReadOnlyList<ClipMapDynEntityReferenceBuildData>>
            dynamicEntities = references.DynamicEntities ??
                throw new InvalidDataException(
                    "ClipMap dependency discovery requires a non-null dynamic-entity table.");
        for (int listIndex = 0; listIndex < dynamicEntities.Count; listIndex++)
        {
            IReadOnlyList<ClipMapDynEntityReferenceBuildData> list =
                dynamicEntities[listIndex] ??
                throw new InvalidDataException(
                    $"ClipMap dependency discovery found a null list at references.dynamicEntities[{listIndex}].");
            for (int index = 0; index < list.Count; index++)
            {
                ClipMapDynEntityReferenceBuildData entity = list[index] ??
                    throw new InvalidDataException(
                        $"ClipMap dependency discovery found a null entry at references.dynamicEntities[{listIndex}][{index}].");
                string path =
                    $"references.dynamicEntities[{listIndex}][{index}]";
                Add(result, entity.XModel, XAssetType.XModel, $"{path}.xmodel");
                Add(result, entity.DestroyFx, XAssetType.Fx, $"{path}.destroyFx");
                Add(
                    result,
                    entity.PhysPreset,
                    XAssetType.PhysPreset,
                    $"{path}.physPreset");
            }
        }
        Add(result, references.MapEnts, XAssetType.MapEnts, "references.mapEnts");
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectFxWorld(
        IXAssetBuildData buildData)
    {
        if (buildData is not IFxWorldBuildData fxWorld)
            throw WrongBuildData(buildData, nameof(IFxWorldBuildData));

        IReadOnlyList<FxGlassDefReferenceBuildData> references =
            fxWorld.DefinitionReferences ??
            throw new InvalidDataException(
                "FxMap dependency discovery requires a non-null definition-reference table.");
        var result = new List<ZoneAssetDependency>();
        for (int index = 0; index < references.Count; index++)
        {
            FxGlassDefReferenceBuildData value = references[index] ??
                throw new InvalidDataException(
                    $"FxMap dependency discovery found a null entry at definitionReferences[{index}].");
            string path = $"definitionReferences[{index}]";
            Add(result, value.Material, XAssetType.Material, $"{path}.material");
            Add(
                result,
                value.ShatteredMaterial,
                XAssetType.Material,
                $"{path}.shatteredMaterial");
            Add(
                result,
                value.PhysPreset,
                XAssetType.PhysPreset,
                $"{path}.physPreset");
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectGfxWorld(
        IXAssetBuildData buildData)
    {
        if (buildData is not IGfxWorldBuildData gfxWorld)
            throw WrongBuildData(buildData, nameof(IGfxWorldBuildData));

        GfxWorldReferenceBuildData references = gfxWorld.References ??
            throw new InvalidDataException(
                "GfxMap dependency discovery requires a non-null references table.");
        var result = new List<ZoneAssetDependency>();
        Add(result, references.SkyImages, XAssetType.Image, "references.skyImages");
        AddExcludingInlineImageDefinitions(
            result,
            references.ReflectionProbeImages,
            references.ReflectionProbeImageDefinitions,
            "references.reflectionProbeImages");
        IReadOnlyList<GfxLightmapReferenceBuildData> lightmaps =
            references.Lightmaps ??
            throw new InvalidDataException(
                "GfxMap dependency discovery requires a non-null lightmap-reference table.");
        for (int index = 0; index < lightmaps.Count; index++)
        {
            GfxLightmapReferenceBuildData lightmap = lightmaps[index] ??
                throw new InvalidDataException(
                    $"GfxMap dependency discovery found a null entry at references.lightmaps[{index}].");
            Add(
                result,
                lightmap.Primary,
                XAssetType.Image,
                $"references.lightmaps[{index}].primary");
            Add(
                result,
                lightmap.Secondary,
                XAssetType.Image,
                $"references.lightmaps[{index}].secondary");
        }
        Add(
            result,
            references.LightmapOverridePrimary,
            XAssetType.Image,
            "references.lightmapOverridePrimary");
        Add(
            result,
            references.LightmapOverrideSecondary,
            XAssetType.Image,
            "references.lightmapOverrideSecondary");
        Add(
            result,
            references.MaterialMemory,
            XAssetType.Material,
            "references.materialMemory");
        Add(
            result,
            references.SunSpriteMaterial,
            XAssetType.Material,
            "references.sunSpriteMaterial");
        Add(
            result,
            references.SunFlareMaterial,
            XAssetType.Material,
            "references.sunFlareMaterial");
        Add(
            result,
            references.OutdoorImage,
            XAssetType.Image,
            "references.outdoorImage");
        Add(
            result,
            references.SurfaceMaterials,
            XAssetType.Material,
            "references.surfaceMaterials");
        Add(
            result,
            references.StaticModelDrawInsts,
            XAssetType.XModel,
            "references.staticModelDrawInsts");
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectLightDef(
        IXAssetBuildData buildData)
    {
        if (buildData is not ILightDefBuildData lightDef)
            throw WrongBuildData(buildData, nameof(ILightDefBuildData));

        var result = new List<ZoneAssetDependency>();
        // A retained nested link owns its inline/insert/alias provenance and
        // is emitted by NestedXAssetEmission. Only the external-reference
        // arm participates in the top-level dependency graph.
        if (lightDef.ImageLink is null)
            Add(result, lightDef.ImageReference, XAssetType.Image, "image");
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectImpactFx(
        IXAssetBuildData buildData)
    {
        if (buildData is not IFxImpactTableBuildData impactFx)
            throw WrongBuildData(buildData, nameof(IFxImpactTableBuildData));

        IReadOnlyList<FxImpactEntryBuildData> entries = impactFx.Entries ??
            throw new InvalidDataException(
                "ImpactFx dependency discovery requires a non-null entry table.");
        var result = new List<ZoneAssetDependency>();
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            FxImpactEntryBuildData entry = entries[entryIndex] ??
                throw new InvalidDataException(
                    $"ImpactFx dependency discovery found a null entry at entries[{entryIndex}].");
            Add(
                result,
                entry.SurfaceEffects,
                XAssetType.Fx,
                $"entries[{entryIndex}].surfaceEffects");
            Add(
                result,
                entry.FleshEffects,
                XAssetType.Fx,
                $"entries[{entryIndex}].fleshEffects");
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectFxEffect(
        IXAssetBuildData buildData)
    {
        const byte decalElementType = 11;
        if (buildData is not IFxEffectDefBuildData effect)
            throw WrongBuildData(buildData, nameof(IFxEffectDefBuildData));

        IReadOnlyList<FxElementBuildData> elements = effect.Elements ??
            throw new InvalidDataException(
                "Fx dependency discovery requires a non-null element table.");
        var result = new List<ZoneAssetDependency>();
        for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            FxElementBuildData element = elements[elementIndex] ??
                throw new InvalidDataException(
                    $"Fx dependency discovery found a null element at elements[{elementIndex}].");
            if (element.ElemType != decalElementType)
            {
                for (int visualIndex = 0;
                     visualIndex < element.Visuals.Count;
                     visualIndex++)
                {
                    FxVisualBuildData visual = element.Visuals[visualIndex] ??
                        throw new InvalidDataException(
                            $"Fx dependency discovery found a null visual at elements[{elementIndex}].visuals[{visualIndex}].");
                    string path =
                        $"elements[{elementIndex}].visuals[{visualIndex}]";
                    if (visual.Kind == FxVisualBuildKind.Material)
                    {
                        Add(
                            result,
                            visual.MaterialReference,
                            XAssetType.Material,
                            $"{path}.material");
                    }
                    else if (visual.Kind == FxVisualBuildKind.Model)
                    {
                        Add(
                            result,
                            visual.ModelReference,
                            XAssetType.XModel,
                            $"{path}.model");
                    }
                }
                continue;
            }

            for (int visualIndex = 0;
                 visualIndex < element.MarkVisuals.Count;
                 visualIndex++)
            {
                FxMarkVisualBuildData visual = element.MarkVisuals[visualIndex] ??
                    throw new InvalidDataException(
                        $"Fx dependency discovery found a null mark visual at elements[{elementIndex}].markVisuals[{visualIndex}].");
                string path =
                    $"elements[{elementIndex}].markVisuals[{visualIndex}]";
                Add(
                    result,
                    visual.Material0Reference,
                    XAssetType.Material,
                    $"{path}.material0");
                Add(
                    result,
                    visual.Material1Reference,
                    XAssetType.Material,
                    $"{path}.material1");
            }
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectWeapon(
        IXAssetBuildData buildData)
    {
        if (buildData is not IWeaponBuildData weapon)
            throw WrongBuildData(buildData, nameof(IWeaponBuildData));

        WeaponReferenceBuildData references = weapon.References ??
            throw new InvalidDataException("Weapon dependency discovery requires a non-null references table.");
        var result = new List<ZoneAssetDependency>();
        Add(result, references.KillIcon, XAssetType.Material, "references.killIcon");
        Add(result, references.DpadIcon, XAssetType.Material, "references.dpadIcon");
        Add(result, references.GunModels, XAssetType.XModel, "references.gunModels");
        Add(result, references.HandModel, XAssetType.XModel, "references.handModel");
        Add(result, references.FlashEffects, XAssetType.Fx, "references.flashEffects");
        Add(result, references.Materials, XAssetType.Material, "references.materials");
        Add(result, references.Effects, XAssetType.Fx, "references.effects");
        Add(result, references.WorldGunModels, XAssetType.XModel, "references.worldGunModels");
        Add(result, references.WorldModels, XAssetType.XModel, "references.worldModels");
        Add(result, references.IconMaterials, XAssetType.Material, "references.iconMaterials");
        Add(result, references.OverlayMaterials, XAssetType.Material, "references.overlayMaterials");
        Add(result, references.PhysCollmap, XAssetType.PhysCollmap, "references.physCollmap");
        Add(result, references.ProjectileModel, XAssetType.XModel, "references.projectileModel");
        Add(result, references.ProjectileEffects, XAssetType.Fx, "references.projectileEffects");
        Add(result, references.ImpactEffects, XAssetType.Fx, "references.impactEffects");
        Add(result, references.IgnitionEffect, XAssetType.Fx, "references.ignitionEffect");
        Add(result, references.Tracer, XAssetType.Tracer, "references.tracer");
        Add(result, references.TurretOverheatEffect, XAssetType.Fx, "references.turretOverheatEffect");
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectTechniqueSet(
        IXAssetBuildData buildData)
    {
        if (buildData is not ITechniqueSetBuildData techniqueSet)
            throw WrongBuildData(buildData, nameof(ITechniqueSetBuildData));

        var result = new List<ZoneAssetDependency>();
        IReadOnlyList<TechniqueBuildData?> slots = techniqueSet.TechniqueSlots ??
            throw new InvalidDataException("TechniqueSet dependency discovery requires a non-null slot table.");
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            TechniqueBuildData? technique = slots[slotIndex];
            if (technique is null)
                continue;
            for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
            {
                TechniquePassBuildData pass = technique.Passes[passIndex];
                string path = $"techniqueSlots[{slotIndex}].passes[{passIndex}]";
                Add(result, pass.VertexShader, XAssetType.VertexShader, $"{path}.vertexShader");
                Add(result, pass.PixelShader, XAssetType.PixelShader, $"{path}.pixelShader");
            }
        }
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectMenu(
        IXAssetBuildData buildData)
    {
        if (buildData is not IMenuBuildData menu)
            throw WrongBuildData(buildData, nameof(IMenuBuildData));

        var result = new List<ZoneAssetDependency>();
        CollectMenuDefinition(
            menu,
            "definition",
            result);
        return result;
    }

    private static IReadOnlyList<ZoneAssetDependency> CollectMenuFile(
        IXAssetBuildData buildData)
    {
        if (buildData is not IMenuFileBuildData menuFile)
            throw WrongBuildData(buildData, nameof(IMenuFileBuildData));

        var result = new List<ZoneAssetDependency>();
        IReadOnlyList<IMenuBuildData> menus = menuFile.Menus ??
            throw new InvalidDataException("MenuFile dependency discovery requires a non-null menu table.");
        for (int menuIndex = 0; menuIndex < menus.Count; menuIndex++)
        {
            IMenuBuildData menu = menus[menuIndex] ??
                throw new InvalidDataException(
                    $"MenuFile dependency discovery found a null menu at menus[{menuIndex}].");
            CollectMenuDefinition(
                menu,
                $"menus[{menuIndex}].definition",
                result);
        }
        return result;
    }

    private static void CollectMenuDefinition(
        IMenuBuildData menu,
        string path,
        List<ZoneAssetDependency> result)
    {
        MenuDefAsset definition = menu.Definition ??
            throw new InvalidDataException(
                $"Menu dependency discovery requires a non-null definition at {path}.");
        string? background = definition.Window.BackgroundMaterialName ??
            menu.References?.WindowBackgroundMaterial?.OriginalSerializedName;
        Add(result, background, XAssetType.Material, $"{path}.window.background");

        for (int itemIndex = 0; itemIndex < definition.Items.Count; itemIndex++)
        {
            ItemDefAsset? item = definition.Items[itemIndex].Item;
            if (item is null)
                continue;
            string itemPath = $"{path}.items[{itemIndex}]";
            Add(
                result,
                item.Window.BackgroundMaterialName,
                XAssetType.Material,
                $"{itemPath}.window.background");
            Add(
                result,
                item.FocusSoundName,
                XAssetType.Sound,
                $"{itemPath}.focusSound");
            if (item.TypeData.Value is ListBoxItemDefData && item.ListBox is { } listBox)
            {
                Add(
                    result,
                    listBox.SelectIconMaterialName,
                    XAssetType.Material,
                    $"{itemPath}.listBox.selectIcon");
            }
        }
    }

    private static void Add(
        List<ZoneAssetDependency> destination,
        IReadOnlyList<SymbolicXAssetReference?> values,
        XAssetType expectedType,
        string path)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (int index = 0; index < values.Count; index++)
            Add(destination, values[index], expectedType, $"{path}[{index}]");
    }

    private static void AddExcludingInlineImageDefinitions(
        List<ZoneAssetDependency> destination,
        IReadOnlyList<SymbolicXAssetReference?> values,
        IReadOnlyList<IXAssetBuildData?> definitions,
        string path)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count is not 0 &&
            definitions.Count != values.Count)
        {
            throw new InvalidDataException(
                $"{path} inline definitions must be absent or parallel all " +
                "symbolic slots.");
        }

        for (int index = 0; index < values.Count; index++)
        {
            IXAssetBuildData? definition =
                definitions.Count == 0
                    ? null
                    : definitions[index];
            if (definition is not null)
            {
                SymbolicXAssetReference? reference = values[index];
                if (reference is null ||
                    reference.AssetType != XAssetType.Image ||
                    !reference.IsExternalReference ||
                    definition is not
                        IGfxImageBuildData imageDefinition ||
                    imageDefinition.Name is null ||
                    !string.Equals(
                        reference.OriginalSerializedName,
                        "," + imageDefinition.Name,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"{path}[{index}] inline ownership requires matching " +
                        "Image type and logical identity.");
                }

                continue;
            }

            Add(
                destination,
                values[index],
                XAssetType.Image,
                $"{path}[{index}]");
        }
    }

    private static void Add(
        List<ZoneAssetDependency> destination,
        SymbolicXAssetReference? reference,
        XAssetType expectedType,
        string path)
    {
        if (reference is null)
            return;
        if (reference.AssetType != expectedType)
        {
            throw new InvalidDataException(
                $"{path} declares '{reference.AssetType}' but the field contract requires '{expectedType}'.");
        }
        if (!reference.IsExternalReference)
        {
            throw new InvalidDataException(
                $"{path} must retain a comma-prefixed external {expectedType} identity.");
        }
        Add(destination, reference.OriginalSerializedName, expectedType, path);
    }

    private static void Add(
        List<ZoneAssetDependency> destination,
        string? serializedName,
        XAssetType expectedType,
        string path)
    {
        if (serializedName is null)
            return;
        if (serializedName.Length == 0 ||
            serializedName.IndexOf('\0') >= 0 ||
            serializedName.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{path} is not a non-empty Latin-1 XAsset identity.");
        }

        ZoneAssetKey target;
        try
        {
            target = serializedName.StartsWith(",", StringComparison.Ordinal)
                ? ZoneAssetKey.FromWireName(expectedType, serializedName)
                : new ZoneAssetKey(expectedType, serializedName);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"{path} contains an invalid {expectedType} identity: {exception.Message}",
                exception);
        }

        destination.Add(new ZoneAssetDependency(
            target,
            ZoneAssetDependencyKind.External,
            path));
    }

    private static IReadOnlyList<ZoneAssetDependency> Normalize(
        IEnumerable<ZoneAssetDependency> dependencies) =>
        Array.AsReadOnly(dependencies
            .Distinct()
            .OrderBy(value => value.Target.Type)
            .ThenBy(value => value.Target.LogicalName, StringComparer.Ordinal)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.OwnerPath, StringComparer.Ordinal)
            .ToArray());

    private static InvalidDataException WrongBuildData(
        IXAssetBuildData buildData,
        string expectedInterface) =>
        new(
            $"Dependency collector for '{buildData.AssetType}' requires {expectedInterface}; " +
            $"received '{buildData.GetType().FullName}'.");
}
