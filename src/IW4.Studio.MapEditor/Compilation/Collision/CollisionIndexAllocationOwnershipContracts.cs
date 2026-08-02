using System.Collections.ObjectModel;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Ownership of payload contributions for one emitted ColMap domain. This is
/// deliberately separate from serialized block ownership and final ordinal
/// authority: every persistent row is emitted by the ColMap body, but not
/// every row payload belongs to one semantic geometry source.
/// </summary>
public enum CollisionIndexAllocationOwnership
{
    /// <summary>
    /// Each row belongs to one semantic collision source and receives a
    /// deterministic source-to-emitted range.
    /// </summary>
    SourceOwned = 0,

    /// <summary>
    /// Sources contribute definitions, while the compiler assigns ordinals
    /// in one map-wide catalog. Source ranges remain valid provenance for
    /// catalog entries; numeric equality never implies cross-source identity.
    /// </summary>
    SharedCatalog = 1,

    /// <summary>
    /// The complete source set is compiled into one aggregate payload.
    /// Individual sources cannot claim geometry/payload ranges in this
    /// domain; a separate cross-asset catalog may still allocate row identity.
    /// </summary>
    CompilerAggregate = 2,

    /// <summary>
    /// The serialized root reserves runtime storage. Authored or imported
    /// source payloads cannot own its rows.
    /// </summary>
    RuntimeDerived = 3
}

/// <summary>
/// Authority for final row ordinals, kept separate from payload-contribution
/// ownership. In particular, CModel payloads are compiled from complete
/// geometry while their cross-asset row identities come from the inline-model
/// catalog.
/// </summary>
public enum CollisionIndexOrdinalAuthority
{
    SourceContributionPlan = 0,
    CompleteSourceCompiler = 1,
    InlineModelIdentityPlan = 2,
    DynamicDefinitionCardinality = 3
}

/// <summary>
/// Payload-ownership and ordinal-authority policy for one independent
/// collision index domain.
/// </summary>
public sealed record CollisionIndexAllocationPolicy(
    CollisionIndexDomain Domain,
    CollisionIndexAllocationOwnership Ownership,
    CollisionIndexOrdinalAuthority OrdinalAuthority)
{
    public bool AcceptsPerSourceContribution =>
        Ownership is CollisionIndexAllocationOwnership.SourceOwned or
            CollisionIndexAllocationOwnership.SharedCatalog;
}

/// <summary>
/// Complete allocation-authority catalog for every known ColMap domain.
/// Spatial structures are aggregate compiler output; this classification
/// does not implement their M3 construction algorithms.
/// </summary>
public static class CollisionIndexAllocationPolicies
{
    private static readonly IReadOnlyDictionary<
        CollisionIndexDomain,
        CollisionIndexAllocationPolicy> Policies =
        new ReadOnlyDictionary<
            CollisionIndexDomain,
            CollisionIndexAllocationPolicy>(CreatePolicies());

    public static IReadOnlyCollection<CollisionIndexAllocationPolicy> All =>
        Array.AsReadOnly(Policies.Values
            .OrderBy(value => value.Domain)
            .ToArray());

    public static CollisionIndexAllocationPolicy GetRequired(
        CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return Policies.TryGetValue(domain, out var policy)
            ? policy
            : throw new InvalidOperationException(
                $"Collision domain {domain} has no allocation-ownership " +
                "policy.");
    }

    public static void RequirePerSourceContribution(
        CollisionIndexDomain domain)
    {
        CollisionIndexAllocationPolicy policy = GetRequired(domain);
        if (policy.AcceptsPerSourceContribution)
            return;

        throw new ArgumentException(
            $"{domain} is {policy.Ownership} output and cannot be claimed " +
            "by an individual collision source.",
            nameof(domain));
    }

