using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Editing.SavePlanning;

public enum MapSaveClassification
{
    EditorOnly,
    PatchSaveable,
    PartialRebuildRequired,
    FullRebuildRequired,
    Unsupported
}

public enum MapAssetKind
{
    GfxMap,
    ColMapSp,
    ColMapMp,
    ComMap,
    MapEnts,
    FxMap,
    GameMapMp
}

[Flags]
public enum MapDerivedSubsystem
{
    None = 0,
    RenderGeometry = 1 << 0,
    SurfaceSorting = 1 << 1,
    Collision = 1 << 2,
    StaticModelBoundsAndSpatialMembership = 1 << 3,
    CellsPortalsAndAabb = 1 << 4,
    Dpvs = 1 << 5,
    PrimaryLightMembershipAndRegions = 1 << 6,
    SunAndLocalShadowData = 1 << 7,
    BakedLighting = 1 << 8,
    GlassCrossLinks = 1 << 9,
    MapEntBrushModelAndEntityIndices = 1 << 10,
    DependenciesSidecarsAndChecksums = 1 << 11
}

public sealed record MapEditImpact
{
    public MapEditImpact(
        MapSaveClassification classification,
        IEnumerable<MapAssetKind> affectedAssets,
        MapDerivedSubsystem invalidatedSubsystems,
        string? saveBlocker)
    {
        ArgumentNullException.ThrowIfNull(affectedAssets);
        Classification = classification;
        AffectedAssets = new HashSet<MapAssetKind>(affectedAssets);
        InvalidatedSubsystems = invalidatedSubsystems;
        SaveBlocker = string.IsNullOrWhiteSpace(saveBlocker)
            ? null
            : saveBlocker;

        if ((classification is MapSaveClassification.PartialRebuildRequired or
             MapSaveClassification.FullRebuildRequired or
             MapSaveClassification.Unsupported) &&
            SaveBlocker is null)
        {
            throw new ArgumentException(
                "Unsafe map edits require a precise save blocker.",
                nameof(saveBlocker));
        }
    }

    public MapSaveClassification Classification { get; }
    public IReadOnlySet<MapAssetKind> AffectedAssets { get; }
    public MapDerivedSubsystem InvalidatedSubsystems { get; }
    public string? SaveBlocker { get; }
}

public enum MapEditKind
{
    EditorOnly,
    PrimaryLightColor,
    PrimaryLightExponent,
    PrimaryLightSpotFalloff,
    PrimaryLightInfluence,
    EnvironmentValue,
    MapEntityKeyValue,
    MapEntityCardinality,
    StaticModelVisibility,
    StaticModelTransform,
    StaticModelCardinality,
    StaticModelDuplication,
    CollisionGeometry,
    CollisionCardinality,
    MaterialReplacement,
    FxGlassDefinitionHalfThickness,
    FxGlassDefinitionColor,
    GlassProperty,
    GlassCardinality,
    WorldTopology,
    BrushCellOrPortalTopology,
    BakedLighting,
    Unknown
}

/// <summary>
/// Canonical impact definitions shared by semantic commands and compiled
/// save planning. Keeping high-risk edit boundaries here prevents a preview
/// workflow from accidentally advertising a weaker persistence contract.
/// </summary>
public static class MapEditImpactTaxonomy
{
    private const string AuthoredCollisionPersistenceBlocker =
        "Authored collision currently compiles only to an offline M3 " +
        "structural candidate. FastFile persistence remains blocked until " +
        "M4 consumer acceptance and M7 dependency/linking and emitter " +
        "integration are complete.";

    /// <summary>
    /// Reshapes or translates one canonical authored collision source without
    /// changing authored source cardinality.
    /// </summary>
    public static MapEditImpact AuthoredCollisionGeometry() =>
        AuthoredCollisionFullRebuild();

    /// <summary>
    /// Adds or removes one canonical authored collision source.
    /// </summary>
    public static MapEditImpact AuthoredCollisionCardinality() =>
        AuthoredCollisionFullRebuild();

