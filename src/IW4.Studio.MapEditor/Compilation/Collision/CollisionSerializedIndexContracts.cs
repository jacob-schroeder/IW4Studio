using System.Collections.ObjectModel;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Encoding of a ColMap array cardinality. Derived triangle payloads retain
/// the signed Int32 triangle-count authority but do not serialize their own
/// element count.
/// </summary>
public enum CollisionSerializedCardinalityEncoding
{
    SignedInt32 = 0,
    UnsignedInt16 = 1,
    TriangleIndexElements = 2,
    TriangleWalkabilityPackedBytes = 3
}

/// <summary>
/// Encoding of scalar values stored directly in a ColMap-owned array.
/// Structured record domains intentionally have no scalar element encoding.
/// </summary>
public enum CollisionSerializedElementEncoding
{
    UnsignedByte = 0,
    UnsignedInt16 = 1,
    UnsignedInt32 = 2
}

/// <summary>
/// Evidence-backed serialized width and capacity for one ColMap index domain.
/// This describes representability only; it does not claim that unresolved
/// topology, sentinel, or consumer semantics are valid.
/// </summary>
public sealed class CollisionIndexDomainSerializationPolicy
{
    internal CollisionIndexDomainSerializationPolicy(
        CollisionIndexDomain domain,
        CollisionSerializedCardinalityEncoding cardinalityEncoding,
        int maximumElementCount,
        int requiredElementCountMultiple = 1,
        CollisionSerializedElementEncoding? elementEncoding = null,
        CollisionIndexDomain? serializedElementTargetDomain = null)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (!Enum.IsDefined(cardinalityEncoding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardinalityEncoding));
        }
        if (maximumElementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumElementCount));
        if (requiredElementCountMultiple <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredElementCountMultiple));
        }
        if (elementEncoding is { } scalarEncoding &&
            !Enum.IsDefined(scalarEncoding))
        {
            throw new ArgumentOutOfRangeException(nameof(elementEncoding));
        }
        if (serializedElementTargetDomain is { } targetDomain &&
            !Enum.IsDefined(targetDomain))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedElementTargetDomain));
        }
        if (serializedElementTargetDomain is not null &&
            elementEncoding is null)
        {
            throw new ArgumentException(
                "A serialized element target requires a scalar element " +
                "encoding.",
                nameof(serializedElementTargetDomain));
        }

        Domain = domain;
        CardinalityEncoding = cardinalityEncoding;
        MaximumElementCount = maximumElementCount;
        RequiredElementCountMultiple = requiredElementCountMultiple;
        ElementEncoding = elementEncoding;
        SerializedElementTargetDomain = serializedElementTargetDomain;
    }

    public CollisionIndexDomain Domain { get; }
    public CollisionSerializedCardinalityEncoding CardinalityEncoding { get; }
    public int MaximumElementCount { get; }
    public int RequiredElementCountMultiple { get; }
    public CollisionSerializedElementEncoding? ElementEncoding { get; }

    /// <summary>
    /// Known direct target of scalar ordinal values. Null means either that
    /// the payload is not an ordinal, that its semantics remain open, or that
    /// it needs structured context. TriangleIndex is intentionally null:
    /// FirstVertSegment supplies a partition-local base before each ushort is
    /// resolved into the TriangleVertex domain.
    /// </summary>
    public CollisionIndexDomain? SerializedElementTargetDomain { get; }

    public ulong? MaximumSerializedElementValue =>
        ElementEncoding switch
        {
            CollisionSerializedElementEncoding.UnsignedByte =>
                byte.MaxValue,
            CollisionSerializedElementEncoding.UnsignedInt16 =>
                ushort.MaxValue,
            CollisionSerializedElementEncoding.UnsignedInt32 =>
                uint.MaxValue,
            null => null,
            _ => throw new InvalidOperationException(
                $"Unsupported collision element encoding {ElementEncoding}.")
        };

    public ulong? MaximumAddressableTargetElementCount =>
        SerializedElementTargetDomain is null
            ? null
            : checked(MaximumSerializedElementValue!.Value + 1);

    public void ValidateElementCount(int elementCount)
    {
        if (elementCount < 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));
        if (elementCount > MaximumElementCount)
        {
            throw new OverflowException(
                $"{Domain} contains {elementCount} elements, exceeding its " +
                $"{CardinalityEncoding} serialized capacity of " +
                $"{MaximumElementCount}.");
        }
        if (elementCount % RequiredElementCountMultiple != 0)
        {
            throw new ArgumentException(
                $"{Domain} element count {elementCount} must be a multiple " +
                $"of {RequiredElementCountMultiple}.",
                nameof(elementCount));
        }
    }

    public void ValidateSerializedElementValue(ulong value)
    {
        ulong maximum = MaximumSerializedElementValue ??
            throw new InvalidOperationException(
                $"{Domain} is a structured record domain, not a scalar " +
                "serialized-value domain.");
        if (value > maximum)
        {
            throw new OverflowException(
                $"{Domain} scalar value {value} exceeds its " +
                $"{ElementEncoding} serialized capacity of {maximum}.");
        }
    }

    public void ValidateSerializedTargetOrdinal(
        ulong ordinal,
        int targetElementCount)
    {
        if (SerializedElementTargetDomain is null)
        {
            throw new InvalidOperationException(
                $"{Domain} has no proven serialized ordinal target.");
        }
        if (targetElementCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetElementCount));
        }

        ulong maximumTargetElementCount =
            MaximumAddressableTargetElementCount ??
            throw new InvalidOperationException(
                $"{Domain} has no serialized target-capacity policy.");
        if ((ulong)targetElementCount > maximumTargetElementCount)
        {
            throw new OverflowException(
                $"{SerializedElementTargetDomain} contains " +
                $"{targetElementCount} elements, but {Domain} " +
                $"{ElementEncoding} ordinals can address at most " +
                $"{maximumTargetElementCount}.");
        }

        ValidateSerializedElementValue(ordinal);
        if (ordinal >= (ulong)targetElementCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                $"{Domain} ordinal {ordinal} is outside the " +
                $"{targetElementCount}-element " +
                $"{SerializedElementTargetDomain} domain.");
        }
    }
}