    private static Dictionary<
        CollisionIndexDomain,
        CollisionIndexAllocationPolicy> CreatePolicies()
    {
        var policies = new Dictionary<
            CollisionIndexDomain,
            CollisionIndexAllocationPolicy>();

        Add(
            policies,
            CollisionIndexAllocationOwnership.SharedCatalog,
            CollisionIndexDomain.Plane,
            CollisionIndexDomain.Material);

        Add(
            policies,
            CollisionIndexAllocationOwnership.SourceOwned,
            CollisionIndexDomain.StaticModel,
            CollisionIndexDomain.BrushSide,
            CollisionIndexDomain.BrushEdge,
            CollisionIndexDomain.TriangleVertex,
            CollisionIndexDomain.TriangleIndex,
            CollisionIndexDomain.Brush,
            CollisionIndexDomain.BrushBounds,
            CollisionIndexDomain.BrushContents,
            CollisionIndexDomain.DynamicEntityDefinitionSlot0,
            CollisionIndexDomain.DynamicEntityDefinitionSlot1);

        Add(
            policies,
            CollisionIndexAllocationOwnership.CompilerAggregate,
            CollisionIndexDomain.BspNode,
            CollisionIndexDomain.Leaf,
            CollisionIndexDomain.LeafBrushNode,
            CollisionIndexDomain.LeafBrushReference,
            CollisionIndexDomain.LeafSurfaceReference,
            CollisionIndexDomain.TriangleWalkabilityPackedByte,
            CollisionIndexDomain.Border,
            CollisionIndexDomain.Partition,
            CollisionIndexDomain.AabbTreeNode,
            CollisionIndexDomain.StaticModelAabbNode);

        AddOne(
            policies,
            CollisionIndexDomain.CollisionModel,
            CollisionIndexAllocationOwnership.CompilerAggregate,
            CollisionIndexOrdinalAuthority.InlineModelIdentityPlan);

        Add(
            policies,
            CollisionIndexAllocationOwnership.RuntimeDerived,
            CollisionIndexDomain.DynamicEntityPoseSlot0,
            CollisionIndexDomain.DynamicEntityPoseSlot1,
            CollisionIndexDomain.DynamicEntityClientSlot0,
            CollisionIndexDomain.DynamicEntityClientSlot1,
            CollisionIndexDomain.DynamicEntityCollisionSlot0,
            CollisionIndexDomain.DynamicEntityCollisionSlot1);

        CollisionIndexDomain[] missing = Enum
            .GetValues<CollisionIndexDomain>()
            .Where(domain => !policies.ContainsKey(domain))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Collision allocation ownership policies are incomplete: " +
                string.Join(", ", missing));
        }

        return policies;
    }

    private static void Add(
        IDictionary<
            CollisionIndexDomain,
            CollisionIndexAllocationPolicy> policies,
        CollisionIndexAllocationOwnership ownership,
        params CollisionIndexDomain[] domains)
    {
        CollisionIndexOrdinalAuthority ordinalAuthority = ownership switch
        {
            CollisionIndexAllocationOwnership.SourceOwned or
                CollisionIndexAllocationOwnership.SharedCatalog =>
                CollisionIndexOrdinalAuthority.SourceContributionPlan,
            CollisionIndexAllocationOwnership.CompilerAggregate =>
                CollisionIndexOrdinalAuthority.CompleteSourceCompiler,
            CollisionIndexAllocationOwnership.RuntimeDerived =>
                CollisionIndexOrdinalAuthority.DynamicDefinitionCardinality,
            _ => throw new ArgumentOutOfRangeException(nameof(ownership))
        };
        foreach (CollisionIndexDomain domain in domains)
        {
            AddOne(policies, domain, ownership, ordinalAuthority);
        }
    }

    private static void AddOne(
        IDictionary<
            CollisionIndexDomain,
            CollisionIndexAllocationPolicy> policies,
        CollisionIndexDomain domain,
        CollisionIndexAllocationOwnership ownership,
        CollisionIndexOrdinalAuthority ordinalAuthority)
    {
        if (!policies.TryAdd(
                domain,
                new CollisionIndexAllocationPolicy(
                    domain,
                    ownership,
                    ordinalAuthority)))
        {
            throw new InvalidOperationException(
                $"Collision domain {domain} has duplicate allocation " +
                "ownership policies.");
        }
    }
}
