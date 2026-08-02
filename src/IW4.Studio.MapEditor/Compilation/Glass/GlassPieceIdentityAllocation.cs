using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;

namespace IW4.Studio.MapEditor.Compilation.Glass;

/// <summary>
/// Stable semantic identity of one authored glass piece. It is deliberately
/// independent of emitted row ordinals, collision handles, and runtime
/// pointers.
/// </summary>
public sealed record GlassPieceSourceId
{
    public GlassPieceSourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A glass-piece source identity is required.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Stable identity of one semantic glass-name membership group. This does
/// not allocate or presume an IW4 script-string representation.
/// </summary>
public sealed record GlassMembershipGroupSourceId
{
    public GlassMembershipGroupSourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A glass membership-group source identity is required.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Semantic piece identity and the collision brushes owned by that piece.
/// One piece may own multiple brushes; one brush may have at most one piece
/// owner because CBrush stores a single glass-piece handle.
/// </summary>
public sealed class GlassPieceIdentitySource
{
    private readonly IReadOnlyList<int> _collisionBrushOrdinals;

    public GlassPieceIdentitySource(
        GlassPieceSourceId sourceId,
        IEnumerable<int>? collisionBrushOrdinals = null)
    {
        SourceId = sourceId ??
            throw new ArgumentNullException(nameof(sourceId));
        _collisionBrushOrdinals = Array.AsReadOnly(
            collisionBrushOrdinals?.ToArray() ?? []);
    }

    public GlassPieceSourceId SourceId { get; }

    public IReadOnlyList<int> CollisionBrushOrdinals =>
        _collisionBrushOrdinals;
}

/// <summary>
/// Semantic group membership expressed exclusively through stable source
/// identities. The allocator resolves these identities only after final
/// piece ordinals are assigned.
/// </summary>
public sealed class GlassMembershipGroupSource
{
    private readonly IReadOnlyList<GlassPieceSourceId> _pieceSourceIds;

    public GlassMembershipGroupSource(
        GlassMembershipGroupSourceId sourceId,
        IEnumerable<GlassPieceSourceId> pieceSourceIds)
    {
        SourceId = sourceId ??
            throw new ArgumentNullException(nameof(sourceId));
        ArgumentNullException.ThrowIfNull(pieceSourceIds);
        _pieceSourceIds = Array.AsReadOnly(pieceSourceIds.ToArray());
    }

    public GlassMembershipGroupSourceId SourceId { get; }

    public IReadOnlyList<GlassPieceSourceId> PieceSourceIds =>
        _pieceSourceIds;
}

/// <summary>
/// Complete semantic input for cross-root glass identity allocation. It owns
/// no FxMap or GameMap payload fields.
/// </summary>
public sealed class GlassPieceIdentityAllocationRequest
{
    private readonly IReadOnlyList<GlassPieceIdentitySource> _pieces;
    private readonly IReadOnlyList<GlassMembershipGroupSource>
        _membershipGroups;

    public GlassPieceIdentityAllocationRequest(
        int collisionBrushCount,
        IEnumerable<GlassPieceIdentitySource> pieces,
        IEnumerable<GlassMembershipGroupSource>? membershipGroups = null)
    {
        if (collisionBrushCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionBrushCount));
        }

        ArgumentNullException.ThrowIfNull(pieces);
        CollisionBrushCount = collisionBrushCount;
        _pieces = Array.AsReadOnly(pieces.ToArray());
        _membershipGroups = Array.AsReadOnly(
            membershipGroups?.ToArray() ?? []);
    }

    public int CollisionBrushCount { get; }

    public IReadOnlyList<GlassPieceIdentitySource> Pieces => _pieces;

    public IReadOnlyList<GlassMembershipGroupSource> MembershipGroups =>
        _membershipGroups;
}

/// <summary>
/// One deterministic cross-root identity assignment.
/// </summary>
public sealed class GlassPieceIdentityAllocation
{
    private readonly IReadOnlyList<int> _collisionBrushOrdinals;

    internal GlassPieceIdentityAllocation(
        GlassPieceSourceId sourceId,
        GlassPieceOrdinal sharedOrdinal,
        ColMapGlassPieceHandle collisionHandle,
        IEnumerable<int> collisionBrushOrdinals)
    {
        SourceId = sourceId;
        SharedOrdinal = sharedOrdinal;
        CollisionHandle = collisionHandle;
        _collisionBrushOrdinals = Array.AsReadOnly(
            collisionBrushOrdinals.ToArray());
    }

    public GlassPieceSourceId SourceId { get; }

    /// <summary>
    /// Shared zero-based row in FxMap initial pieces and GameMapMp pieces.
    /// </summary>
    public GlassPieceOrdinal SharedOrdinal { get; }

    /// <summary>
    /// One-based CBrush handle for this piece.
    /// </summary>
    public ColMapGlassPieceHandle CollisionHandle { get; }

    public IReadOnlyList<int> CollisionBrushOrdinals =>
        _collisionBrushOrdinals;
}