/// <summary>
/// Complete policy catalog for the currently known ColMap-owned domains.
/// Widths are taken from ClipMapAsset and ClipMapBodyEmitter. Narrow fields
/// nested inside structured records are not promoted to global cardinality
/// limits unless they serialize the owning table's count.
/// </summary>
public static class CollisionIndexDomainSerializationPolicies
{
    // ClipMapBodyEmitter validates both TriCount * 3 and the checked packed
    // walkability expression ((TriCount * 3 + 0x1f) >> 5) << 2. The latter
    // establishes the tightest representable signed-Int32 triangle count.
    private const int MaximumTriangleCount =
        (int.MaxValue - 0x1f) / 3;
    private const int MaximumTriangleIndexElementCount =
        MaximumTriangleCount * 3;
    private const int MaximumTriangleWalkabilityByteCount =
        ((MaximumTriangleIndexElementCount + 0x1f) >> 5) << 2;

    private static readonly IReadOnlyDictionary<
        CollisionIndexDomain,
        CollisionIndexDomainSerializationPolicy> Policies =
        new ReadOnlyDictionary<
            CollisionIndexDomain,
            CollisionIndexDomainSerializationPolicy>(
            CreatePolicies());

    public static IReadOnlyCollection<
        CollisionIndexDomainSerializationPolicy> All =>
        Array.AsReadOnly(Policies.Values
            .OrderBy(value => value.Domain)
            .ToArray());

    public static CollisionIndexDomainSerializationPolicy GetRequired(
        CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return Policies.TryGetValue(domain, out var policy)
            ? policy
            : throw new InvalidOperationException(
                $"Collision domain {domain} has no serialized-width policy.");
    }

