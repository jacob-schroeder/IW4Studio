using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;

namespace IW4.Studio.MapEditor.Editing.Entities;

/// <summary>
/// Proven relationship between one MapEnt text entity and compiled map
/// subsystems. Unknown is intentionally distinct from MapEnt-only.
/// </summary>
public enum MapEntityCompilationRelationship
{
    Unknown,
    MapEntOnlyProven,
    CompiledCounterpart
}

public sealed record MapEntityCompilationAssessment(
    MapEntityCompilationRelationship Relationship,
    string Evidence)
{
    /// <summary>
    /// Identifies the entity-level consumer relationship only. Callers must
    /// also obtain exact property-operation evidence before authorizing an
    /// existing property edit.
    /// </summary>
    public bool CanPatchExistingProperty =>
        Relationship ==
        MapEntityCompilationRelationship.MapEntOnlyProven;
}

public sealed record MapEntityConsumerEvidence(
    string ClassName,
    MapEntityCompilationRelationship Relationship,
    string Evidence);

public enum MapEntityPropertyEditOperation
{
    ReplaceValue,
    ReplaceKey
}

/// <summary>
/// One reviewed authorization tuple for an existing MapEnt property. Catalog
/// absence is intentionally interpreted as unknown rather than text-only.
/// </summary>
public sealed record MapEntityPropertyConsumerEvidence(
    string ClassName,
    string PropertyKey,
    MapEntityPropertyEditOperation Operation,
    string Evidence);

/// <summary>
/// Result of classifying one exact existing-property operation. For key
/// replacement, <see cref="SourceKey"/> and <see cref="TargetKey"/> must each
/// have independent <see cref="MapEntityPropertyEditOperation.ReplaceKey"/>
/// evidence.
/// </summary>
public sealed record MapEntityPropertyEditAssessment(
    string ClassName,
    string SourceKey,
    string TargetKey,
    MapEntityPropertyEditOperation Operation,
    MapEntityCompilationRelationship Relationship,
    string Evidence)
{
    public bool IsPatchAuthorized =>
        Relationship ==
        MapEntityCompilationRelationship.MapEntOnlyProven;
}

public enum MapEntityCardinalityOperation
{
    Append,
    Remove
}

/// <summary>
/// Executable-backed authority for one exact entity-cardinality operation.
/// Addresses are PS3 ELF virtual addresses and are deliberately retained as
/// reviewable evidence rather than collapsed into a general classname rule.
/// </summary>
public sealed record MapEntityCardinalityConsumerEvidence(
    string ClassName,
    MapEntityCardinalityOperation Operation,
    string ExecutableSha256,
    uint SpawnDispatcherAddress,
    uint ClassNameTableRowAddress,
    uint ClassNamePointerCellAddress,
    uint ClassNameStringAddress,
    uint SpawnHandlerDescriptorPointerCellAddress,
    uint SpawnHandlerDescriptorAddress,
    uint SpawnHandlerEntryAddress,
    uint SpawnHandlerTocAddress,
    string Evidence);

public sealed record MapEntityCardinalityAssessment(
    string ClassName,
    MapEntityCardinalityOperation Operation,
    MapEntityCompilationRelationship Relationship,
    string Evidence)
{
    public bool IsPatchAuthorized =>
        Relationship ==
        MapEntityCompilationRelationship.MapEntOnlyProven;
}

/// <summary>
/// Closed, reviewable consumer taxonomy for existing MapEnt property edits.
/// Cardinality authorization is a separate, narrower evidence set.
/// Structural markers that imply compiled geometry always override classname
/// evidence.
/// </summary>
public sealed class MapEntityConsumerCatalog
{
    private readonly IReadOnlyDictionary<string, MapEntityConsumerEvidence>
        _byClassName;
    private readonly IReadOnlyDictionary<
        MapEntityPropertyConsumerIdentity,
        MapEntityPropertyConsumerEvidence> _byPropertyOperation;
    private readonly IReadOnlyDictionary<
        MapEntityCardinalityConsumerIdentity,
        MapEntityCardinalityConsumerEvidence> _byCardinalityOperation;