/// <summary>
/// One resolved semantic membership group. Member ordinals are emitted in
/// ascending shared-piece order for deterministic output.
/// </summary>
public sealed class GlassMembershipGroupAllocation
{
    private readonly IReadOnlyList<GlassPieceOrdinal> _pieceOrdinals;

    internal GlassMembershipGroupAllocation(
        GlassMembershipGroupSourceId sourceId,
        IEnumerable<GlassPieceOrdinal> pieceOrdinals)
    {
        SourceId = sourceId;
        _pieceOrdinals = Array.AsReadOnly(pieceOrdinals.ToArray());
    }

    public GlassMembershipGroupSourceId SourceId { get; }

    public IReadOnlyList<GlassPieceOrdinal> PieceOrdinals =>
        _pieceOrdinals;
}

/// <summary>
/// Immutable source-to-ordinal allocation shared by FxMap, GameMapMp, and
/// ColMap. This is an identity contract only; it authorizes no non-empty
/// serialized glass payload.
/// </summary>
public sealed class GlassPieceIdentityAllocationPlan
{
    private readonly IReadOnlyList<GlassPieceIdentityAllocation> _pieces;
    private readonly IReadOnlyList<GlassMembershipGroupAllocation>
        _membershipGroups;
    private readonly IReadOnlyList<ColMapGlassPieceHandle>
        _collisionBrushHandles;
    private readonly IReadOnlyDictionary<
        GlassPieceSourceId,
        GlassPieceIdentityAllocation> _piecesBySourceId;

    internal GlassPieceIdentityAllocationPlan(
        IEnumerable<GlassPieceIdentityAllocation> pieces,
        IEnumerable<GlassMembershipGroupAllocation> membershipGroups,
        IEnumerable<ColMapGlassPieceHandle> collisionBrushHandles)
    {
        GlassPieceIdentityAllocation[] pieceCopy = pieces.ToArray();
        _pieces = Array.AsReadOnly(pieceCopy);
        _membershipGroups = Array.AsReadOnly(
            membershipGroups.ToArray());
        _collisionBrushHandles = Array.AsReadOnly(
            collisionBrushHandles.ToArray());
        _piecesBySourceId = new ReadOnlyDictionary<
            GlassPieceSourceId,
            GlassPieceIdentityAllocation>(
                pieceCopy.ToDictionary(value => value.SourceId));
    }

    public int PieceCount => _pieces.Count;

    public int CollisionBrushCount => _collisionBrushHandles.Count;

    public IReadOnlyList<GlassPieceIdentityAllocation> Pieces => _pieces;

    public IReadOnlyList<GlassMembershipGroupAllocation>
        MembershipGroups => _membershipGroups;

    /// <summary>
    /// Complete ColMap brush-handle projection, including zero-valued
    /// no-glass rows.
    /// </summary>
    public IReadOnlyList<ColMapGlassPieceHandle>
        CollisionBrushHandles => _collisionBrushHandles;

    public GlassPieceIdentityAllocation GetRequiredPiece(
        GlassPieceSourceId sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        return _piecesBySourceId.TryGetValue(
            sourceId,
            out GlassPieceIdentityAllocation? allocation)
                ? allocation
                : throw new KeyNotFoundException(
                    $"Glass piece '{sourceId}' was not allocated.");
    }
}

/// <summary>
/// Allocates deterministic dense glass-piece identity without constructing
/// any IW4 glass geometry or gameplay payload.
/// </summary>
public static class GlassPieceIdentityAllocator
{
    public const string CompilerIdentity =
        "iw4-studio.glass.identity.stable-source-shared-ordinal-col-handle@1";

    public static GlassPieceIdentityAllocationPlan Allocate(
        GlassPieceIdentityAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GlassPieceIdentitySource[] pieces = request.Pieces.ToArray();
        GlassMembershipGroupSource[] groups =
            request.MembershipGroups.ToArray();
        RequireNonNullEntries(pieces, nameof(request.Pieces));
        RequireNonNullEntries(groups, nameof(request.MembershipGroups));

        if (pieces.Length > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"At most {ushort.MaxValue} glass pieces can receive " +
                "one-based collision handles.",
                nameof(request));
        }

        GlassPieceIdentitySource[] orderedPieces = pieces
            .OrderBy(
                value => value.SourceId.Value,
                StringComparer.Ordinal)
            .ToArray();
        RequireUniquePieceIds(orderedPieces);

        var bySourceId = new Dictionary<
            GlassPieceSourceId,
            GlassPieceIdentityAllocation>();
        var brushOwners = new Dictionary<int, GlassPieceSourceId>();
        var collisionHandles = new ColMapGlassPieceHandle[
            request.CollisionBrushCount];
        var allocations = new GlassPieceIdentityAllocation[
            orderedPieces.Length];