    private static Dictionary<
        CollisionIndexDomain,
        CollisionIndexDomainSerializationPolicy> CreatePolicies()
    {
        var policies = new Dictionary<
            CollisionIndexDomain,
            CollisionIndexDomainSerializationPolicy>();

        AddInt32(policies, CollisionIndexDomain.Plane);
        AddInt32(policies, CollisionIndexDomain.StaticModel);
        // The root count is signed Int32, but every proven collision
        // material consumer loads a UInt16 ordinal.
        Add(
            policies,
            new(
                CollisionIndexDomain.Material,
                CollisionSerializedCardinalityEncoding.SignedInt32,
                ushort.MaxValue + 1));
        AddInt32(policies, CollisionIndexDomain.BrushSide);
        AddInt32(
            policies,
            CollisionIndexDomain.BrushEdge,
            CollisionSerializedElementEncoding.UnsignedByte);
        AddInt32(policies, CollisionIndexDomain.BspNode);
        AddInt32(policies, CollisionIndexDomain.Leaf);
        AddInt32(policies, CollisionIndexDomain.LeafBrushNode);
        AddInt32(
            policies,
            CollisionIndexDomain.LeafBrushReference,
            CollisionSerializedElementEncoding.UnsignedInt16,
            CollisionIndexDomain.Brush);
        AddInt32(
            policies,
            CollisionIndexDomain.LeafSurfaceReference,
            CollisionSerializedElementEncoding.UnsignedInt32);
        AddInt32(policies, CollisionIndexDomain.TriangleVertex);
        Add(
            policies,
            new(
                CollisionIndexDomain.TriangleIndex,
                CollisionSerializedCardinalityEncoding
                    .TriangleIndexElements,
                MaximumTriangleIndexElementCount,
                requiredElementCountMultiple: 3,
                elementEncoding:
                    CollisionSerializedElementEncoding.UnsignedInt16));
        Add(
            policies,
            new(
                CollisionIndexDomain.TriangleWalkabilityPackedByte,
                CollisionSerializedCardinalityEncoding
                    .TriangleWalkabilityPackedBytes,
                MaximumTriangleWalkabilityByteCount,
                requiredElementCountMultiple: sizeof(uint),
                elementEncoding:
                    CollisionSerializedElementEncoding.UnsignedByte));
        AddInt32(policies, CollisionIndexDomain.Border);
        AddInt32(policies, CollisionIndexDomain.Partition);
        AddInt32(policies, CollisionIndexDomain.AabbTreeNode);
        AddInt32(policies, CollisionIndexDomain.CollisionModel);

        // ClipMapAsset.NumBrushes is the shared UInt16 authority for all three
        // ordinal-parallel brush arrays.
        AddUInt16(policies, CollisionIndexDomain.Brush);
        AddUInt16(policies, CollisionIndexDomain.BrushBounds);
        AddUInt16(
            policies,
            CollisionIndexDomain.BrushContents,
            CollisionSerializedElementEncoding.UnsignedInt32);

        AddUInt16(policies, CollisionIndexDomain.StaticModelAabbNode);

        // ClipMapAsset.DynEntCount[slot] is the shared UInt16 authority for
        // definitions and all three same-slot runtime arrays.
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityDefinitionSlot0);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityDefinitionSlot1);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityPoseSlot0);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityPoseSlot1);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityClientSlot0);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityClientSlot1);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityCollisionSlot0);
        AddUInt16(
            policies,
            CollisionIndexDomain.DynamicEntityCollisionSlot1);

        CollisionIndexDomain[] missing = Enum
            .GetValues<CollisionIndexDomain>()
            .Where(domain => !policies.ContainsKey(domain))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Serialized collision index policies are incomplete: " +
                string.Join(", ", missing));
        }

        return policies;
    }

    private static void AddInt32(
        IDictionary<
            CollisionIndexDomain,
            CollisionIndexDomainSerializationPolicy> policies,
        CollisionIndexDomain domain,
        CollisionSerializedElementEncoding? elementEncoding = null,
        CollisionIndexDomain? targetDomain = null) =>
        Add(
            policies,
            new(
                domain,
                CollisionSerializedCardinalityEncoding.SignedInt32,
                int.MaxValue,
                elementEncoding: elementEncoding,
                serializedElementTargetDomain: targetDomain));

    private static void AddUInt16(
        IDictionary<
            CollisionIndexDomain,
            CollisionIndexDomainSerializationPolicy> policies,
        CollisionIndexDomain domain,
        CollisionSerializedElementEncoding? elementEncoding = null) =>
        Add(
            policies,
            new(
                domain,
                CollisionSerializedCardinalityEncoding.UnsignedInt16,
                ushort.MaxValue,
                elementEncoding: elementEncoding));

    private static void Add(
        IDictionary<
            CollisionIndexDomain,
            CollisionIndexDomainSerializationPolicy> policies,
        CollisionIndexDomainSerializationPolicy policy)
    {
        if (!policies.TryAdd(policy.Domain, policy))
        {
            throw new InvalidOperationException(
                $"Collision domain {policy.Domain} has duplicate serialized " +
                "policies.");
        }
    }
}

/// <summary>
/// Establishes aggregate triangle payload shape after source-owned ranges
/// have been assigned. Walkability is one packed stream for the whole ColMap.
/// Triangle ushort values are partition-relative through FirstVertSegment;
/// their effective vertex bounds are validated by the structured-record
/// contract rather than by imposing a false 65,536-row global vertex limit.
/// </summary>
internal static class CollisionTriangleAggregateIndexValidator
{
    public static void ValidateAndAddDerivedDomains(
        IDictionary<CollisionIndexDomain, int> domainCounts)
    {
        ArgumentNullException.ThrowIfNull(domainCounts);

        domainCounts.TryGetValue(
            CollisionIndexDomain.TriangleIndex,
            out int triangleIndexElementCount);
        if (triangleIndexElementCount == 0)
            return;

        domainCounts.TryGetValue(
            CollisionIndexDomain.TriangleVertex,
            out int triangleVertexCount);
        if (triangleVertexCount == 0)
        {
            throw new ArgumentException(
                "Triangle index elements require a triangle-vertex target " +
                "domain.");
        }

        int packedByteCount = checked(
            ((triangleIndexElementCount + 0x1f) >> 5) << 2);
        CollisionIndexDomainSerializationPolicies
            .GetRequired(
                CollisionIndexDomain.TriangleWalkabilityPackedByte)
            .ValidateElementCount(packedByteCount);
        domainCounts.Add(
            CollisionIndexDomain.TriangleWalkabilityPackedByte,
            packedByteCount);
    }
}

