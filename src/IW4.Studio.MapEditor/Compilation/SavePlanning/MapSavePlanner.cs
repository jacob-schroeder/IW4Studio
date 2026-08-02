using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.SavePlanning;

public interface IMapSavePlanner
{
    MapSavePlan Plan(
        EditorMapDocument document,
        CompiledMapBundle baseline,
        long expectedDocumentRevision,
        long currentSourcePoolRevision,
        long currentEditingSessionRevision,
        string currentBaselineDigest,
        IEnumerable<MapPendingEdit> edits);
}

/// <summary>
/// Phase-0 fail-closed planner. It classifies the complete initial safety
/// matrix but performs no patching, rebuilding, draft replacement, or output.
/// </summary>
public sealed class MapSavePlanner : IMapSavePlanner
{
    private readonly IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding>
        _sourceBindings;
    private readonly IReadOnlySet<MapEditKind> _availablePatchers;

    /// <summary>
    /// Creates a planner without compiled binding authority. This preserves the
    /// Phase-0 construction surface, but every serialized edit will fail closed.
    /// Use the binding-catalog overload for an imported map session.
    /// </summary>
    public MapSavePlanner()
        : this([], [])
    {
    }

    public MapSavePlanner(
        IEnumerable<CompiledSourceBinding> sourceBindings,
        IEnumerable<MapEditKind>? availablePatchers = null)
    {
        ArgumentNullException.ThrowIfNull(sourceBindings);
        var byId = new Dictionary<SourceBindingId, CompiledSourceBinding>();
        foreach (CompiledSourceBinding binding in sourceBindings)
        {
            if (binding is null)
            {
                throw new ArgumentException(
                    "The compiled source-binding catalog cannot contain null entries.",
                    nameof(sourceBindings));
            }

            if (byId.TryGetValue(binding.Id, out CompiledSourceBinding? existing))
            {
                if (existing != binding)
                {
                    throw new InvalidDataException(
                        $"Compiled source binding {binding.Id} has conflicting catalog entries.");
                }

                continue;
            }

            byId.Add(binding.Id, binding);
        }

        _sourceBindings = byId;
        var patchers = new HashSet<MapEditKind>(availablePatchers ?? []);
        if (patchers.Any(kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(availablePatchers),
                "Available patcher capabilities must be defined map edit kinds.");
        }

        _availablePatchers = patchers;
    }

    public MapSavePlan Plan(
        EditorMapDocument document,
        CompiledMapBundle baseline,
        long expectedDocumentRevision,
        long currentSourcePoolRevision,
        long currentEditingSessionRevision,
        string currentBaselineDigest,
        IEnumerable<MapPendingEdit> edits)
        => PlanCore(
            document,
            baseline,
            expectedDocumentRevision,
            currentSourcePoolRevision,
            currentEditingSessionRevision,
            currentBaselineDigest,
            edits,
            composedSourceSnapshot: null,
            normalizations: null);

    /// <summary>
    /// Plans against a captured Studio snapshot that will be copied into the
    /// isolated candidate transaction. This explicitly authorizes revisions
    /// newer than map import without weakening the default stale-session
    /// rejection used by <see cref="Plan"/>.
    /// </summary>
    public MapSavePlan PlanComposed(
        EditorMapDocument document,
        CompiledMapBundle baseline,
        long expectedDocumentRevision,
        long currentSourcePoolRevision,
        FastFileEditingSaveSnapshot composedSourceSnapshot,
        string currentBaselineDigest,
        IEnumerable<MapPendingEdit> edits)
        => PlanComposed(
            document,
            baseline,
            expectedDocumentRevision,
            currentSourcePoolRevision,
            composedSourceSnapshot,
            currentBaselineDigest,
            edits,
            normalizations: null);

    internal MapSavePlan PlanComposed(
        EditorMapDocument document,
        CompiledMapBundle baseline,
        long expectedDocumentRevision,
        long currentSourcePoolRevision,
        FastFileEditingSaveSnapshot composedSourceSnapshot,
        string currentBaselineDigest,
        IEnumerable<MapPendingEdit> edits,
        IEnumerable<MapSavePlanNormalization>? normalizations)
    {
        ArgumentNullException.ThrowIfNull(composedSourceSnapshot);
        return PlanCore(
            document,
            baseline,
            expectedDocumentRevision,
            currentSourcePoolRevision,
            composedSourceSnapshot.Revision,
            currentBaselineDigest,
            edits,
            composedSourceSnapshot,
            normalizations);
    }

    private MapSavePlan PlanCore(
        EditorMapDocument document,
        CompiledMapBundle baseline,
        long expectedDocumentRevision,
        long currentSourcePoolRevision,
        long currentEditingSessionRevision,
        string currentBaselineDigest,
        IEnumerable<MapPendingEdit> edits,
        FastFileEditingSaveSnapshot? composedSourceSnapshot,
        IEnumerable<MapSavePlanNormalization>? normalizations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentBaselineDigest);
        ArgumentNullException.ThrowIfNull(edits);

        var blockers = new List<string>();
        if (document.Id != baseline.DocumentId ||
            !string.Equals(
                document.MapIdentity,
                baseline.MapIdentity,
                StringComparison.Ordinal))
        {
            blockers.Add(
                "The editor document does not belong to the selected compiled baseline.");
        }
        if (document.Revision != expectedDocumentRevision)
        {
            blockers.Add(
                $"Editor revision changed from {expectedDocumentRevision} to {document.Revision}; discard and replan.");
        }
        if (baseline.SourcePoolRevision != currentSourcePoolRevision)
        {
            blockers.Add(
                $"Asset-pool revision changed from {baseline.SourcePoolRevision} to {currentSourcePoolRevision}; discard and replan.");
        }
        bool composedRevisionIsAuthorized =
            composedSourceSnapshot is not null &&
            composedSourceSnapshot.DocumentId ==
                baseline.SourceDocumentId &&
            composedSourceSnapshot.Revision ==
                currentEditingSessionRevision &&
            composedSourceSnapshot.Revision >=
                baseline.SourceEditingSessionRevision;
        if (baseline.SourceEditingSessionRevision !=
                currentEditingSessionRevision &&
            !composedRevisionIsAuthorized)
        {
            blockers.Add(
                $"Editing-session revision changed from {baseline.SourceEditingSessionRevision} to {currentEditingSessionRevision}; discard and replan.");
        }
        if (composedSourceSnapshot is not null &&
            !composedRevisionIsAuthorized)
        {
            blockers.Add(
                "The composed Studio draft snapshot does not match the " +
                "compiled-map target document and captured session revision.");
        }
        if (!string.Equals(
                baseline.BaselineDigest,
                currentBaselineDigest,
                StringComparison.Ordinal))
        {
            blockers.Add(
                "The compiled baseline digest changed; discard and replan.");
        }

        HashSet<SourceBindingId> documentBindings =
            CollectDocumentBindings(document);
        MapSavePlanEntry[] entries = edits
            .Select(edit => new MapSavePlanEntry(
                edit,
                ValidateSourceBindings(
                    edit,
                    ClassifyCapability(edit),
                    documentBindings,
                    baseline,
                    document)))
            .ToArray();