        for (int ordinal = 0; ordinal < orderedPieces.Length; ordinal++)
        {
            GlassPieceIdentitySource source = orderedPieces[ordinal];
            int[] brushOrdinals = source.CollisionBrushOrdinals
                .Order()
                .ToArray();
            RequireValidBrushOwnership(
                source.SourceId,
                brushOrdinals,
                request.CollisionBrushCount,
                brushOwners);

            var sharedOrdinal =
                new GlassPieceOrdinal(checked((ushort)ordinal));
            var collisionHandle =
                new ColMapGlassPieceHandle(
                    checked((ushort)(ordinal + 1)));
            var allocation = new GlassPieceIdentityAllocation(
                source.SourceId,
                sharedOrdinal,
                collisionHandle,
                brushOrdinals);
            allocations[ordinal] = allocation;
            bySourceId.Add(source.SourceId, allocation);

            foreach (int brushOrdinal in brushOrdinals)
                collisionHandles[brushOrdinal] = collisionHandle;
        }

        GlassMembershipGroupAllocation[] membershipAllocations =
            AllocateMemberships(groups, bySourceId);
        return new GlassPieceIdentityAllocationPlan(
            allocations,
            membershipAllocations,
            collisionHandles);
    }

    private static GlassMembershipGroupAllocation[] AllocateMemberships(
        IEnumerable<GlassMembershipGroupSource> sources,
        IReadOnlyDictionary<
            GlassPieceSourceId,
            GlassPieceIdentityAllocation> piecesBySourceId)
    {
        GlassMembershipGroupSource[] ordered = sources
            .OrderBy(
                value => value.SourceId.Value,
                StringComparer.Ordinal)
            .ToArray();
        for (int index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].SourceId ==
                ordered[index].SourceId)
            {
                throw new ArgumentException(
                    $"Glass membership group " +
                    $"'{ordered[index].SourceId}' is duplicated.");
            }
        }

        var results = new GlassMembershipGroupAllocation[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            GlassMembershipGroupSource group = ordered[index];
            if (group.PieceSourceIds.Count > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"Glass membership group '{group.SourceId}' exceeds " +
                    $"the {ushort.MaxValue} member limit.");
            }

            var seen = new HashSet<GlassPieceSourceId>();
            var ordinals = new GlassPieceOrdinal[
                group.PieceSourceIds.Count];
            for (int memberIndex = 0;
                 memberIndex < group.PieceSourceIds.Count;
                 memberIndex++)
            {
                GlassPieceSourceId? pieceSourceId =
                    group.PieceSourceIds[memberIndex];
                if (pieceSourceId is null)
                {
                    throw new ArgumentException(
                        $"Glass membership group '{group.SourceId}' " +
                        "contains a null piece source identity.");
                }

                if (!seen.Add(pieceSourceId))
                {
                    throw new ArgumentException(
                        $"Glass membership group '{group.SourceId}' " +
                        $"contains duplicate piece '{pieceSourceId}'.");
                }

                if (!piecesBySourceId.TryGetValue(
                        pieceSourceId,
                        out GlassPieceIdentityAllocation? piece))
                {
                    throw new ArgumentException(
                        $"Glass membership group '{group.SourceId}' " +
                        $"references unknown piece '{pieceSourceId}'.");
                }

                ordinals[memberIndex] = piece.SharedOrdinal;
            }

            results[index] = new GlassMembershipGroupAllocation(
                group.SourceId,
                ordinals.OrderBy(value => value.Value));
        }

        return results;
    }

    private static void RequireUniquePieceIds(
        IReadOnlyList<GlassPieceIdentitySource> pieces)
    {
        for (int index = 1; index < pieces.Count; index++)
        {
            if (pieces[index - 1].SourceId == pieces[index].SourceId)
            {
                throw new ArgumentException(
                    $"Glass piece '{pieces[index].SourceId}' is duplicated.");
            }
        }
    }

    private static void RequireValidBrushOwnership(
        GlassPieceSourceId sourceId,
        IReadOnlyList<int> brushOrdinals,
        int brushCount,
        IDictionary<int, GlassPieceSourceId> brushOwners)
    {
        int? previous = null;
        foreach (int brushOrdinal in brushOrdinals)
        {
            if (brushOrdinal < 0 || brushOrdinal >= brushCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(brushOrdinals),
                    $"Glass piece '{sourceId}' claims collision brush " +
                    $"{brushOrdinal}, outside [0, {brushCount}).");
            }

            if (previous == brushOrdinal)
            {
                throw new ArgumentException(
                    $"Glass piece '{sourceId}' claims collision brush " +
                    $"{brushOrdinal} more than once.");
            }

            if (!brushOwners.TryAdd(brushOrdinal, sourceId))
            {
                throw new ArgumentException(
                    $"Collision brush {brushOrdinal} is claimed by glass " +
                    $"pieces '{brushOwners[brushOrdinal]}' and " +
                    $"'{sourceId}'.");
            }

            previous = brushOrdinal;
        }
    }

    private static void RequireNonNullEntries<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        if (values.Any(value => value is null))
        {
            throw new ArgumentException(
                "The collection cannot contain null entries.",
                parameterName);
        }
    }
}