/// <summary>
/// Validates ordinal joins for ColMap arrays whose parallel relationship is
/// explicit in the serialized root. Brush rows are source-aware: equal totals
/// are insufficient when source ranges differ. Dynamic runtime domains are
/// derived from their same-slot definition count and therefore have no
/// source-owned mappings.
/// </summary>
internal static class CollisionParallelIndexDomainValidator
{
    private static readonly CollisionIndexDomain[] BrushDomains =
    [
        CollisionIndexDomain.Brush,
        CollisionIndexDomain.BrushBounds,
        CollisionIndexDomain.BrushContents
    ];

    private static readonly CollisionIndexDomain[][] DynamicEntityDomains =
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

    public static void Validate(CollisionSourceIndexPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ValidateGroup(plan, "brush rows", BrushDomains);
        for (int slot = 0; slot < DynamicEntityDomains.Length; slot++)
        {
            ValidateRuntimeDerivedGroup(
                plan,
                slot,
                DynamicEntityDomains[slot]);
        }
    }

    private static void ValidateRuntimeDerivedGroup(
        CollisionSourceIndexPlan plan,
        int slot,
        IReadOnlyList<CollisionIndexDomain> domains)
    {
        int expectedCount = plan.GetDomainCount(domains[0]);
        for (int index = 1; index < domains.Count; index++)
        {
            CollisionIndexDomain domain = domains[index];
            int actualCount = plan.GetDomainCount(domain);
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Collision dynamic-entity slot {slot} runtime domain " +
                    $"{domain} contains {actualCount} rows instead of its " +
                    $"{expectedCount}-row definition count.");
            }
            if (plan.Mappings.Any(mapping => mapping.Domain == domain))
            {
                throw new InvalidOperationException(
                    $"Collision dynamic-entity slot {slot} runtime domain " +
                    $"{domain} cannot have source-owned mappings.");
            }
        }
    }

    private static void ValidateGroup(
        CollisionSourceIndexPlan plan,
        string groupName,
        IReadOnlyList<CollisionIndexDomain> domains)
    {
        CollisionIndexDomain[] present = domains
            .Where(domain => plan.GetDomainCount(domain) != 0)
            .ToArray();
        if (present.Length == 0)
            return;
        if (present.Length != domains.Count)
        {
            CollisionIndexDomain[] missing = domains
                .Except(present)
                .ToArray();
            throw new ArgumentException(
                $"Collision {groupName} must contribute every parallel " +
                $"domain; missing {string.Join(", ", missing)}.");
        }

        CollisionSourceIndexMapping[] expected =
            MappingsFor(plan, domains[0]);
        for (int index = 1; index < domains.Count; index++)
        {
            CollisionSourceIndexMapping[] actual =
                MappingsFor(plan, domains[index]);
            if (expected.Length != actual.Length)
            {
                throw new ArgumentException(
                    $"Collision {groupName} has unequal source ownership " +
                    $"between {domains[0]} and {domains[index]}.");
            }

            for (int mappingIndex = 0;
                 mappingIndex < expected.Length;
                 mappingIndex++)
            {
                CollisionSourceIndexMapping left =
                    expected[mappingIndex];
                CollisionSourceIndexMapping right =
                    actual[mappingIndex];
                if (left.SourceObjectId != right.SourceObjectId ||
                    left.EmittedRange != right.EmittedRange)
                {
                    throw new ArgumentException(
                        $"Collision {groupName} must preserve identical " +
                        $"source ordinals across {domains[0]} and " +
                        $"{domains[index]}.");
                }
            }
        }
    }

    private static CollisionSourceIndexMapping[] MappingsFor(
        CollisionSourceIndexPlan plan,
        CollisionIndexDomain domain) =>
        plan.Mappings
            .Where(mapping => mapping.Domain == domain)
            .OrderBy(mapping => mapping.EmittedRange.Start)
            .ToArray();
}
