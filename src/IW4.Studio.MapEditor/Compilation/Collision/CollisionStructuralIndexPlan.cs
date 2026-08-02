using System.Collections.ObjectModel;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Final compiler-owned cardinality for one aggregate ColMap domain. These
/// counts describe output from the complete source set and never create
/// source-local ownership or index ranges.
/// </summary>
public readonly record struct CollisionCompilerAggregateCardinality
{
    public CollisionCompilerAggregateCardinality(
        CollisionIndexDomain domain,
        int elementCount)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (elementCount < 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));

        CollisionIndexAllocationPolicy policy =
            CollisionIndexAllocationPolicies.GetRequired(domain);
        if (policy.Ownership !=
            CollisionIndexAllocationOwnership.CompilerAggregate)
        {
            throw new ArgumentException(
                $"{domain} is {policy.Ownership} and cannot receive a " +
                "compiler-aggregate cardinality.",
                nameof(domain));
        }
        if (domain ==
            CollisionIndexDomain.TriangleWalkabilityPackedByte)
        {
            throw new ArgumentException(
                "Triangle walkability cardinality is derived from the final " +
                "triangle-index count and cannot be supplied independently.",
                nameof(domain));
        }

        CollisionIndexDomainSerializationPolicies
            .GetRequired(domain)
            .ValidateElementCount(elementCount);
        Domain = domain;
        ElementCount = elementCount;
    }

    public CollisionIndexDomain Domain { get; }
    public int ElementCount { get; }
}

/// <summary>
/// Compiler-synthesized rows appended to a shared catalog after all
/// source-contributed ranges. The first M3 use is the deterministic BSP split
/// plane; the synthesized rows receive no semantic source ownership.
/// </summary>
public readonly record struct CollisionCompilerCatalogCardinality
{
    public CollisionCompilerCatalogCardinality(
        CollisionIndexDomain domain,
        int elementCount)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (elementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));

        CollisionIndexAllocationPolicy policy =
            CollisionIndexAllocationPolicies.GetRequired(domain);
        if (policy.Ownership !=
            CollisionIndexAllocationOwnership.SharedCatalog)
        {
            throw new ArgumentException(
                $"{domain} is {policy.Ownership} and cannot receive " +
                "compiler-synthesized shared-catalog rows.",
                nameof(domain));
        }

        Domain = domain;
        ElementCount = elementCount;
    }

    public CollisionIndexDomain Domain { get; }
    public int ElementCount { get; }
}

