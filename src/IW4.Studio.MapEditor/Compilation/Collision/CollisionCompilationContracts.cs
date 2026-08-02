using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Semantic ownership of collision source geometry. Ownership is independent
/// from whether the source was imported or authored.
/// </summary>
public enum CollisionOwnershipCategory
{
    StandaloneWorld = 0,
    PairedStaticModel = 1,
    BrushModelEntity = 2
}

public enum CollisionSourceProvenance
{
    Imported = 0,
    Authored = 1
}

public enum CollisionGeometryKind
{
    ConvexBrush = 0,
    TriangleMesh = 1,
    StaticModelHull = 2
}

public enum CollisionCounterpartKind
{
    RenderStaticModel = 0,
    MapEntity = 1
}

/// <summary>
/// Explicit semantic counterpart. Its absence is valid for standalone world
/// collision and is never filled by name, ordinal, bounds, or proximity.
/// </summary>
public readonly record struct CollisionCounterpartIdentity
{
    public CollisionCounterpartIdentity(
        MapObjectId objectId,
        CollisionCounterpartKind kind)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        ObjectId = objectId;
        Kind = kind;
    }

    public MapObjectId ObjectId { get; }
    public CollisionCounterpartKind Kind { get; }
}

/// <summary>
/// Stable collision compiler input identity. Imported ordinals are retained as
/// provenance and deterministic-order evidence; they are not emitted indices.
/// </summary>
public sealed class CollisionCompilationSource
{
    public CollisionCompilationSource(
        MapObjectId objectId,
        CollisionGeometryKind geometryKind,
        CollisionOwnershipCategory ownership,
        CollisionSourceProvenance provenance,
        int? importedSourceOrdinal,
        CollisionCounterpartIdentity? counterpart)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (!Enum.IsDefined(geometryKind))
            throw new ArgumentOutOfRangeException(nameof(geometryKind));
        if (!Enum.IsDefined(ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));
        if (!Enum.IsDefined(provenance))
            throw new ArgumentOutOfRangeException(nameof(provenance));
        if (importedSourceOrdinal is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(importedSourceOrdinal));
        }
        if (provenance == CollisionSourceProvenance.Imported &&
            importedSourceOrdinal is null)
        {
            throw new ArgumentException(
                "Imported collision sources require their exact source " +
                "ordinal.",
                nameof(importedSourceOrdinal));
        }
        if (provenance == CollisionSourceProvenance.Authored &&
            importedSourceOrdinal is not null)
        {
            throw new ArgumentException(
                "Authored collision sources cannot claim an imported source " +
                "ordinal.",
                nameof(importedSourceOrdinal));
        }
        if (counterpart is { } explicitCounterpart)
        {
            if (explicitCounterpart.ObjectId.Value == Guid.Empty ||
                !Enum.IsDefined(explicitCounterpart.Kind))
            {
                throw new ArgumentException(
                    "A collision counterpart identity must be fully " +
                    "initialized.",
                    nameof(counterpart));
            }
            if (explicitCounterpart.ObjectId == objectId)
            {
                throw new ArgumentException(
                    "A collision source cannot be its own counterpart.",
                    nameof(counterpart));
            }
        }
        if (ownership == CollisionOwnershipCategory.PairedStaticModel &&
            geometryKind != CollisionGeometryKind.StaticModelHull)
        {
            throw new ArgumentException(
                "Paired static-model collision must identify static-model " +
                "hull geometry.",
                nameof(geometryKind));
        }
        if (ownership == CollisionOwnershipCategory.BrushModelEntity &&
            geometryKind != CollisionGeometryKind.ConvexBrush)
        {
            throw new ArgumentException(
                "Brush-model entity collision must identify convex-brush " +
                "geometry.",
                nameof(geometryKind));
        }

        ValidateCounterpart(ownership, counterpart);

        ObjectId = objectId;
        GeometryKind = geometryKind;
        Ownership = ownership;
        Provenance = provenance;
        ImportedSourceOrdinal = importedSourceOrdinal;
        Counterpart = counterpart;
    }

    public MapObjectId ObjectId { get; }
    public CollisionGeometryKind GeometryKind { get; }
    public CollisionOwnershipCategory Ownership { get; }
    public CollisionSourceProvenance Provenance { get; }
    public int? ImportedSourceOrdinal { get; }
    public CollisionCounterpartIdentity? Counterpart { get; }

    private static void ValidateCounterpart(
        CollisionOwnershipCategory ownership,
        CollisionCounterpartIdentity? counterpart)
    {
        CollisionCounterpartKind? expected = ownership switch
        {
            CollisionOwnershipCategory.StandaloneWorld => null,
            CollisionOwnershipCategory.PairedStaticModel =>
                CollisionCounterpartKind.RenderStaticModel,
            CollisionOwnershipCategory.BrushModelEntity =>
                CollisionCounterpartKind.MapEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(ownership))
        };

        if (expected is null && counterpart is not null)
        {
            throw new ArgumentException(
                "Standalone world collision cannot claim a graphics or " +
                "entity counterpart.",
                nameof(counterpart));
        }
        if (expected is not null && counterpart is null)
        {
            throw new ArgumentException(
                $"{ownership} collision requires an explicit counterpart.",
                nameof(counterpart));
        }
        if (expected is not null && counterpart?.Kind != expected)
        {
            throw new ArgumentException(
                $"{ownership} collision requires a {expected} counterpart.",
                nameof(counterpart));
        }
    }
}