    /// <summary>
    /// Changes the exponent byte of one existing ComPrimaryLight without
    /// changing light cardinality, its spatial envelope, membership, or
    /// derived-lighting topology.
    /// </summary>
    public static MapEditImpact PrimaryLightExponent() =>
        new(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.ComMap],
            MapDerivedSubsystem.None,
            saveBlocker: null);

    /// <summary>
    /// Changes the inner cone cosine of one existing type-2 ComPrimaryLight.
    /// The outer and expanded cones remain exact, so spatial membership and
    /// every compiled shadow/light-region consumer retain their imported
    /// envelope.
    /// </summary>
    public static MapEditImpact PrimaryLightSpotFalloff() =>
        new(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.ComMap],
            MapDerivedSubsystem.None,
            saveBlocker: null);

    /// <summary>
    /// Changes one existing FxGlassDef scalar without changing definition,
    /// initial-piece, geometry, runtime-cache, or dependency topology.
    /// Initial-piece thickness is a derived editor projection through DefIndex
    /// and is not a second serialized mutation.
    /// </summary>
    public static MapEditImpact FxGlassDefinitionHalfThickness() =>
        new(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.FxMap],
            MapDerivedSubsystem.None,
            saveBlocker: null);

    /// <summary>
    /// Changes the packed RGBA value of one existing FxGlassDef without
    /// changing definition, piece, geometry, cache, or dependency topology.
    /// </summary>
    public static MapEditImpact FxGlassDefinitionColor() =>
        new(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.FxMap],
            MapDerivedSubsystem.None,
            saveBlocker: null);

    /// <summary>
    /// Conservative compiled suppression of one artifact-local, uniquely
    /// proven render/collision pair. Existing spatial memberships remain
    /// intentionally conservative; this capability is not a transform.
    /// </summary>
    public static MapEditImpact StaticModelSuppression(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind),
                "Static-model suppression requires an exact ColMap owner.");
        }

        return new MapEditImpact(
            MapSaveClassification.PatchSaveable,
            [
                MapAssetKind.GfxMap,
                collisionAssetKind
            ],
            MapDerivedSubsystem.None,
            saveBlocker: null);
    }

    /// <summary>
    /// Translation of one exact-bundle render/collision pair after the
    /// compilation layer has proven that every retained Gfx membership and
    /// lighting assignment remains valid and that the Clip tree can be
    /// expanded conservatively.
    /// </summary>
    public static MapEditImpact CompiledStaticModelTranslation(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind),
                "Static-model translation requires an exact ColMap owner.");
        }

        return new MapEditImpact(
            MapSaveClassification.PatchSaveable,
            [
                MapAssetKind.GfxMap,
                collisionAssetKind
            ],
            MapDerivedSubsystem.None,
            saveBlocker: null);
    }

    /// <summary>
    /// Removal of one exact-bundle render/collision pair after every Gfx and
    /// Clip ordinal consumer has passed the cardinality rebuild gate.
    /// </summary>
    public static MapEditImpact CompiledStaticModelRemoval(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind),
                "Static-model removal requires an exact ColMap owner.");
        }

        return new MapEditImpact(
            MapSaveClassification.PatchSaveable,
            [
                MapAssetKind.GfxMap,
                collisionAssetKind
            ],
            MapDerivedSubsystem.None,
            saveBlocker: null);
    }

    /// <summary>
    /// Duplication of one exact-bundle render/collision pair after both
    /// typed cardinality builders have proven destination capacity,
    /// dependency ownership, relationship identity, and spatial coverage.
    /// The two existing template records are the complete source authority;
    /// the authored destination has no imported binding.
    /// </summary>
    public static MapEditImpact CompiledStaticModelDuplication(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind),
                "Static-model duplication requires an exact ColMap owner.");
        }

        return new MapEditImpact(
            MapSaveClassification.PatchSaveable,
            [
                MapAssetKind.GfxMap,
                collisionAssetKind
            ],
            MapDerivedSubsystem.None,
            saveBlocker: null);
    }

    public static MapEditImpact StaticModelTransform() =>
        new(
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
            MapDerivedSubsystem.PrimaryLightMembershipAndRegions |
            MapDerivedSubsystem.BakedLighting,
            "General static-model transforms remain blocked. Only an " +
            "exact Gfx/Col pair whose existing cell, AABB-leaf, probe, " +
            "primary-light, shadow, and runtime-lighting invariants are " +
            "proven unchanged can use the conservative translation builder.");

    private static MapEditImpact AuthoredCollisionFullRebuild() =>
        new(
            MapSaveClassification.FullRebuildRequired,
            [MapAssetKind.ColMapMp],
            MapDerivedSubsystem.Collision |
            MapDerivedSubsystem.CellsPortalsAndAabb |
            MapDerivedSubsystem.DependenciesSidecarsAndChecksums,
            AuthoredCollisionPersistenceBlocker);
}

/// <summary>
/// Save-planning input produced by a future semantic command journal. Compiled
/// bindings remain opaque here; the compilation-layer planner resolves and
/// validates them against the imported binding catalog and baseline bundle.
/// </summary>
public sealed record MapPendingEdit
{
    private readonly IReadOnlyList<SourceBindingId> _sourceBindings;

    public MapPendingEdit(
        string description,
        MapEditKind kind,
        IEnumerable<SourceBindingId>? sourceBindings = null,
        bool preservationCoverageProven = false,
        bool hasRequiredBuilder = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        SourceBindingId[] bindingCopy = (sourceBindings ?? [])
            .Distinct()
            .ToArray();
        if (bindingCopy.Any(binding => binding.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "Pending map edits cannot reference an empty source binding.",
                nameof(sourceBindings));
        }

        Description = description;
        Kind = kind;
        _sourceBindings = new ReadOnlyCollection<SourceBindingId>(bindingCopy);
        PreservationCoverageProven = preservationCoverageProven;
        HasRequiredBuilder = hasRequiredBuilder;
    }

    public string Description { get; }
    public MapEditKind Kind { get; }
    public IReadOnlyList<SourceBindingId> SourceBindings => _sourceBindings;
    public bool HasAuthoritativeBinding => _sourceBindings.Count != 0;
    public bool PreservationCoverageProven { get; }
    public bool HasRequiredBuilder { get; }
}