/// <summary>
/// Composes source-owned/shared-catalog cardinalities with the aggregate
/// topology counts produced by the structural compiler. This is the first
/// plan that can preflight a complete detached ColMap candidate; it does not
/// alter the source ranges locked by <see cref="CollisionSourceIndexPlan"/>.
/// </summary>
public sealed class CollisionStructuralIndexPlan :
    ICollisionDomainCardinalityPlan
{
    private static readonly IReadOnlyList<CollisionIndexDomain>
        RequiredAggregateDomains =
            Array.AsReadOnly(
                CollisionIndexAllocationPolicies.All
                    .Where(value =>
                        value.Ownership ==
                            CollisionIndexAllocationOwnership
                                .CompilerAggregate &&
                        value.Domain !=
                            CollisionIndexDomain
                                .TriangleWalkabilityPackedByte)
                    .Select(value => value.Domain)
                    .Order()
                    .ToArray());

    private readonly IReadOnlyDictionary<CollisionIndexDomain, int>
        _aggregateCounts;
    private readonly IReadOnlyDictionary<CollisionIndexDomain, int>
        _compilerCatalogCounts;

    private CollisionStructuralIndexPlan(
        CollisionSourceIndexPlan sourcePlan,
        IReadOnlyDictionary<CollisionIndexDomain, int> aggregateCounts,
        IReadOnlyDictionary<CollisionIndexDomain, int>
            compilerCatalogCounts)
    {
        SourcePlan = sourcePlan;
        _aggregateCounts = aggregateCounts;
        _compilerCatalogCounts = compilerCatalogCounts;
    }

    public CollisionSourceIndexPlan SourcePlan { get; }

    public IReadOnlyDictionary<CollisionIndexDomain, int> AggregateCounts =>
        _aggregateCounts;

    public IReadOnlyDictionary<CollisionIndexDomain, int>
        CompilerCatalogCounts => _compilerCatalogCounts;

    public static CollisionStructuralIndexPlan Create(
        CollisionSourceIndexPlan sourcePlan,
        IEnumerable<CollisionCompilerAggregateCardinality>
            aggregateCardinalities,
        IEnumerable<CollisionCompilerCatalogCardinality>?
            compilerCatalogCardinalities = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePlan);
        ArgumentNullException.ThrowIfNull(aggregateCardinalities);

        CollisionCompilerAggregateCardinality[] supplied =
            aggregateCardinalities.ToArray();
        CollisionIndexDomain? duplicate = supplied
            .GroupBy(value => value.Domain)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicate is { } duplicateDomain)
        {
            throw new ArgumentException(
                $"Compiler-aggregate cardinality {duplicateDomain} is " +
                "declared more than once.",
                nameof(aggregateCardinalities));
        }

        CollisionIndexDomain[] missing = RequiredAggregateDomains
            .Except(supplied.Select(value => value.Domain))
            .ToArray();
        CollisionIndexDomain[] unexpected = supplied
            .Select(value => value.Domain)
            .Except(RequiredAggregateDomains)
            .ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
        {
            string detail = string.Join(
                "; ",
                new[]
                {
                    missing.Length == 0
                        ? null
                        : "missing " + string.Join(", ", missing),
                    unexpected.Length == 0
                        ? null
                        : "unexpected " +
                          string.Join(", ", unexpected)
                }.Where(value => value is not null));
            throw new ArgumentException(
                "A structural index plan requires one count for every " +
                $"compiler-aggregate domain ({detail}).",
                nameof(aggregateCardinalities));
        }

        var counts = new ReadOnlyDictionary<CollisionIndexDomain, int>(
            supplied.ToDictionary(
                value => value.Domain,
                value => value.ElementCount));
        CollisionCompilerCatalogCardinality[] suppliedCatalog =
            (compilerCatalogCardinalities ?? []).ToArray();
        CollisionIndexDomain? duplicateCatalog = suppliedCatalog
            .GroupBy(value => value.Domain)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateCatalog is { } duplicateCatalogDomain)
        {
            throw new ArgumentException(
                $"Compiler catalog cardinality {duplicateCatalogDomain} is " +
                "declared more than once.",
                nameof(compilerCatalogCardinalities));
        }

        var catalogCounts =
            new ReadOnlyDictionary<CollisionIndexDomain, int>(
                suppliedCatalog.ToDictionary(
                    value => value.Domain,
                    value => value.ElementCount));
        foreach ((CollisionIndexDomain domain, int addition) in
                 catalogCounts)
        {
            int total = checked(
                sourcePlan.GetDomainCount(domain) + addition);
            CollisionIndexDomainSerializationPolicies
                .GetRequired(domain)
                .ValidateElementCount(total);
        }

        return new CollisionStructuralIndexPlan(
            sourcePlan,
            counts,
            catalogCounts);
    }

    public int GetDomainCount(CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        if (domain ==
            CollisionIndexDomain.TriangleWalkabilityPackedByte)
        {
            return SourcePlan.GetDomainCount(domain);
        }
        if (_aggregateCounts.TryGetValue(domain, out int aggregateCount))
            return aggregateCount;

        return checked(
            SourcePlan.GetDomainCount(domain) +
            _compilerCatalogCounts.GetValueOrDefault(domain));
    }

    public CollisionEmittedIndexRange GetRequiredCompilerCatalogRange(
        CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (!_compilerCatalogCounts.TryGetValue(domain, out int count))
        {
            throw new KeyNotFoundException(
                $"{domain} has no compiler-synthesized shared-catalog " +
                "range.");
        }

        return new CollisionEmittedIndexRange(
            SourcePlan.GetDomainCount(domain),
            count);
    }
}