/// <summary>
/// Independent serialized ColMap array domains. An index is meaningful only
/// inside its declared domain; equal numeric values across domains are not
/// interchangeable.
/// </summary>
public enum CollisionIndexDomain
{
    Plane = 0,
    StaticModel = 1,
    Material = 2,
    BrushSide = 3,
    BrushEdge = 4,
    BspNode = 5,
    Leaf = 6,
    LeafBrushNode = 7,
    LeafBrushReference = 8,
    LeafSurfaceReference = 9,
    TriangleVertex = 10,
    TriangleIndex = 11,
    // Aggregate payload derived from the complete TriangleIndex cardinality.
    // It never owns source-local ranges because packed edge words may cross
    // source boundaries.
    TriangleWalkabilityPackedByte = 12,
    Border = 13,
    Partition = 14,
    AabbTreeNode = 15,
    CollisionModel = 16,
    Brush = 17,
    BrushBounds = 18,
    BrushContents = 19,
    StaticModelAabbNode = 20,
    DynamicEntityDefinitionSlot0 = 21,
    DynamicEntityDefinitionSlot1 = 22,
    DynamicEntityPoseSlot0 = 23,
    DynamicEntityPoseSlot1 = 24,
    DynamicEntityClientSlot0 = 25,
    DynamicEntityClientSlot1 = 26,
    DynamicEntityCollisionSlot0 = 27,
    DynamicEntityCollisionSlot1 = 28
}

// CBrush.GlassPieceIndex is intentionally absent above. It targets an
// external Fx/Game glass identity and is not a ColMap-owned emitted array.

/// <summary>
/// Number of records one semantic source will own in one emitted index domain.
/// </summary>
public readonly record struct CollisionIndexContribution
{
    public CollisionIndexContribution(
        MapObjectId sourceObjectId,
        CollisionIndexDomain domain,
        int elementCount)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (domain ==
            CollisionIndexDomain.TriangleWalkabilityPackedByte)
        {
            throw new ArgumentException(
                "Triangle walkability bytes are one aggregate payload " +
                "derived from the complete triangle count; sources cannot " +
                "own packed-byte ranges.",
                nameof(domain));
        }
        CollisionIndexAllocationPolicies.RequirePerSourceContribution(domain);
        if (elementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));

        SourceObjectId = sourceObjectId;
        Domain = domain;
        ElementCount = elementCount;
    }

    public MapObjectId SourceObjectId { get; }
    public CollisionIndexDomain Domain { get; }
    public int ElementCount { get; }
}

public readonly record struct CollisionEmittedIndexRange
{
    public CollisionEmittedIndexRange(int start, int count)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        Start = start;
        Count = count;
        EndExclusive = checked(start + count);
    }

    public int Start { get; }
    public int Count { get; }
    public int EndExclusive { get; }
}