        return new MapSavePlan(
            document.Revision,
            baseline.SourcePoolRevision,
            currentEditingSessionRevision,
            baseline.BaselineDigest,
            entries,
            blockers,
            normalizations);
    }

    private MapEditImpact ClassifyCapability(MapPendingEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return edit.Kind switch
        {
            MapEditKind.EditorOnly => Impact(
                MapSaveClassification.EditorOnly,
                [],
                MapDerivedSubsystem.None),

            MapEditKind.PrimaryLightColor => PatchableWhenProven(
                edit,
                [MapAssetKind.ComMap],
                "ComMap primary-light Color"),

            MapEditKind.PrimaryLightExponent => PatchableWhenProven(
                edit,
                [MapAssetKind.ComMap],
                "ComMap primary-light Exponent"),

            MapEditKind.PrimaryLightSpotFalloff => PatchableWhenProven(
                edit,
                [MapAssetKind.ComMap],
                "type-2 ComMap primary-light spot falloff"),

            MapEditKind.PrimaryLightInfluence => Impact(
                MapSaveClassification.PartialRebuildRequired,
                [MapAssetKind.ComMap, MapAssetKind.GfxMap],
                MapDerivedSubsystem.PrimaryLightMembershipAndRegions |
                MapDerivedSubsystem.SunAndLocalShadowData |
                MapDerivedSubsystem.BakedLighting,
                "Changing light origin, direction, radius, type, or shadow behavior requires unavailable light-region, visibility, shadow, and lighting rebuilders."),

            MapEditKind.EnvironmentValue => PatchableWhenProven(
                edit,
                [MapAssetKind.GfxMap, MapAssetKind.MapEnts],
                "environment value with one proven serialized or script owner"),

            MapEditKind.MapEntityKeyValue => PatchableWhenProven(
                edit,
                [MapAssetKind.MapEnts],
                "MapEnt key/value edit with byte-faithful serialization"),

            MapEditKind.MapEntityCardinality => PatchableWhenProven(
                edit,
                [MapAssetKind.MapEnts],
                "executable-proven final script_origin append/remove"),

            MapEditKind.StaticModelVisibility => PatchableWhenProven(
                edit,
                [
                    MapAssetKind.GfxMap,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                "atomic render/collision static-model suppression"),

            MapEditKind.StaticModelTransform
                when edit.SourceBindings.Count == 5 =>
                PatchableWhenProven(
                    edit,
                    [
                        MapAssetKind.GfxMap,
                        MapAssetKind.ColMapMp,
                        MapAssetKind.ColMapSp
                    ],
                    "proof-gated atomic render/collision static-model translation"),

            MapEditKind.StaticModelTransform =>
                MapEditImpactTaxonomy.StaticModelTransform(),

            MapEditKind.StaticModelCardinality
                when edit.SourceBindings.Count is 2 or 3 =>
                PatchableWhenProven(
                    edit,
                    [
                        MapAssetKind.GfxMap,
                        MapAssetKind.ColMapMp,
                        MapAssetKind.ColMapSp
                    ],
                    "proof-gated exact-pair static-model removal"),

            MapEditKind.StaticModelCardinality => Impact(
                MapSaveClassification.PartialRebuildRequired,
                [
                    MapAssetKind.GfxMap,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                MapDerivedSubsystem.Collision |
                MapDerivedSubsystem.StaticModelBoundsAndSpatialMembership |
                MapDerivedSubsystem.CellsPortalsAndAabb |
                MapDerivedSubsystem.Dpvs |
                MapDerivedSubsystem.BakedLighting |
                MapDerivedSubsystem.DependenciesSidecarsAndChecksums,
                "Adding or removing a static model requires unavailable collection, collision, AABB/DPVS, lighting, and dependency rebuilders."),

            MapEditKind.StaticModelDuplication =>
                PatchableWhenProven(
                    edit,
                    [
                        MapAssetKind.GfxMap,
                        MapAssetKind.ColMapMp,
                        MapAssetKind.ColMapSp
                    ],
                    "proof-gated exact-pair static-model duplication"),

            MapEditKind.CollisionGeometry =>
                MapEditImpactTaxonomy.AuthoredCollisionGeometry(),

            MapEditKind.CollisionCardinality =>
                MapEditImpactTaxonomy.AuthoredCollisionCardinality(),

            MapEditKind.MaterialReplacement => PatchableWhenProven(
                edit,
                [MapAssetKind.GfxMap],
                "material replacement with proven vertex/pass compatibility and dependency closure"),

            MapEditKind.FxGlassDefinitionHalfThickness =>
                PatchableWhenProven(
                    edit,
                    [MapAssetKind.FxMap],
                    "existing FxGlassDef HalfThickness scalar"),

            MapEditKind.FxGlassDefinitionColor =>
                PatchableWhenProven(
                    edit,
                    [MapAssetKind.FxMap],
                    "existing FxGlassDef packed Color scalar"),

            MapEditKind.GlassProperty => PatchableWhenProven(
                edit,
                [
                    MapAssetKind.FxMap,
                    MapAssetKind.GameMapMp,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                "glass property edit with every Fx/Game/Col link proven"),

            MapEditKind.GlassCardinality => Impact(
                MapSaveClassification.PartialRebuildRequired,
                [
                    MapAssetKind.FxMap,
                    MapAssetKind.GameMapMp,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                MapDerivedSubsystem.Collision |
                MapDerivedSubsystem.GlassCrossLinks |
                MapDerivedSubsystem.CellsPortalsAndAabb |
                MapDerivedSubsystem.Dpvs,
                "Adding or removing glass requires unavailable index, collision, spatial, visibility, and cross-asset rebuilders."),

            MapEditKind.WorldTopology => Impact(
                MapSaveClassification.FullRebuildRequired,
                [
                    MapAssetKind.GfxMap,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                MapDerivedSubsystem.RenderGeometry |
                MapDerivedSubsystem.SurfaceSorting |
                MapDerivedSubsystem.Collision |
                MapDerivedSubsystem.CellsPortalsAndAabb |
                MapDerivedSubsystem.Dpvs |
                MapDerivedSubsystem.BakedLighting |
                MapDerivedSubsystem.DependenciesSidecarsAndChecksums,
                "World geometry or material topology changes require a coordinated render, collision, spatial, DPVS, and lighting compiler."),

            MapEditKind.BrushCellOrPortalTopology => Impact(
                MapSaveClassification.FullRebuildRequired,
                [
                    MapAssetKind.GfxMap,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.ColMapSp
                ],
                MapDerivedSubsystem.RenderGeometry |
                MapDerivedSubsystem.Collision |
                MapDerivedSubsystem.CellsPortalsAndAabb |
                MapDerivedSubsystem.Dpvs,
                "Brush, cell, or portal creation/deletion requires a full compiled-map topology compiler."),

            MapEditKind.BakedLighting => Impact(
                MapSaveClassification.FullRebuildRequired,
                [MapAssetKind.GfxMap],
                MapDerivedSubsystem.SunAndLocalShadowData |
                MapDerivedSubsystem.BakedLighting,
                "Lightmap, light-grid, probe, or baked-shadow edits require a lighting compiler."),

            MapEditKind.Unknown => Impact(
                MapSaveClassification.Unsupported,
                [],
                MapDerivedSubsystem.None,
                "Unknown map commands are unsupported until an explicit save-impact rule exists."),

            _ => Impact(
                MapSaveClassification.Unsupported,
                [],
                MapDerivedSubsystem.None,
                $"Map edit kind '{edit.Kind}' has no save-impact rule.")
        };
    }

    private MapEditImpact PatchableWhenProven(
        MapPendingEdit edit,
        IEnumerable<MapAssetKind> affectedAssets,
        string capability)
    {
        var missing = new List<string>();
        if (!edit.PreservationCoverageProven)
            missing.Add("preservation-coverage evidence");
        if (!edit.HasRequiredBuilder)
            missing.Add("validated patcher/rebuilder");
        if (!_availablePatchers.Contains(edit.Kind))
            missing.Add("registered planner patcher capability");
        if (edit.Kind is not (
                MapEditKind.PrimaryLightColor or
                MapEditKind.PrimaryLightExponent or
                MapEditKind.PrimaryLightSpotFalloff or
                MapEditKind.MapEntityKeyValue or
                MapEditKind.MapEntityCardinality or
                MapEditKind.StaticModelVisibility or
                MapEditKind.StaticModelTransform or
                MapEditKind.StaticModelCardinality or
                MapEditKind.StaticModelDuplication or
                MapEditKind.FxGlassDefinitionHalfThickness or
                MapEditKind.FxGlassDefinitionColor))
        {
            missing.Add(
                "object-specific binding-set and invariant validation");
        }
        return missing.Count == 0
            ? Impact(
                MapSaveClassification.PatchSaveable,
                affectedAssets,
                MapDerivedSubsystem.None)
            : Impact(
                MapSaveClassification.Unsupported,
                affectedAssets,
                MapDerivedSubsystem.None,
                $"{capability} is not saveable because it lacks {string.Join(", ", missing)}.");
    }

    private MapEditImpact ValidateSourceBindings(
        MapPendingEdit edit,
        MapEditImpact impact,
        IReadOnlySet<SourceBindingId> documentBindings,
        CompiledMapBundle baseline,
        EditorMapDocument document)
    {
        if (edit.Kind == MapEditKind.EditorOnly)
        {
            return edit.SourceBindings.Count == 0
                ? impact
                : Impact(
                    MapSaveClassification.Unsupported,
                    impact.AffectedAssets,
                    impact.InvalidatedSubsystems,
                    "Editor-only commands cannot carry compiled source bindings.");
        }
        if (edit.Kind is
            MapEditKind.CollisionGeometry or
            MapEditKind.CollisionCardinality)
        {
            return edit.SourceBindings.Count == 0
                ? impact
                : Impact(
                    MapSaveClassification.Unsupported,
                    impact.AffectedAssets,
                    impact.InvalidatedSubsystems,
                    "Authored collision may carry only editor-local " +
                    "provenance; it cannot claim imported compiled-source " +
                    "bindings.");
        }

        var errors = new List<string>();
        bool hasPrimaryLightColorBinding = false;
        bool hasPrimaryLightExponentBinding = false;
        bool hasPrimaryLightSpotFalloffBinding = false;
        bool hasFxGlassDefinitionHalfThicknessBinding = false;
        bool hasFxGlassDefinitionColorBinding = false;
        bool hasMapEntPropertyBinding = false;
        bool hasMapEntEntityStringBinding = false;
        var staticModelSuppressionBindings =
            new List<CompiledSourceBinding>();
        var staticModelTranslationBindings =
            new List<CompiledSourceBinding>();
        var staticModelRemovalBindings =
            new List<CompiledSourceBinding>();
        var staticModelDuplicationBindings =
            new List<CompiledSourceBinding>();
        if (edit.SourceBindings.Count == 0)
        {
            errors.Add("no concrete source binding IDs were supplied");
        }

        foreach (SourceBindingId id in edit.SourceBindings)
        {
            if (!documentBindings.Contains(id))
            {
                errors.Add(
                    $"source binding {id} is not referenced by the editor document");
                continue;
            }
            if (!_sourceBindings.TryGetValue(id, out CompiledSourceBinding? binding))
            {
                errors.Add(
                    $"source binding {id} is absent from the imported compiled-binding catalog");
                continue;
            }
            if (!TryValidateAgainstBundle(
                    binding,
                    baseline,
                    out MapAssetKind assetKind,
                    out string? error))
            {
                errors.Add(error!);
                continue;
            }
            if (binding.Provenance is not (
                    MapValueProvenance.ExactSerialized or
                    MapValueProvenance.ExactDecodedRuntime))
            {
                errors.Add(
                    $"source binding {id} has non-authoritative {binding.Provenance} provenance");
                continue;
            }
            if (impact.AffectedAssets.Count != 0 &&
                !impact.AffectedAssets.Contains(assetKind))
            {
                errors.Add(
                    $"source binding {id} belongs to {assetKind}, which is not an affected asset for {edit.Kind}");
                continue;
            }

            if (edit.Kind == MapEditKind.PrimaryLightColor)
            {
                if (assetKind != MapAssetKind.ComMap ||
                    !TryParsePrimaryLightColorPath(
                        binding.FieldPath,
                        out int pathOrdinal) ||
                    binding.SourceOrdinal != pathOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact primaryLights[index].color binding with matching source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.ComMap,
                        out IW4.Studio.Documents.ComWorldBuildData? comWorld) ||
                    comWorld is null ||
                    pathOrdinal >= comWorld.PrimaryLights.Count)
                {
                    errors.Add(
                        $"source binding {id} primary-light ordinal {pathOrdinal} is outside the owned ComMap baseline");
                    continue;
                }

                hasPrimaryLightColorBinding = true;
            }
            else if (edit.Kind == MapEditKind.PrimaryLightExponent)
            {
                if (assetKind != MapAssetKind.ComMap ||
                    !TryParsePrimaryLightExponentPath(
                        binding.FieldPath,
                        out int pathOrdinal) ||
                    binding.SourceOrdinal != pathOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact " +
                        "primaryLights[index].exponent binding with matching " +
                        "source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.ComMap,
                        out ComWorldBuildData? comWorld) ||
                    comWorld is null ||
                    pathOrdinal >= comWorld.PrimaryLights.Count)
                {
                    errors.Add(
                        $"source binding {id} primary-light ordinal " +
                        $"{pathOrdinal} is outside the owned ComMap baseline");
                    continue;
                }

                hasPrimaryLightExponentBinding = true;
            }
            else if (edit.Kind ==
                     MapEditKind.PrimaryLightSpotFalloff)
            {
                if (assetKind != MapAssetKind.ComMap ||
                    !TryParsePrimaryLightSpotFalloffPath(
                        binding.FieldPath,
                        out int pathOrdinal) ||
                    binding.SourceOrdinal != pathOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact " +
                        "primaryLights[index].cosHalfFovInner binding with " +
                        "matching source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.ComMap,
                        out ComWorldBuildData? comWorld) ||
                    comWorld is null ||
                    pathOrdinal >= comWorld.PrimaryLights.Count)
                {
                    errors.Add(
                        $"source binding {id} primary-light ordinal " +
                        $"{pathOrdinal} is outside the owned ComMap baseline");
                    continue;
                }

                ComPrimaryLightBuildData importedLight =
                    comWorld.PrimaryLights[pathOrdinal];
                EditorPrimaryLight[] currentMatches =
                    document.PrimaryLights
                        .Where(light =>
                            light.SourceOrdinal.Value == pathOrdinal)
                        .Take(2)
                        .ToArray();
                EditorPrimaryLight? currentLight =
                    currentMatches.Length == 1
                        ? currentMatches[0]
                        : null;
                if (currentLight is null ||
                    !IsValidSpotFalloff(
                        importedLight.Type,
                        importedLight.CosHalfFovOuter,
                        importedLight.CosHalfFovInner) ||
                    !IsValidSpotFalloff(
                        currentLight.LightType.Value,
                        currentLight.CosHalfFovOuter.Value,
                        currentLight.CosHalfFovInner.Value))
                {
                    errors.Add(
                        $"source binding {id} does not target imported and " +
                        "current type-2 spotlight state satisfying " +
                        "0 < outer < inner <= 1");
                    continue;
                }

                hasPrimaryLightSpotFalloffBinding = true;
            }
            else if (edit.Kind ==
                     MapEditKind.FxGlassDefinitionHalfThickness)
            {
                if (assetKind != MapAssetKind.FxMap ||
                    !TryParseFxGlassDefinitionHalfThicknessPath(
                        binding.FieldPath,
                        out int pathOrdinal) ||
                    binding.SourceOrdinal != pathOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact " +
                        "$.glassSystem.defs[index].halfThickness binding " +
                        "with matching source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.FxMap,
                        out FxWorldBuildData? fxWorld) ||
                    fxWorld is null ||
                    pathOrdinal >= fxWorld.GlassSystem.Defs.Count)
                {
                    errors.Add(
                        $"source binding {id} FX glass definition ordinal " +
                        $"{pathOrdinal} is outside the owned FxMap baseline");
                    continue;
                }

                hasFxGlassDefinitionHalfThicknessBinding = true;
            }
            else if (edit.Kind ==
                     MapEditKind.FxGlassDefinitionColor)
            {
                if (assetKind != MapAssetKind.FxMap ||
                    !TryParseFxGlassDefinitionColorPath(
                        binding.FieldPath,
                        out int pathOrdinal) ||
                    binding.SourceOrdinal != pathOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact " +
                        "$.glassSystem.defs[index].color binding with " +
                        "matching source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.FxMap,
                        out FxWorldBuildData? fxWorld) ||
                    fxWorld is null ||
                    pathOrdinal >= fxWorld.GlassSystem.Defs.Count)
                {
                    errors.Add(
                        $"source binding {id} FX glass definition ordinal " +
                        $"{pathOrdinal} is outside the owned FxMap baseline");
                    continue;
                }

                hasFxGlassDefinitionColorBinding = true;
            }
            else if (edit.Kind == MapEditKind.MapEntityKeyValue)
            {
                if (assetKind != MapAssetKind.MapEnts ||
                    !TryParseMapEntPropertyPath(
                        binding.FieldPath,
                        out int entityOrdinal,
                        out int propertyOrdinal,
                        out _ ) ||
                    binding.SourceOrdinal != entityOrdinal)
                {
                    errors.Add(
                        $"source binding {id} is not an exact existing " +
                        "MapEnt entity/property key-or-value binding with a " +
                        "matching entity source ordinal");
                    continue;
                }

                if (!baseline.TryGetBaseline(
                        MapAssetKind.MapEnts,
                        out IMapEntsBuildData? mapEnts) ||
                    mapEnts is null)
                {
                    errors.Add(
                        $"source binding {id} has no owned MapEnts baseline");
                    continue;
                }

                MapEntsSyntaxDocument syntax =
                    MapEntsSyntaxParser.Parse(
                        mapEnts.GetEntityStringBytesCopy());
                if (!syntax.CanEdit ||
                    entityOrdinal >= syntax.Entities.Count ||
                    propertyOrdinal >=
                        syntax.Entities[entityOrdinal].Properties.Count)
                {
                    errors.Add(
                        $"source binding {id} targets MapEnt entity " +
                        $"{entityOrdinal}, property {propertyOrdinal} outside " +
                        "the strict editable baseline syntax");
                    continue;
                }

                hasMapEntPropertyBinding = true;
            }
            else if (edit.Kind == MapEditKind.MapEntityCardinality)
            {
                CompiledMapAssetDescriptor mapEntsDescriptor =
                    baseline.RequireAsset(MapAssetKind.MapEnts);
                string expectedPath =
                    $"{mapEntsDescriptor.SourcePath}.entityStringBytes";
                if (assetKind != MapAssetKind.MapEnts ||
                    binding.SourceOrdinal is not null ||
                    !string.Equals(
                        binding.FieldPath,
                        expectedPath,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"source binding {id} is not the exact owned MapEnts " +
                        "entity-string binding required for a cardinality edit");
                    continue;
                }

                hasMapEntEntityStringBinding = true;
            }
            else if (edit.Kind == MapEditKind.StaticModelVisibility)
            {
                staticModelSuppressionBindings.Add(binding);
            }
            else if (edit.Kind == MapEditKind.StaticModelTransform &&
                     edit.SourceBindings.Count == 5)
            {
                staticModelTranslationBindings.Add(binding);
            }
            else if (edit.Kind == MapEditKind.StaticModelCardinality)
            {
                staticModelRemovalBindings.Add(binding);
            }
            else if (edit.Kind == MapEditKind.StaticModelDuplication)
            {
                staticModelDuplicationBindings.Add(binding);
            }
        }

        if (edit.Kind == MapEditKind.PrimaryLightColor &&
            (edit.SourceBindings.Count != 1 ||
             !hasPrimaryLightColorBinding))
        {
            errors.Add(
                "a primary-light color edit requires exactly one exact " +
                "primaryLights[index].color binding to an owned ComMap");
        }
        if (edit.Kind == MapEditKind.PrimaryLightExponent &&
            (edit.SourceBindings.Count != 1 ||
             !hasPrimaryLightExponentBinding))
        {
            errors.Add(
                "a primary-light exponent edit requires exactly one exact " +
                "primaryLights[index].exponent binding to an owned ComMap");
        }
        if (edit.Kind == MapEditKind.PrimaryLightSpotFalloff &&
            (edit.SourceBindings.Count != 1 ||
             !hasPrimaryLightSpotFalloffBinding))
        {
            errors.Add(
                "a primary-light spot-falloff edit requires exactly one " +
                "exact primaryLights[index].cosHalfFovInner binding to an " +
                "owned type-2 ComMap light");
        }
        if (edit.Kind ==
                MapEditKind.FxGlassDefinitionHalfThickness &&
            (edit.SourceBindings.Count != 1 ||
             !hasFxGlassDefinitionHalfThicknessBinding))
        {
            errors.Add(
                "an FX glass definition HalfThickness edit requires exactly " +
                "one exact $.glassSystem.defs[index].halfThickness binding " +
                "to an owned FxMap");
        }
        if (edit.Kind == MapEditKind.FxGlassDefinitionColor &&
            (edit.SourceBindings.Count != 1 ||
             !hasFxGlassDefinitionColorBinding))
        {
            errors.Add(
                "an FX glass definition Color edit requires exactly one " +
                "exact $.glassSystem.defs[index].color binding to an owned " +
                "FxMap");
        }
        if (edit.Kind == MapEditKind.MapEntityKeyValue &&
            !hasMapEntPropertyBinding)
        {
            errors.Add(
                "a MapEnt property edit requires an exact existing " +
                "entity/property key-or-value binding to owned strict " +
                "MapEnts syntax");
        }
        if (edit.Kind == MapEditKind.MapEntityCardinality &&
            !hasMapEntEntityStringBinding)
        {
            errors.Add(
                "a MapEnt cardinality edit requires the exact owned MapEnts " +
                "entity-string source binding");
        }
        if (edit.Kind == MapEditKind.StaticModelVisibility)
        {
            ValidateStaticModelSuppressionBindingSet(
                staticModelSuppressionBindings,
                baseline,
                errors);
        }
        if (edit.Kind == MapEditKind.StaticModelTransform &&
            edit.SourceBindings.Count == 5)
        {
            ValidateStaticModelTranslationBindingSet(
                staticModelTranslationBindings,
                baseline,
                errors);
        }
        if (edit.Kind == MapEditKind.StaticModelCardinality)
        {
            ValidateStaticModelRemovalBindingSet(
                staticModelRemovalBindings,
                baseline,
                errors);
        }
        if (edit.Kind == MapEditKind.StaticModelDuplication)
        {
            ValidateStaticModelDuplicationBindingSet(
                staticModelDuplicationBindings,
                baseline,
                document,
                errors);
        }

        if (errors.Count == 0)
            return impact;

        string bindingBlocker =
            $"Compiled source-binding evidence is invalid: {string.Join("; ", errors.Distinct(StringComparer.Ordinal))}.";
        string blocker = string.IsNullOrWhiteSpace(impact.SaveBlocker)
            ? bindingBlocker
            : $"{impact.SaveBlocker} {bindingBlocker}";
        MapSaveClassification classification =
            impact.Classification == MapSaveClassification.PatchSaveable
                ? MapSaveClassification.Unsupported
                : impact.Classification;
        return Impact(
            classification,
            impact.AffectedAssets,
            impact.InvalidatedSubsystems,
            blocker);
    }

    private static void ValidateStaticModelSuppressionBindingSet(
        IReadOnlyList<CompiledSourceBinding> bindings,
        CompiledMapBundle baseline,
        ICollection<string> errors)
    {
        if (bindings.Count != 7)
        {
            errors.Add(
                "a compiled static-model suppression requires exactly seven " +
                "mutable-field bindings across one Gfx row and one Col row");
            return;
        }

        CompiledSourceBinding[] gfxBindings = bindings
            .Where(value => value.AssetType ==
                IW4.FastFiles.Zone.XAssetType.GfxMap)
            .ToArray();
        CompiledSourceBinding[] clipBindings = bindings
            .Where(value => value.AssetType is (
                IW4.FastFiles.Zone.XAssetType.ColMapMp or
                IW4.FastFiles.Zone.XAssetType.ColMapSp))
            .ToArray();
        if (gfxBindings.Length != 5 || clipBindings.Length != 2)
        {
            errors.Add(
                "a compiled static-model suppression requires five Gfx " +
                "tombstone fields and two fields from exactly one ColMap");
            return;
        }
        if (clipBindings.Select(value => value.AssetType)
                .Distinct().Count() != 1)
        {
            errors.Add(
                "compiled static-model suppression cannot span multiple " +
                "collision authorities");
            return;
        }

        int[] gfxOrdinals = gfxBindings
            .Select(value => value.SourceOrdinal ?? -1)
            .Distinct()
            .ToArray();
        int[] clipOrdinals = clipBindings
            .Select(value => value.SourceOrdinal ?? -1)
            .Distinct()
            .ToArray();
        if (gfxOrdinals.Length != 1 || gfxOrdinals[0] < 0 ||
            clipOrdinals.Length != 1 || clipOrdinals[0] < 0)
        {
            errors.Add(
                "compiled static-model suppression bindings must resolve to " +
                "one nonnegative Gfx ordinal and one nonnegative Col ordinal");
            return;
        }

        int gfxOrdinal = gfxOrdinals[0];
        int clipOrdinal = clipOrdinals[0];
        string[] expectedGfx =
        [
            $"$.definition.dpvs.sModelDrawInsts[{gfxOrdinal}].placement.origin",
            $"$.definition.dpvs.sModelDrawInsts[{gfxOrdinal}].cullDist",
            $"$.definition.dpvs.sModelDrawInsts[{gfxOrdinal}].flags",
            $"$.definition.dpvs.sModelInsts[{gfxOrdinal}].bounds",
            $"$.definition.dpvs.sModelInsts[{gfxOrdinal}].lightingOrigin"
        ];
        string[] expectedClip =
        [
            $"$.definition.staticModelList[{clipOrdinal}].origin",
            $"$.definition.staticModelList[{clipOrdinal}].absMin"
        ];
        if (!expectedGfx.ToHashSet(StringComparer.Ordinal)
                .SetEquals(gfxBindings.Select(value => value.FieldPath)) ||
            !expectedClip.ToHashSet(StringComparer.Ordinal)
                .SetEquals(clipBindings.Select(value => value.FieldPath)))
        {
            errors.Add(
                "compiled static-model suppression bindings do not match " +
                "the exact canonical Gfx/Col tombstone field set");
        }

        if (!baseline.TryGetBaseline(
            MapAssetKind.GfxMap,
                out IW4.Studio.Documents.GfxWorldBuildData? gfx) ||
            gfx is null ||
            (uint)gfxOrdinal >= gfx.Definition.Dpvs.SModelCount ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelDrawInsts.Count ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelInsts.Count)
        {
            errors.Add(
                $"Gfx static-model suppression ordinal {gfxOrdinal} is " +
                "outside the owned baseline tables");
        }

        MapAssetKind collisionKind =
            clipBindings[0].AssetType ==
                IW4.FastFiles.Zone.XAssetType.ColMapMp
                ? MapAssetKind.ColMapMp
                : MapAssetKind.ColMapSp;
        if (!baseline.TryGetBaseline(
                collisionKind,
                out IW4.Studio.Documents.ClipMapBuildData? clip) ||
            clip is null ||
            (uint)clipOrdinal >= clip.Definition.NumStaticModels ||
            clipOrdinal >= clip.Definition.StaticModelList.Count)
        {
            errors.Add(
                $"Collision static-model suppression ordinal {clipOrdinal} " +
                "is outside the owned baseline table");
        }
    }

    private static void ValidateStaticModelTranslationBindingSet(
        IReadOnlyList<CompiledSourceBinding> bindings,
        CompiledMapBundle baseline,
        ICollection<string> errors)
    {
        if (bindings.Count != 5)
        {
            errors.Add(
                "a compiled static-model translation requires exactly five " +
                "mutable-field bindings across one Gfx row and one Col row");
            return;
        }

        CompiledSourceBinding[] gfxBindings = bindings
            .Where(value => value.AssetType ==
                IW4.FastFiles.Zone.XAssetType.GfxMap)
            .ToArray();
        CompiledSourceBinding[] clipBindings = bindings
            .Where(value => value.AssetType is (
                IW4.FastFiles.Zone.XAssetType.ColMapMp or
                IW4.FastFiles.Zone.XAssetType.ColMapSp))
            .ToArray();
        if (gfxBindings.Length != 3 || clipBindings.Length != 2)
        {
            errors.Add(
                "a compiled static-model translation requires three Gfx " +
                "fields and two fields from exactly one ColMap");
            return;
        }
        if (clipBindings.Select(value => value.AssetType)
                .Distinct().Count() != 1)
        {
            errors.Add(
                "compiled static-model translation cannot span multiple " +
                "collision authorities");
            return;
        }

        int[] gfxOrdinals = gfxBindings
            .Select(value => value.SourceOrdinal ?? -1)
            .Distinct()
            .ToArray();
        int[] clipOrdinals = clipBindings
            .Select(value => value.SourceOrdinal ?? -1)
            .Distinct()
            .ToArray();
        if (gfxOrdinals.Length != 1 || gfxOrdinals[0] < 0 ||
            clipOrdinals.Length != 1 || clipOrdinals[0] < 0)
        {
            errors.Add(
                "compiled static-model translation bindings must resolve " +
                "to one nonnegative Gfx ordinal and one nonnegative Col " +
                "ordinal");
            return;
        }

        int gfxOrdinal = gfxOrdinals[0];
        int clipOrdinal = clipOrdinals[0];
        string[] expectedGfx =
        [
            $"$.definition.dpvs.sModelDrawInsts[{gfxOrdinal}].placement.origin",
            $"$.definition.dpvs.sModelInsts[{gfxOrdinal}].bounds",
            $"$.definition.dpvs.sModelInsts[{gfxOrdinal}].lightingOrigin"
        ];
        string[] expectedClip =
        [
            $"$.definition.staticModelList[{clipOrdinal}].origin",
            $"$.definition.staticModelList[{clipOrdinal}].absMin"
        ];
        if (!expectedGfx.ToHashSet(StringComparer.Ordinal)
                .SetEquals(gfxBindings.Select(value => value.FieldPath)) ||
            !expectedClip.ToHashSet(StringComparer.Ordinal)
                .SetEquals(clipBindings.Select(value => value.FieldPath)))
        {
            errors.Add(
                "compiled static-model translation bindings do not match " +
                "the exact canonical Gfx/Col translated field set");
        }

        if (!baseline.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) ||
            gfx is null ||
            (uint)gfxOrdinal >= gfx.Definition.Dpvs.SModelCount ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelDrawInsts.Count ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelInsts.Count)
        {
            errors.Add(
                $"Gfx static-model translation ordinal {gfxOrdinal} is " +
                "outside the owned baseline tables");
        }

        MapAssetKind collisionKind =
            clipBindings[0].AssetType ==
                IW4.FastFiles.Zone.XAssetType.ColMapMp
                ? MapAssetKind.ColMapMp
                : MapAssetKind.ColMapSp;
        if (!baseline.TryGetBaseline(
                collisionKind,
                out ClipMapBuildData? clip) ||
            clip is null ||
            (uint)clipOrdinal >= clip.Definition.NumStaticModels ||
            clipOrdinal >= clip.Definition.StaticModelList.Count)
        {
            errors.Add(
                $"Collision static-model translation ordinal {clipOrdinal} " +
                "is outside the owned baseline table");
        }
    }

    private static void ValidateStaticModelRemovalBindingSet(
        IReadOnlyList<CompiledSourceBinding> bindings,
        CompiledMapBundle baseline,
        ICollection<string> errors)
    {
        if (bindings.Count is not (2 or 3))
        {
            errors.Add(
                "a compiled static-model removal requires two exact record " +
                "bindings, or three when an adjacent Gfx provider receiver " +
                "is carried forward");
            return;
        }

        CompiledSourceBinding[] gfxBindings = bindings.Where(value =>
                value.AssetType ==
                IW4.FastFiles.Zone.XAssetType.GfxMap)
            .ToArray();
        CompiledSourceBinding[] clipBindings = bindings.Where(value =>
                value.AssetType is (
                    IW4.FastFiles.Zone.XAssetType.ColMapMp or
                    IW4.FastFiles.Zone.XAssetType.ColMapSp))
            .ToArray();
        if (gfxBindings.Length != bindings.Count - 1 ||
            gfxBindings.Length is not (1 or 2) ||
            clipBindings.Length != 1)
        {
            errors.Add(
                "compiled static-model removal bindings must resolve to " +
                "exactly one Gfx record and exactly one Col record, plus " +
                "only the proven adjacent Gfx provider receiver when required");
            return;
        }

        CompiledSourceBinding clipBinding = clipBindings[0];
        if (gfxBindings.Any(value =>
                value.SourceOrdinal is null or < 0) ||
            clipBinding.SourceOrdinal is not { } clipOrdinal ||
            clipOrdinal < 0)
        {
            errors.Add(
                "compiled static-model removal bindings must resolve to " +
                "nonnegative Gfx and Col source ordinals");
            return;
        }
        int[] gfxOrdinals = gfxBindings
            .Select(value => value.SourceOrdinal!.Value)
            .ToArray();
        if (gfxOrdinals.Distinct().Count() != gfxOrdinals.Length)
        {
            errors.Add(
                "compiled static-model removal Gfx binding roles must target " +
                "distinct source ordinals");
            return;
        }

        if (gfxBindings.Any(value =>
                !string.Equals(
                    value.FieldPath,
                    "$.definition.dpvs.sModelDrawInsts" +
                    $"[{value.SourceOrdinal}]",
                    StringComparison.Ordinal)) ||
            !string.Equals(
                clipBinding.FieldPath,
                $"$.definition.staticModelList[{clipOrdinal}]",
                StringComparison.Ordinal))
        {
            errors.Add(
                "compiled static-model removal bindings do not match the " +
                "canonical Gfx/Col record paths");
        }

        if (!baseline.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) ||
            gfx is null)
        {
            errors.Add(
                "compiled static-model removal has no owned Gfx baseline");
            return;
        }
        int[] outOfRangeGfxOrdinals = gfxOrdinals.Where(value =>
                (uint)value >= gfx.Definition.Dpvs.SModelCount ||
                value >=
                    gfx.Definition.Dpvs.SModelDrawInsts.Count ||
                value >=
                    gfx.Definition.Dpvs.SModelInsts.Count)
            .ToArray();
        foreach (int gfxOrdinal in outOfRangeGfxOrdinals)
        {
            errors.Add(
                $"Gfx static-model removal authority ordinal {gfxOrdinal} " +
                "is outside the owned baseline tables");
        }
        if (outOfRangeGfxOrdinals.Length != 0)
            return;

        if (gfxOrdinals.Length == 1)
        {
            GfxStaticModelRemovalAssessment assessment =
                GfxStaticModelRemovalAssessor.Assess(
                    gfx,
                    [gfxOrdinals[0]]);
            if (!assessment.IsEligible)
            {
                errors.Add(
                    "compiled static-model removal Gfx authority does not " +
                    "pass the removal invariant group: " +
                    assessment.Issues[0].Detail);
            }
            else if (assessment.ProviderCarryForwards.Count != 0)
            {
                errors.Add(
                    "compiled static-model removal is missing the exact " +
                    "adjacent Gfx provider-receiver binding authority");
            }
        }
        else
        {
            int carryForwardMatches = gfxOrdinals.Count(
                removedOrdinal =>
                {
                    GfxStaticModelRemovalAssessment assessment =
                        GfxStaticModelRemovalAssessor.Assess(
                            gfx,
                            [removedOrdinal]);
                    return assessment.IsEligible &&
                           assessment.ProviderCarryForwards.Count == 1 &&
                           gfxOrdinals.Contains(
                               assessment.ProviderCarryForwards[0]
                                   .ReceiverOrdinal);
                });
            if (carryForwardMatches != 1)
            {
                errors.Add(
                    "compiled static-model removal does not carry one " +
                    "bijective removed-Gfx/provider-receiver authority pair");
            }
        }

        MapAssetKind collisionKind =
            clipBinding.AssetType ==
                IW4.FastFiles.Zone.XAssetType.ColMapMp
                ? MapAssetKind.ColMapMp
                : MapAssetKind.ColMapSp;
        if (!baseline.TryGetBaseline(
                collisionKind,
                out ClipMapBuildData? clip) ||
            clip is null ||
            (uint)clipOrdinal >= clip.Definition.NumStaticModels ||
            clipOrdinal >= clip.Definition.StaticModelList.Count)
        {
            errors.Add(
                $"Collision static-model removal ordinal {clipOrdinal} is " +
                    "outside the owned baseline table");
            return;
        }

        ClipStaticModelRemovalAssessment clipAssessment =
            ClipStaticModelRemovalAssessor.Assess(
                clip,
                [clipOrdinal]);
        if (!clipAssessment.IsEligible)
        {
            errors.Add(
                "compiled static-model removal collision authority does not " +
                "pass the dependency and spatial invariant group");
        }
    }

    private static void ValidateStaticModelDuplicationBindingSet(
        IReadOnlyList<CompiledSourceBinding> bindings,
        CompiledMapBundle baseline,
        EditorMapDocument document,
        ICollection<string> errors)
    {
        if (bindings.Count != 2)
        {
            errors.Add(
                "a compiled static-model duplication requires exactly two " +
                "imported template-record bindings");
            return;
        }

        CompiledSourceBinding[] gfxBindings = bindings.Where(value =>
                value.AssetType ==
                IW4.FastFiles.Zone.XAssetType.GfxMap)
            .ToArray();
        CompiledSourceBinding[] clipBindings = bindings.Where(value =>
                value.AssetType is (
                    IW4.FastFiles.Zone.XAssetType.ColMapMp or
                    IW4.FastFiles.Zone.XAssetType.ColMapSp))
            .ToArray();
        if (gfxBindings.Length != 1 || clipBindings.Length != 1)
        {
            errors.Add(
                "compiled static-model duplication bindings must resolve to " +
                "one imported Gfx template record and one imported Col " +
                "template record");
            return;
        }

        CompiledSourceBinding gfxBinding = gfxBindings[0];
        CompiledSourceBinding clipBinding = clipBindings[0];
        if (gfxBinding.SourceOrdinal is not { } gfxOrdinal ||
            gfxOrdinal < 0 ||
            clipBinding.SourceOrdinal is not { } clipOrdinal ||
            clipOrdinal < 0)
        {
            errors.Add(
                "compiled static-model duplication template bindings must " +
                "resolve to nonnegative Gfx and Col source ordinals");
            return;
        }

        if (!string.Equals(
                gfxBinding.FieldPath,
                "$.definition.dpvs.sModelDrawInsts" +
                $"[{gfxOrdinal}]",
                StringComparison.Ordinal) ||
            !string.Equals(
                clipBinding.FieldPath,
                $"$.definition.staticModelList[{clipOrdinal}]",
                StringComparison.Ordinal))
        {
            errors.Add(
                "compiled static-model duplication bindings do not match " +
                "the canonical Gfx/Col template-record paths");
        }

        if (!baseline.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) ||
            gfx is null ||
            (uint)gfxOrdinal >= gfx.Definition.Dpvs.SModelCount ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelDrawInsts.Count ||
            gfxOrdinal >= gfx.Definition.Dpvs.SModelInsts.Count)
        {
            errors.Add(
                $"Gfx static-model duplication template ordinal " +
                $"{gfxOrdinal} is outside the owned baseline tables");
        }

        MapAssetKind collisionKind =
            clipBinding.AssetType ==
                IW4.FastFiles.Zone.XAssetType.ColMapMp
                ? MapAssetKind.ColMapMp
                : MapAssetKind.ColMapSp;
        if (!baseline.TryGetBaseline(
                collisionKind,
                out ClipMapBuildData? clip) ||
            clip is null ||
            (uint)clipOrdinal >= clip.Definition.NumStaticModels ||
            clipOrdinal >= clip.Definition.StaticModelList.Count)
        {
            errors.Add(
                $"Collision static-model duplication template ordinal " +
                $"{clipOrdinal} is outside the owned baseline table");
        }

        ValidateAuthoredStaticModelDuplicationState(
            bindings,
            baseline,
            document,
            gfxOrdinal,
            clipOrdinal,
            collisionKind,
            errors);
    }

    private static void ValidateAuthoredStaticModelDuplicationState(
        IReadOnlyList<CompiledSourceBinding> bindings,
        CompiledMapBundle baseline,
        EditorMapDocument document,
        int gfxOrdinal,
        int clipOrdinal,
        MapAssetKind collisionKind,
        ICollection<string> errors)
    {
        EditorStaticModel[] authored = document.StaticModels
            .Where(value => !value.IsImported)
            .ToArray();
        if (authored.Length != 2 ||
            authored.Count(value =>
                value.Representation ==
                StaticModelRepresentation.Render) != 1 ||
            authored.Count(value =>
                value.Representation ==
                StaticModelRepresentation.Collision) != 1)
        {
            errors.Add(
                "compiled static-model duplication requires exactly one " +
                "authored render/collision pair in the semantic document");
            return;
        }

        AuthoredStaticModelDuplicatePairState? state =
            authored[0].AuthoredDuplicatePair;
        if (state is null ||
            authored.Any(value =>
                !ReferenceEquals(
                    value.AuthoredDuplicatePair,
                    state)) ||
            authored.Any(value =>
                value.Id != state.ObjectId(value.Representation) ||
                value.SourceOrdinal.Value !=
                    state.ProjectedOrdinal(value.Representation) ||
                value.CompiledDisposition !=
                    StaticModelCompiledDisposition.AuthoredPending ||
                value.Origin.Value != state.Destination))
        {
            errors.Add(
                "authored static-model duplication rows do not share one " +
                "coherent pending operation state");
            return;
        }

        if (!state.TemplateRecordBindings.ToHashSet().SetEquals(
                bindings.Select(value => value.Id)) ||
            state.GfxTemplateOrdinal != gfxOrdinal ||
            state.ClipTemplateOrdinal != clipOrdinal ||
            state.CollisionAssetKind != collisionKind ||
            !string.Equals(
                state.BundleBaselineDigest,
                baseline.BaselineDigest,
                StringComparison.Ordinal))
        {
            errors.Add(
                "the authored duplicate operation does not match the exact " +
                "journal bindings, template ordinals, collision owner, and " +
                "compiled baseline");
            return;
        }

        if (!document.TryGetObject(
                state.RenderTemplateObjectId,
                out EditorMapObject? renderObject) ||
            renderObject is not EditorStaticModel renderTemplate ||
            !document.TryGetObject(
                state.CollisionTemplateObjectId,
                out EditorMapObject? collisionObject) ||
            collisionObject is not EditorStaticModel collisionTemplate ||
            !renderTemplate.IsImported ||
            !collisionTemplate.IsImported ||
            renderTemplate.SourceOrdinal.SourceBinding !=
                state.GfxTemplateRecordBinding ||
            collisionTemplate.SourceOrdinal.SourceBinding !=
                state.ClipTemplateRecordBinding)
        {
            errors.Add(
                "the authored duplicate operation has no exact imported " +
                "template-object binding authorities");
            return;
        }

        StaticModelCorrespondenceCatalog catalog =
            StaticModelCompilationRelationshipResolver.Resolve(
                baseline,
                document);
        if (!catalog.TryGetByRenderObjectId(
                state.RenderTemplateObjectId,
                out StaticModelCompilationRelationship? relationship) ||
            relationship is null ||
            relationship.CollisionObjectId !=
                state.CollisionTemplateObjectId ||
            relationship.GfxSourceOrdinal != gfxOrdinal ||
            relationship.ClipSourceOrdinal != clipOrdinal ||
            relationship.CollisionAssetKind != collisionKind)
        {
            errors.Add(
                "the authored duplicate templates no longer resolve to one " +
                "ExactBundleUnique Gfx/Col relationship");
            return;
        }

        StaticModelDuplicationEligibilityAssessment eligibility =
            StaticModelDuplicationEligibilityEvaluator.Evaluate(
                baseline,
                document,
                catalog,
                relationship,
                state.Destination);
        if (!eligibility.IsPatchEligible ||
            eligibility.Gfx is null ||
            eligibility.Collision is null ||
            state.GfxProjectedOrdinal !=
                eligibility.Gfx.NewOrdinal ||
            state.ClipProjectedOrdinal !=
                eligibility.Collision.NewOrdinal)
        {
            errors.Add(
                "the authored duplicate operation does not retain concrete, " +
                "destination-bound Gfx/Col compiler evidence: " +
                eligibility.Evidence);
        }
    }

    private static bool TryValidateAgainstBundle(
        CompiledSourceBinding binding,
        CompiledMapBundle baseline,
        out MapAssetKind assetKind,
        out string? error)
    {
        CompiledMapAssetDescriptor? asset = baseline.Assets.FirstOrDefault(
            candidate =>
                candidate.SerializedType == binding.AssetType &&
                candidate.OwnerRow == binding.OwnerRow &&
                string.Equals(
                    candidate.AssetName,
                    binding.AssetName,
                    StringComparison.Ordinal));
        if (asset is null)
        {
            assetKind = default;
            error =
                $"source binding {binding.Id} owner {binding.AssetType} row #{binding.OwnerRow.SerializedIndex} is not owned by the compiled bundle";
            return false;
        }
        if (!string.Equals(
                asset.BaselineDigest,
                binding.BaselineDigest,
                StringComparison.Ordinal))
        {
            assetKind = default;
            error =
                $"source binding {binding.Id} baseline digest does not match owned {asset.Kind}";
            return false;
        }
        if (!binding.FieldPath.StartsWith(
                $"{asset.SourcePath}.",
                StringComparison.Ordinal))
        {
            assetKind = default;
            error =
                $"source binding {binding.Id} field path is outside owned {asset.Kind} source path '{asset.SourcePath}'";
            return false;
        }

        SourceBindingId expectedId = DeterministicMapIdentity.Binding(
            baseline.MapIdentity,
            binding.AssetType.ToString(),
            binding.AssetName,
            binding.FieldPath,
            binding.SourceOrdinal);
        if (expectedId != binding.Id)
        {
            assetKind = default;
            error =
                $"source binding {binding.Id} does not match its deterministic compiled-field identity";
            return false;
        }

        assetKind = asset.Kind;
        error = null;
        return true;
    }

    private static HashSet<SourceBindingId> CollectDocumentBindings(
        EditorMapDocument document)
    {
        var result = new HashSet<SourceBindingId>(
            document.Objects.SelectMany(value => value.SourceBindings));
        result.UnionWith(
            document.Environment.Values.Select(value => value.SourceBinding));
        if (document.EntitySource is not null)
            result.Add(document.EntitySource.SourceBinding);
        return result;
    }

    private static bool TryParsePrimaryLightColorPath(
        string fieldPath,
        out int value)
    {
        const string prefix = "$.primaryLights[";
        const string suffix = "].color";
        if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldPath.EndsWith(suffix, StringComparison.Ordinal))
        {
            value = -1;
            return false;
        }

        ReadOnlySpan<char> ordinal = fieldPath.AsSpan(
            prefix.Length,
            fieldPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(
            ordinal,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value) &&
            value >= 0;
    }

    private static bool TryParsePrimaryLightExponentPath(
        string fieldPath,
        out int value)
    {
        const string prefix = "$.primaryLights[";
        const string suffix = "].exponent";
        if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldPath.EndsWith(suffix, StringComparison.Ordinal))
        {
            value = -1;
            return false;
        }

        ReadOnlySpan<char> ordinal = fieldPath.AsSpan(
            prefix.Length,
            fieldPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(
            ordinal,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value) &&
            value >= 0;
    }

    private static bool TryParsePrimaryLightSpotFalloffPath(
        string fieldPath,
        out int value)
    {
        const string prefix = "$.primaryLights[";
        const string suffix = "].cosHalfFovInner";
        if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldPath.EndsWith(suffix, StringComparison.Ordinal))
        {
            value = -1;
            return false;
        }

        ReadOnlySpan<char> ordinal = fieldPath.AsSpan(
            prefix.Length,
            fieldPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(
            ordinal,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value) &&
            value >= 0;
    }

    private static bool IsValidSpotFalloff(
        byte type,
        float outer,
        float inner) =>
        type == 2 &&
        float.IsFinite(outer) &&
        float.IsFinite(inner) &&
        outer > 0f &&
        outer < inner &&
        inner <= 1f;

    private static bool TryParseFxGlassDefinitionHalfThicknessPath(
        string fieldPath,
        out int value)
    {
        const string prefix = "$.glassSystem.defs[";
        const string suffix = "].halfThickness";
        if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldPath.EndsWith(suffix, StringComparison.Ordinal))
        {
            value = -1;
            return false;
        }

        ReadOnlySpan<char> ordinal = fieldPath.AsSpan(
            prefix.Length,
            fieldPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(
            ordinal,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value) &&
            value >= 0;
    }

    private static bool TryParseFxGlassDefinitionColorPath(
        string fieldPath,
        out int value)
    {
        const string prefix = "$.glassSystem.defs[";
        const string suffix = "].color";
        if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !fieldPath.EndsWith(suffix, StringComparison.Ordinal))
        {
            value = -1;
            return false;
        }

        ReadOnlySpan<char> ordinal = fieldPath.AsSpan(
            prefix.Length,
            fieldPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(
            ordinal,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value) &&
            value >= 0;
    }

    private static bool TryParseMapEntPropertyPath(
        string fieldPath,
        out int entityOrdinal,
        out int propertyOrdinal,
        out string field)
    {
        entityOrdinal = -1;
        propertyOrdinal = -1;
        field = string.Empty;
        const string entityMarker = ".entityStringBytes.entities[";
        const string propertyMarker = "].properties[";
        int entityStart = fieldPath.IndexOf(
            entityMarker,
            StringComparison.Ordinal);
        if (entityStart < 1)
        {
            return false;
        }

        entityStart += entityMarker.Length;
        int propertyStart = fieldPath.IndexOf(
            propertyMarker,
            entityStart,
            StringComparison.Ordinal);
        if (propertyStart < 0 ||
            !int.TryParse(
                fieldPath.AsSpan(
                    entityStart,
                    propertyStart - entityStart),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out entityOrdinal) ||
            entityOrdinal < 0)
        {
            return false;
        }

        propertyStart += propertyMarker.Length;
        int propertyEnd = fieldPath.IndexOf(
            ']',
            propertyStart);
        if (propertyEnd < 0 ||
            !int.TryParse(
                fieldPath.AsSpan(
                    propertyStart,
                    propertyEnd - propertyStart),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out propertyOrdinal) ||
            propertyOrdinal < 0)
        {
            return false;
        }

        ReadOnlySpan<char> suffix =
            fieldPath.AsSpan(propertyEnd + 1);
        if (suffix.SequenceEqual(".key"))
            field = "key";
        else if (suffix.SequenceEqual(".value"))
            field = "value";
        else
        {
            return false;
        }
        return true;
    }

    private static MapEditImpact Impact(
        MapSaveClassification classification,
        IEnumerable<MapAssetKind> assets,
        MapDerivedSubsystem invalidated,
        string? blocker = null) =>
        new(classification, assets, invalidated, blocker);
}
