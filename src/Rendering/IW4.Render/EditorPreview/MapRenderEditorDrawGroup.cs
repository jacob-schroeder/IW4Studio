using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Immutable editor draw group. Keeping authored passes inside this object makes
/// queue sorting unable to interleave passes from different source submissions.
/// </summary>
public sealed class MapRenderEditorDrawGroup<TPass>
{
    private readonly TPass[] _authoredPasses;
    private readonly IReadOnlyList<TPass> _authoredPassView;
    private readonly Vector3? _sortCenter;
    private readonly float? _explicitDepth;

    private MapRenderEditorDrawGroup(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        IReadOnlyList<TPass> authoredPasses,
        Vector3? sortCenter,
        float? explicitDepth,
        long? cameraIndependentSortKey)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(authoredPasses);
        if (authoredPasses.Count == 0)
        {
            throw new ArgumentException(
                "An editor draw group must retain at least one authored pass.",
                nameof(authoredPasses));
        }
        if (sortCenter.HasValue == explicitDepth.HasValue)
        {
            throw new ArgumentException(
                "An editor draw group requires exactly one sort center or explicit depth.");
        }
        if (sortCenter is { } center && !IsFinite(center))
            throw new ArgumentOutOfRangeException(nameof(sortCenter));
        if (explicitDepth is { } depth && !float.IsFinite(depth))
            throw new ArgumentOutOfRangeException(nameof(explicitDepth));

        SourceOrdinal = sourceOrdinal;
        Classification = classification;
        _authoredPasses = authoredPasses.ToArray();
        _authoredPassView = Array.AsReadOnly(_authoredPasses);
        _sortCenter = sortCenter;
        _explicitDepth = explicitDepth;
        CameraIndependentSortKey = cameraIndependentSortKey;
    }

    private MapRenderEditorDrawGroup(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        TPass[] authoredPasses,
        IReadOnlyList<TPass> authoredPassView,
        Vector3? sortCenter,
        float? explicitDepth,
        long? cameraIndependentSortKey)
    {
        SourceOrdinal = sourceOrdinal;
        Classification = classification;
        _authoredPasses = authoredPasses;
        _authoredPassView = authoredPassView;
        _sortCenter = sortCenter;
        _explicitDepth = explicitDepth;
        CameraIndependentSortKey = cameraIndependentSortKey;
    }

    public long SourceOrdinal { get; }

    public MapRenderEditorDrawBucketClassification Classification { get; }

    public MapRenderEditorDrawBucket Bucket => Classification.Bucket;

    public IReadOnlyList<TPass> AuthoredPasses => _authoredPassView;

    internal ReadOnlySpan<TPass> AuthoredPassSpan => _authoredPasses;

    public Vector3? SortCenter => _sortCenter;

    public float? ExplicitDepth => _explicitDepth;

    /// <summary>
    /// Optional cached state/material ordering key for opaque and cutout
    /// submissions. Translucent ordering never consumes this value.
    /// </summary>
    public long? CameraIndependentSortKey { get; }

    /// <summary>
    /// Reuses this immutable group's authored-pass storage while publishing it
    /// at a different stable queue ordinal. Progressive resource admission
    /// uses this to preserve the eager builder's exact source ordering without
    /// rebuilding bounds, pass arrays, or classifications for every resident
    /// static group.
    /// </summary>
    internal MapRenderEditorDrawGroup<TPass> WithSourceOrdinal(
        long sourceOrdinal) =>
        sourceOrdinal == SourceOrdinal
            ? this
            : new(
                sourceOrdinal,
                Classification,
                _authoredPasses,
                _authoredPassView,
                _sortCenter,
                _explicitDepth,
                CameraIndependentSortKey);

    public static MapRenderEditorDrawGroup<TPass> FromCenter(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        IReadOnlyList<TPass> authoredPasses,
        Vector3 sortCenter,
        long? cameraIndependentSortKey = null) =>
        new(
            sourceOrdinal,
            classification,
            authoredPasses,
            sortCenter,
            explicitDepth: null,
            cameraIndependentSortKey: cameraIndependentSortKey);

    public static MapRenderEditorDrawGroup<TPass> FromBounds(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        IReadOnlyList<TPass> authoredPasses,
        RenderBounds bounds,
        long? cameraIndependentSortKey = null)
    {
        if (!bounds.IsValid || !IsFinite(bounds.Min) || !IsFinite(bounds.Max))
            throw new ArgumentException("Editor draw bounds must be finite and valid.", nameof(bounds));

        return FromCenter(
            sourceOrdinal,
            classification,
            authoredPasses,
            bounds.Center,
            cameraIndependentSortKey);
    }

    public static MapRenderEditorDrawGroup<TPass> FromExplicitDepth(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        IReadOnlyList<TPass> authoredPasses,
        float explicitDepth,
        long? cameraIndependentSortKey = null) =>
        new(
            sourceOrdinal,
            classification,
            authoredPasses,
            sortCenter: null,
            explicitDepth: explicitDepth,
            cameraIndependentSortKey: cameraIndependentSortKey);

    internal float ResolveDepth(Vector3 cameraPosition, Vector3 cameraForward) =>
        _explicitDepth ?? Vector3.Dot(_sortCenter!.Value - cameraPosition, cameraForward);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