    public MapEntityConsumerCatalog(
        IEnumerable<MapEntityConsumerEvidence> evidence,
        IEnumerable<MapEntityPropertyConsumerEvidence>? propertyEvidence = null,
        IEnumerable<MapEntityCardinalityConsumerEvidence>?
            cardinalityEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var byClassName =
            new Dictionary<string, MapEntityConsumerEvidence>(
                StringComparer.OrdinalIgnoreCase);
        foreach (MapEntityConsumerEvidence entry in evidence)
        {
            if (entry is null)
            {
                throw new ArgumentException(
                    "MapEnt consumer evidence cannot contain null entries.",
                    nameof(evidence));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.ClassName);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Evidence);
            if (entry.Relationship ==
                MapEntityCompilationRelationship.Unknown)
            {
                throw new ArgumentException(
                    "Unknown consumers must remain absent from the proven catalog.",
                    nameof(evidence));
            }
            if (!byClassName.TryAdd(entry.ClassName, entry))
            {
                throw new ArgumentException(
                    $"Duplicate MapEnt consumer evidence for classname " +
                    $"'{entry.ClassName}'.",
                    nameof(evidence));
            }
        }

        _byClassName =
            new ReadOnlyDictionary<string, MapEntityConsumerEvidence>(
                byClassName);

        var byPropertyOperation = new Dictionary<
            MapEntityPropertyConsumerIdentity,
            MapEntityPropertyConsumerEvidence>(
                MapEntityPropertyConsumerIdentityComparer.Instance);
        foreach (MapEntityPropertyConsumerEvidence entry in
                 propertyEvidence ??
                 Enumerable.Empty<MapEntityPropertyConsumerEvidence>())
        {
            if (entry is null)
            {
                throw new ArgumentException(
                    "MapEnt property consumer evidence cannot contain null entries.",
                    nameof(propertyEvidence));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.ClassName);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.PropertyKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Evidence);
            if (!Enum.IsDefined(entry.Operation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(propertyEvidence),
                    $"Unknown MapEnt property edit operation {entry.Operation}.");
            }
            if (!byClassName.TryGetValue(
                    entry.ClassName,
                    out MapEntityConsumerEvidence? classEvidence) ||
                classEvidence.Relationship !=
                    MapEntityCompilationRelationship.MapEntOnlyProven)
            {
                throw new ArgumentException(
                    $"Property evidence for classname '{entry.ClassName}' " +
                    "requires a proven MapEnt-only entity consumer.",
                    nameof(propertyEvidence));
            }