public sealed record CollisionSourceIndexMapping(
    MapObjectId SourceObjectId,
    CollisionIndexDomain Domain,
    CollisionEmittedIndexRange EmittedRange);

/// <summary>
/// Read-only cardinality view shared by the source-contribution plan and the
/// later structural plan that supplies compiler-aggregate domains.
/// </summary>
public interface ICollisionDomainCardinalityPlan
{
    int GetDomainCount(CollisionIndexDomain domain);
}

/// <summary>
/// Deterministic, immutable source-to-emitted-index contract. This is a
/// planning artifact only; it does not create ColMap records.
/// </summary>
public sealed class CollisionSourceIndexPlan : ICollisionDomainCardinalityPlan
{
    private readonly IReadOnlyList<CollisionCompilationSource> _orderedSources;
    private readonly IReadOnlyList<CollisionSourceIndexMapping> _mappings;
    private readonly IReadOnlyDictionary<
        CollisionSourceIndexKey,
        CollisionEmittedIndexRange> _ranges;
    private readonly IReadOnlyDictionary<CollisionIndexDomain, int>
        _domainCounts;

    private CollisionSourceIndexPlan(
        IReadOnlyList<CollisionCompilationSource> orderedSources,
        IReadOnlyList<CollisionSourceIndexMapping> mappings,
        IReadOnlyDictionary<
            CollisionSourceIndexKey,
            CollisionEmittedIndexRange> ranges,
        IReadOnlyDictionary<CollisionIndexDomain, int> domainCounts)
    {
        _orderedSources = orderedSources;
        _mappings = mappings;
        _ranges = ranges;
        _domainCounts = domainCounts;
    }

    public IReadOnlyList<CollisionCompilationSource> OrderedSources =>
        _orderedSources;

    public IReadOnlyList<CollisionSourceIndexMapping> Mappings => _mappings;

    public static CollisionSourceIndexPlan Create(
        IEnumerable<CollisionCompilationSource> sources,
        IEnumerable<CollisionIndexContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(contributions);

        CollisionCompilationSource[] sourceCopy = sources.ToArray();
        CollisionIndexContribution[] contributionCopy =
            contributions.ToArray();
        ValidateSources(sourceCopy);

        Dictionary<MapObjectId, CollisionCompilationSource> sourceById =
            sourceCopy.ToDictionary(value => value.ObjectId);
        ValidateContributions(
            sourceCopy,
            sourceById,
            contributionCopy);

        CollisionCompilationSource[] orderedSources = sourceCopy
            .Order(CollisionCompilationSourceComparer.Instance)
            .ToArray();
        Dictionary<MapObjectId, int> sourceRank = orderedSources
            .Select((source, index) => (source.ObjectId, index))
            .ToDictionary(value => value.ObjectId, value => value.index);

        var mappings = new List<CollisionSourceIndexMapping>(
            contributionCopy.Length);
        var ranges = new Dictionary<
            CollisionSourceIndexKey,
            CollisionEmittedIndexRange>();
        var domainCounts = new Dictionary<CollisionIndexDomain, int>();

        foreach (IGrouping<
                     CollisionIndexDomain,
                     CollisionIndexContribution> domain in contributionCopy
                     .OrderBy(value => value.Domain)
                     .ThenBy(value => sourceRank[value.SourceObjectId])
                     .GroupBy(value => value.Domain))
        {
            int next = 0;
            foreach (CollisionIndexContribution contribution in domain)
            {
                var range = new CollisionEmittedIndexRange(
                    next,
                    contribution.ElementCount);
                next = range.EndExclusive;
                var key = new CollisionSourceIndexKey(
                    contribution.SourceObjectId,
                    contribution.Domain);
                ranges.Add(key, range);
                mappings.Add(new CollisionSourceIndexMapping(
                    contribution.SourceObjectId,
                    contribution.Domain,
                    range));
            }

            CollisionIndexDomainSerializationPolicies
                .GetRequired(domain.Key)
                .ValidateElementCount(next);
            domainCounts.Add(domain.Key, next);
        }

        CollisionTriangleAggregateIndexValidator
            .ValidateAndAddDerivedDomains(domainCounts);
        CollisionRuntimeDerivedIndexValidator
            .ValidateAndAddDerivedDomains(domainCounts);
        var plan = new CollisionSourceIndexPlan(
            Array.AsReadOnly(orderedSources),
            Array.AsReadOnly(mappings.ToArray()),
            new ReadOnlyDictionary<
                CollisionSourceIndexKey,
                CollisionEmittedIndexRange>(ranges),
            new ReadOnlyDictionary<CollisionIndexDomain, int>(domainCounts));
        CollisionParallelIndexDomainValidator.Validate(plan);
        return plan;
    }

    public bool TryGetRange(
        MapObjectId sourceObjectId,
        CollisionIndexDomain domain,
        out CollisionEmittedIndexRange range)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return _ranges.TryGetValue(
            new CollisionSourceIndexKey(sourceObjectId, domain),
            out range);
    }

    public CollisionEmittedIndexRange GetRequiredRange(
        MapObjectId sourceObjectId,
        CollisionIndexDomain domain) =>
        TryGetRange(sourceObjectId, domain, out CollisionEmittedIndexRange range)
            ? range
            : throw new KeyNotFoundException(
                $"Collision source {sourceObjectId} has no {domain} index " +
                "mapping.");

    public int GetDomainCount(CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return _domainCounts.GetValueOrDefault(domain);
    }

    private static void ValidateSources(
        IReadOnlyList<CollisionCompilationSource> sources)
    {
        if (sources.Any(value => value is null))
        {
            throw new ArgumentException(
                "Collision sources cannot contain null entries.",
                nameof(sources));
        }
        MapObjectId? duplicateObjectId = sources
            .GroupBy(value => value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateObjectId is not null)
        {
            throw new ArgumentException(
                $"Collision source identity {duplicateObjectId} is duplicated.",
                nameof(sources));
        }

        CollisionCompilationSource[] importedOrdinalCollision = sources
            .Where(value =>
                value.Provenance == CollisionSourceProvenance.Imported)
            .GroupBy(value => new
            {
                value.GeometryKind,
                value.ImportedSourceOrdinal
            })
            .FirstOrDefault(value => value.Count() > 1)
            ?.ToArray() ?? [];
        if (importedOrdinalCollision.Length != 0)
        {
            CollisionCompilationSource first =
                importedOrdinalCollision[0];
            throw new ArgumentException(
                $"Imported {first.GeometryKind} source ordinal " +
                $"{first.ImportedSourceOrdinal} is duplicated.",
                nameof(sources));
        }

        MapObjectId? duplicateStaticModelCounterpart = sources
            .Where(value =>
                value.Ownership ==
                    CollisionOwnershipCategory.PairedStaticModel)
            .GroupBy(value => value.Counterpart!.Value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateStaticModelCounterpart is not null)
        {
            throw new ArgumentException(
                $"Render static-model counterpart " +
                $"{duplicateStaticModelCounterpart} is claimed by multiple " +
                "collision sources.",
                nameof(sources));
        }
    }

    private static void ValidateContributions(
        IReadOnlyList<CollisionCompilationSource> sources,
        IReadOnlyDictionary<MapObjectId, CollisionCompilationSource> sourceById,
        IReadOnlyList<CollisionIndexContribution> contributions)
    {
        foreach (CollisionIndexContribution contribution in contributions)
        {
            if (contribution.SourceObjectId.Value == Guid.Empty ||
                !Enum.IsDefined(contribution.Domain) ||
                contribution.ElementCount <= 0)
            {
                throw new ArgumentException(
                    "Collision index contributions must be fully initialized.",
                    nameof(contributions));
            }
            if (contribution.Domain ==
                CollisionIndexDomain.TriangleWalkabilityPackedByte)
            {
                throw new ArgumentException(
                    "Triangle walkability bytes are aggregate derived data " +
                    "and cannot have source-owned contributions.",
                    nameof(contributions));
            }
            try
            {
                CollisionIndexAllocationPolicies
                    .RequirePerSourceContribution(contribution.Domain);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    exception.Message,
                    nameof(contributions),
                    exception);
            }
            if (!sourceById.ContainsKey(contribution.SourceObjectId))
            {
                throw new ArgumentException(
                    $"Collision index contribution references unknown source " +
                    $"{contribution.SourceObjectId}.",
                    nameof(contributions));
            }
        }

        CollisionIndexContribution[] duplicateContribution = contributions
            .GroupBy(value => new
            {
                value.SourceObjectId,
                value.Domain
            })
            .FirstOrDefault(value => value.Count() > 1)
            ?.ToArray() ?? [];
        if (duplicateContribution.Length != 0)
        {
            CollisionIndexContribution first = duplicateContribution[0];
            throw new ArgumentException(
                $"Collision source {first.SourceObjectId} declares " +
                $"{first.Domain} ownership more than once.",
                nameof(contributions));
        }

        MapObjectId? sourceWithoutContribution = sources
            .Select(value => value.ObjectId)
            .FirstOrDefault(sourceId =>
                contributions.All(value =>
                    value.SourceObjectId != sourceId));
        if (sourceWithoutContribution is MapObjectId missing &&
            missing.Value != Guid.Empty)
        {
            throw new ArgumentException(
                $"Collision source {missing} has no emitted index-domain " +
                "contribution.",
                nameof(contributions));
        }
    }

    private readonly record struct CollisionSourceIndexKey(
        MapObjectId SourceObjectId,
        CollisionIndexDomain Domain);

    private sealed class CollisionCompilationSourceComparer :
        IComparer<CollisionCompilationSource>
    {
        public static CollisionCompilationSourceComparer Instance { get; } =
            new();

        public int Compare(
            CollisionCompilationSource? left,
            CollisionCompilationSource? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            int comparison = left.Provenance.CompareTo(right.Provenance);
            if (comparison != 0)
                return comparison;

            if (left.Provenance == CollisionSourceProvenance.Imported)
            {
                comparison = left.ImportedSourceOrdinal!.Value.CompareTo(
                    right.ImportedSourceOrdinal!.Value);
                if (comparison != 0)
                    return comparison;
            }

            comparison = left.GeometryKind.CompareTo(right.GeometryKind);
            if (comparison != 0)
                return comparison;
            comparison = left.Ownership.CompareTo(right.Ownership);
            if (comparison != 0)
                return comparison;

            return StringComparer.Ordinal.Compare(
                left.ObjectId.Value.ToString("N"),
                right.ObjectId.Value.ToString("N"));
        }
    }
}