            var identity = new MapEntityPropertyConsumerIdentity(
                entry.ClassName,
                entry.PropertyKey,
                entry.Operation);
            if (!byPropertyOperation.TryAdd(identity, entry))
            {
                throw new ArgumentException(
                    "Duplicate MapEnt property consumer evidence for " +
                    Format(identity),
                    nameof(propertyEvidence));
            }
        }

        _byPropertyOperation = new ReadOnlyDictionary<
            MapEntityPropertyConsumerIdentity,
            MapEntityPropertyConsumerEvidence>(byPropertyOperation);

        var byCardinalityOperation = new Dictionary<
            MapEntityCardinalityConsumerIdentity,
            MapEntityCardinalityConsumerEvidence>(
                MapEntityCardinalityConsumerIdentityComparer.Instance);
        foreach (MapEntityCardinalityConsumerEvidence entry in
                 cardinalityEvidence ??
                 Enumerable.Empty<MapEntityCardinalityConsumerEvidence>())
        {
            if (entry is null)
            {
                throw new ArgumentException(
                    "MapEnt cardinality evidence cannot contain null entries.",
                    nameof(cardinalityEvidence));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.ClassName);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                entry.ExecutableSha256);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Evidence);
            if (!Enum.IsDefined(entry.Operation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cardinalityEvidence),
                    $"Unknown MapEnt cardinality operation {entry.Operation}.");
            }
            if (entry.ExecutableSha256.Length != 64 ||
                entry.ExecutableSha256.Any(value =>
                    !Uri.IsHexDigit(value)) ||
                entry.SpawnDispatcherAddress == 0 ||
                entry.ClassNameTableRowAddress == 0 ||
                entry.ClassNamePointerCellAddress == 0 ||
                entry.ClassNameStringAddress == 0 ||
                entry.SpawnHandlerDescriptorPointerCellAddress == 0 ||
                entry.SpawnHandlerDescriptorAddress == 0 ||
                entry.SpawnHandlerEntryAddress == 0 ||
                entry.SpawnHandlerTocAddress == 0)
            {
                throw new ArgumentException(
                    "MapEnt cardinality evidence requires a complete SHA-256 " +
                    "and non-zero executable addresses.",
                    nameof(cardinalityEvidence));
            }
            if (!byClassName.TryGetValue(
                    entry.ClassName,
                    out MapEntityConsumerEvidence? classEvidence) ||
                classEvidence.Relationship !=
                    MapEntityCompilationRelationship.MapEntOnlyProven)
            {
                throw new ArgumentException(
                    $"Cardinality evidence for classname '{entry.ClassName}' " +
                    "requires a proven MapEnt-only entity consumer.",
                    nameof(cardinalityEvidence));
            }

            var identity = new MapEntityCardinalityConsumerIdentity(
                entry.ClassName,
                entry.Operation);
            if (!byCardinalityOperation.TryAdd(identity, entry))
            {
                throw new ArgumentException(
                    "Duplicate MapEnt cardinality consumer evidence for " +
                    $"classname '{entry.ClassName}', operation " +
                    $"'{entry.Operation}'.",
                    nameof(cardinalityEvidence));
            }
        }

        _byCardinalityOperation = new ReadOnlyDictionary<
            MapEntityCardinalityConsumerIdentity,
            MapEntityCardinalityConsumerEvidence>(
                byCardinalityOperation);
    }

    /// <summary>
    /// Conservative IW4 catalog for existing property edits. Entity evidence
    /// and property-operation evidence are separate gates. The catalog
    /// deliberately omits topology-changing authorization.
    /// </summary>
    public static MapEntityConsumerCatalog ConservativeIw4 { get; } =
        CreateConservativeIw4();

    public MapEntityCompilationAssessment Classify(
        IEnumerable<KeyValuePair<string, string>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        KeyValuePair<string, string>[] copy = properties.ToArray();
        KeyValuePair<string, string>[] modelProperties = copy
            .Where(value => string.Equals(
                value.Key,
                "model",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (modelProperties.Any(value => value.Value.StartsWith('*')))
        {
            return new MapEntityCompilationAssessment(
                MapEntityCompilationRelationship.CompiledCounterpart,
                "The entity references a compiled brush model through model *n.");
        }
        if (modelProperties.Length != 0)
        {
            return new MapEntityCompilationAssessment(
                MapEntityCompilationRelationship.CompiledCounterpart,
                "The entity has a model consumer that may require compiled " +
                "render, collision, or dependency state.");
        }

        string[] classNames = copy
            .Where(value => string.Equals(
                value.Key,
                "classname",
                StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Value)
            .ToArray();
        if (classNames.Length != 1 ||
            string.IsNullOrWhiteSpace(classNames[0]))
        {
            return Unknown(
                "A unique non-empty classname was not preserved.");
        }

        string className = classNames[0];
        if (className.StartsWith(
                "trigger_",
                StringComparison.OrdinalIgnoreCase) ||
            className.StartsWith(
                "func_",
                StringComparison.OrdinalIgnoreCase) ||
            className.StartsWith(
                "node_",
                StringComparison.OrdinalIgnoreCase))
        {
            return new MapEntityCompilationAssessment(
                MapEntityCompilationRelationship.CompiledCounterpart,
                $"Classname '{className}' participates in compiled " +
                "collision, brush-model, or path data.");
        }

        if (_byClassName.TryGetValue(
                className,
                out MapEntityConsumerEvidence? evidence))
        {
            return new MapEntityCompilationAssessment(
                evidence.Relationship,
                evidence.Evidence);
        }

        return Unknown(
            $"Classname '{className}' has no proven MapEnt-only consumer mapping.");
    }

    /// <summary>
    /// Classifies an exact existing-property operation. Value replacement
    /// requires evidence for the current key. Key replacement requires
    /// independent evidence for both the current and replacement keys.
    /// </summary>
    public MapEntityPropertyEditAssessment ClassifyExistingPropertyEdit(
        IEnumerable<KeyValuePair<string, string>> properties,
        string sourceKey,
        MapEntityPropertyEditOperation operation,
        string? replacementKey = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(sourceKey);
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (operation == MapEntityPropertyEditOperation.ReplaceKey &&
            replacementKey is null)
        {
            throw new ArgumentNullException(
                nameof(replacementKey),
                "Key replacement requires the proposed replacement key.");
        }

        KeyValuePair<string, string>[] copy = properties.ToArray();
        MapEntityCompilationAssessment entityAssessment = Classify(copy);
        string className = GetUniqueClassName(copy) ?? "<unknown>";
        string targetKey =
            operation == MapEntityPropertyEditOperation.ReplaceKey
                ? replacementKey!
                : sourceKey;
        if (entityAssessment.Relationship !=
            MapEntityCompilationRelationship.MapEntOnlyProven)
        {
            return new MapEntityPropertyEditAssessment(
                className,
                sourceKey,
                targetKey,
                operation,
                entityAssessment.Relationship,
                $"{Format(className, sourceKey, operation)} is not " +
                "patch-authorized because the entity-level consumer " +
                $"relationship is not proven MapEnt-only: " +
                entityAssessment.Evidence);
        }

        var sourceIdentity = new MapEntityPropertyConsumerIdentity(
            className,
            sourceKey,
            operation);
        if (!_byPropertyOperation.TryGetValue(
                sourceIdentity,
                out MapEntityPropertyConsumerEvidence? sourceEvidence))
        {
            return UnknownPropertyEdit(
                className,
                sourceKey,
                targetKey,
                operation,
                $"{Format(sourceIdentity)} has no proven existing-property " +
                "consumer mapping.");
        }

        if (operation == MapEntityPropertyEditOperation.ReplaceKey)
        {
            var targetIdentity = new MapEntityPropertyConsumerIdentity(
                className,
                targetKey,
                operation);
            if (!_byPropertyOperation.TryGetValue(
                    targetIdentity,
                    out MapEntityPropertyConsumerEvidence? targetEvidence))
            {
                return UnknownPropertyEdit(
                    className,
                    sourceKey,
                    targetKey,
                    operation,
                    $"{Format(targetIdentity)} has no proven replacement-key " +
                    "consumer mapping.");
            }

            return new MapEntityPropertyEditAssessment(
                className,
                sourceKey,
                targetKey,
                operation,
                MapEntityCompilationRelationship.MapEntOnlyProven,
                $"Source evidence: {sourceEvidence.Evidence} Target " +
                $"evidence: {targetEvidence.Evidence}");
        }

        return new MapEntityPropertyEditAssessment(
            className,
            sourceKey,
            targetKey,
            operation,
            MapEntityCompilationRelationship.MapEntOnlyProven,
            sourceEvidence.Evidence);
    }

    /// <summary>
    /// Classifies the exact, deliberately narrow cardinality slice. The
    /// caller supplies whether the row is physically last because arbitrary
    /// middle removal would invalidate ordinal-based runtime relationships.
    /// </summary>
    public MapEntityCardinalityAssessment ClassifyCardinalityEdit(
        IEnumerable<KeyValuePair<string, string>> properties,
        MapEntityCardinalityOperation operation,
        bool isPhysicalTail)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));

        KeyValuePair<string, string>[] copy = properties.ToArray();
        string className = GetUniqueClassName(copy) ?? "<unknown>";
        if (!isPhysicalTail)
        {
            return UnknownCardinality(
                className,
                operation,
                "Only the physical final MapEnt row can participate in the " +
                "proven append/remove transition.");
        }

        KeyValuePair<string, string>[] canonical;
        try
        {
            canonical =
                MapEntsSyntaxEditing
                    .ValidateAndCanonicalizeScriptOrigin(copy);
        }
        catch (MapEntsEditRejectedException exception)
        {
            return UnknownCardinality(
                className,
                operation,
                "The row is outside the reviewed script_origin " +
                $"cardinality schema: {exception.Message}");
        }

        className = canonical.Single(value =>
            value.Key == "classname").Value;
        MapEntityCompilationAssessment entityAssessment =
            Classify(canonical);
        if (entityAssessment.Relationship !=
            MapEntityCompilationRelationship.MapEntOnlyProven)
        {
            return new MapEntityCardinalityAssessment(
                className,
                operation,
                entityAssessment.Relationship,
                "Entity-level consumer classification does not authorize " +
                $"cardinality changes: {entityAssessment.Evidence}");
        }

        var identity = new MapEntityCardinalityConsumerIdentity(
            className,
            operation);
        if (!_byCardinalityOperation.TryGetValue(
                identity,
                out MapEntityCardinalityConsumerEvidence? evidence))
        {
            return UnknownCardinality(
                className,
                operation,
                $"No executable-backed cardinality evidence exists for " +
                $"classname '{className}', operation '{operation}'.");
        }

        return new MapEntityCardinalityAssessment(
            className,
            operation,
            MapEntityCompilationRelationship.MapEntOnlyProven,
            $"{evidence.Evidence} IW4 PS3 ELF SHA-256 " +
            $"{evidence.ExecutableSha256}; dispatcher " +
            $"0x{evidence.SpawnDispatcherAddress:X8}; table row " +
            $"0x{evidence.ClassNameTableRowAddress:X8}; classname pointer " +
            $"cell 0x{evidence.ClassNamePointerCellAddress:X8} -> " +
            $"0x{evidence.ClassNameStringAddress:X8}; handler descriptor " +
            $"pointer cell " +
            $"0x{evidence.SpawnHandlerDescriptorPointerCellAddress:X8} -> " +
            $"0x{evidence.SpawnHandlerDescriptorAddress:X8}; handler entry " +
            $"0x{evidence.SpawnHandlerEntryAddress:X8}; handler TOC " +
            $"0x{evidence.SpawnHandlerTocAddress:X8}.");
    }

    private static MapEntityConsumerCatalog CreateConservativeIw4()
    {
        MapEntityConsumerEvidence[] entityEvidence =
        [
            TextOnly("info_null"),
            TextOnly("info_notnull"),
            TextOnly("script_origin"),
            TextOnly("script_struct"),
            TextOnly("info_player_start"),
            TextOnly("info_player_deathmatch"),
            TextOnly("mp_dm_spawn"),
            TextOnly("mp_tdm_spawn"),
            TextOnly("mp_dom_spawn"),
            TextOnly("mp_sab_spawn"),
            TextOnly("mp_hq_spawn"),
            TextOnly("mp_ctf_spawn"),
            TextOnly("mp_koth_spawn"),
            TextOnly("mp_sd_spawn_attacker"),
            TextOnly("mp_sd_spawn_defender"),
            TextOnly("mp_global_intermission"),
            TextOnly("mp_team_intermission"),
            Compiled("worldspawn", "owns compiled world/environment state"),
            Compiled("light", "has compiled ComMap/GfxMap light counterparts"),
            Compiled(
                "reflection_probe",
                "has compiled GfxMap reflection-probe state"),
            Compiled(
                "script_model",
                "may have compiled render/collision model state"),
            Compiled(
                "script_brushmodel",
                "owns a compiled brush-model counterpart"),
            Compiled(
                "misc_model",
                "may have compiled render/collision model state")
        ];
        string[] safeKeys =
        [
            "origin",
            "angles",
            "angle",
            "target",
            "targetname",
            "spawnflags"
        ];
        MapEntityPropertyConsumerEvidence[] propertyEvidence = entityEvidence
            .Where(value =>
                value.Relationship ==
                MapEntityCompilationRelationship.MapEntOnlyProven)
            .SelectMany(entity => safeKeys.SelectMany(key =>
                Enum.GetValues<MapEntityPropertyEditOperation>()
                    .Select(operation =>
                        ExistingProperty(
                            entity.ClassName,
                            key,
                            operation))))
            .ToArray();
        const string executableSha256 =
            "a2e79a8498dd63bebbf899eb04cc5928574fd229df52c082a8f01184906237a6";
        MapEntityCardinalityConsumerEvidence[] cardinalityEvidence =
        [
            Cardinality(
                MapEntityCardinalityOperation.Append,
                executableSha256),
            Cardinality(
                MapEntityCardinalityOperation.Remove,
                executableSha256)
        ];
        return new MapEntityConsumerCatalog(
            entityEvidence,
            propertyEvidence,
            cardinalityEvidence);
    }

    private static string? GetUniqueClassName(
        IEnumerable<KeyValuePair<string, string>> properties)
    {
        string[] classNames = properties
            .Where(value => string.Equals(
                value.Key,
                "classname",
                StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Value)
            .ToArray();
        return classNames.Length == 1 &&
               !string.IsNullOrWhiteSpace(classNames[0])
            ? classNames[0]
            : null;
    }

    private static MapEntityPropertyEditAssessment UnknownPropertyEdit(
        string className,
        string sourceKey,
        string targetKey,
        MapEntityPropertyEditOperation operation,
        string evidence) =>
        new(
            className,
            sourceKey,
            targetKey,
            operation,
            MapEntityCompilationRelationship.Unknown,
            evidence);

    private static MapEntityCardinalityAssessment UnknownCardinality(
        string className,
        MapEntityCardinalityOperation operation,
        string evidence) =>
        new(
            className,
            operation,
            MapEntityCompilationRelationship.Unknown,
            evidence);

    private static MapEntityCompilationAssessment Unknown(string evidence) =>
        new(
            MapEntityCompilationRelationship.Unknown,
            evidence);

    private static MapEntityConsumerEvidence TextOnly(string className) =>
        new(
            className,
            MapEntityCompilationRelationship.MapEntOnlyProven,
            $"Existing '{className}' property values are consumed from the " +
            "MapEnt text record; entity creation and removal require separate " +
            "operation-specific cardinality evidence.");

    private static MapEntityPropertyConsumerEvidence ExistingProperty(
        string className,
        string propertyKey,
        MapEntityPropertyEditOperation operation) =>
        new(
            className,
            propertyKey,
            operation,
            $"Existing '{className}' MapEnt key '{propertyKey}' is proven for " +
            $"{operation}; property and entity cardinality remain unchanged.");

    private static MapEntityCardinalityConsumerEvidence Cardinality(
        MapEntityCardinalityOperation operation,
        string executableSha256) =>
        new(
            "script_origin",
            operation,
            executableSha256,
            SpawnDispatcherAddress: 0x001B9210,
            ClassNameTableRowAddress: 0x006F7F94,
            ClassNamePointerCellAddress: 0x006F7F94,
            ClassNameStringAddress: 0x00562528,
            SpawnHandlerDescriptorPointerCellAddress: 0x006F7F98,
            SpawnHandlerDescriptorAddress: 0x00707AB8,
            SpawnHandlerEntryAddress: 0x001B37A8,
            SpawnHandlerTocAddress: 0x00724C38,
            "The reviewed script_origin spawn handler performs generic " +
            "entity initialization/runtime linking and has no compiled-map " +
            "counterpart lookup.");

    private static MapEntityConsumerEvidence Compiled(
        string className,
        string evidence) =>
        new(
            className,
            MapEntityCompilationRelationship.CompiledCounterpart,
            $"Classname '{className}' {evidence}.");

    private static string Format(
        MapEntityPropertyConsumerIdentity identity) =>
        Format(
            identity.ClassName,
            identity.PropertyKey,
            identity.Operation);

    private static string Format(
        string className,
        string propertyKey,
        MapEntityPropertyEditOperation operation) =>
        $"MapEnt consumer tuple (classname='{className}', " +
        $"key='{propertyKey}', operation='{operation}')";

    private readonly record struct MapEntityPropertyConsumerIdentity(
        string ClassName,
        string PropertyKey,
        MapEntityPropertyEditOperation Operation);

    private readonly record struct MapEntityCardinalityConsumerIdentity(
        string ClassName,
        MapEntityCardinalityOperation Operation);

    private sealed class MapEntityPropertyConsumerIdentityComparer :
        IEqualityComparer<MapEntityPropertyConsumerIdentity>
    {
        public static MapEntityPropertyConsumerIdentityComparer Instance
            { get; } = new();

        public bool Equals(
            MapEntityPropertyConsumerIdentity left,
            MapEntityPropertyConsumerIdentity right) =>
            left.Operation == right.Operation &&
            string.Equals(
                left.ClassName,
                right.ClassName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.PropertyKey,
                right.PropertyKey,
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            MapEntityPropertyConsumerIdentity value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ClassName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PropertyKey),
                value.Operation);
    }

    private sealed class MapEntityCardinalityConsumerIdentityComparer :
        IEqualityComparer<MapEntityCardinalityConsumerIdentity>
    {
        public static MapEntityCardinalityConsumerIdentityComparer Instance
            { get; } = new();

        public bool Equals(
            MapEntityCardinalityConsumerIdentity left,
            MapEntityCardinalityConsumerIdentity right) =>
            left.Operation == right.Operation &&
            string.Equals(
                left.ClassName,
                right.ClassName,
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            MapEntityCardinalityConsumerIdentity value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ClassName),
                value.Operation);
    }

}