/// <summary>
/// Runtime pose/client/collision arrays are allocated from the same-slot
/// persistent definition count. They are never per-source payload
/// contributions.
/// </summary>
internal static class CollisionRuntimeDerivedIndexValidator
{
    private static readonly CollisionIndexDomain[][] Domains =
    [
        [
            CollisionIndexDomain.DynamicEntityDefinitionSlot0,
            CollisionIndexDomain.DynamicEntityPoseSlot0,
            CollisionIndexDomain.DynamicEntityClientSlot0,
            CollisionIndexDomain.DynamicEntityCollisionSlot0
        ],
        [
            CollisionIndexDomain.DynamicEntityDefinitionSlot1,
            CollisionIndexDomain.DynamicEntityPoseSlot1,
            CollisionIndexDomain.DynamicEntityClientSlot1,
            CollisionIndexDomain.DynamicEntityCollisionSlot1
        ]
    ];

    public static void ValidateAndAddDerivedDomains(
        IDictionary<CollisionIndexDomain, int> domainCounts)
    {
        ArgumentNullException.ThrowIfNull(domainCounts);

        foreach (CollisionIndexDomain[] slot in Domains)
        {
            domainCounts.TryGetValue(slot[0], out int definitionCount);
            if (definitionCount == 0)
                continue;

            for (int index = 1; index < slot.Length; index++)
            {
                CollisionIndexDomain runtimeDomain = slot[index];
                CollisionIndexDomainSerializationPolicies
                    .GetRequired(runtimeDomain)
                    .ValidateElementCount(definitionCount);
                domainCounts.Add(runtimeDomain, definitionCount);
            }
        }
    }
}
