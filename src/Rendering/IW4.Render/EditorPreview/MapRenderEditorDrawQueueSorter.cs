using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Pure host-preview queue ordering. Opaque and cutout groups use an optional
/// cached state/material key and otherwise retain source order; translucent
/// groups use back-to-front view depth and stable source ordinal ties. The
/// original group objects are returned, so authored pass sequences remain
/// contiguous.
/// </summary>
public static class MapRenderEditorDrawQueueSorter
{
    public static IReadOnlyList<MapRenderEditorDrawGroup<TPass>> Sort<TPass>(
        IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups,
        Vector3 cameraPosition,
        Vector3 cameraForward)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ValidateCamera(cameraPosition, cameraForward);

        // The renderer replaces its draw-group array whenever a scene is
        // loaded. A weak identity cache therefore follows that exact scene
        // lifetime without retaining an old scene. QueueCache still validates
        // every group reference and its structural ordering fields so callers
        // that pass a mutable IReadOnlyList cannot receive a stale ordering.
        QueueCache<TPass> cache = CacheStore<TPass>.ByGroupList.GetValue(
            groups,
            static _ => new QueueCache<TPass>());
        return cache.Sort(groups, cameraPosition, cameraForward);
    }

    /// <summary>
    /// Allocation-free moving-camera path for a renderer-owned immutable
    /// group array. The returned view is frame-local and is overwritten by a
    /// later call for the same array identity.
    /// </summary>
    internal static IReadOnlyList<MapRenderEditorDrawGroup<TPass>>
        SortImmutableFrame<TPass>(
            MapRenderEditorDrawGroup<TPass>[] groups,
            Vector3 cameraPosition,
            Vector3 cameraForward)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ValidateCamera(cameraPosition, cameraForward);
        QueueCache<TPass> cache = CacheStore<TPass>.ByGroupList.GetValue(
            groups,
            static _ => new QueueCache<TPass>());
        return cache.SortImmutableFrame(
            groups,
            cameraPosition,
            cameraForward);
    }

    private static void ValidateCamera(
        Vector3 cameraPosition,
        Vector3 cameraForward)
    {
        if (!IsFinite(cameraPosition))
            throw new ArgumentOutOfRangeException(nameof(cameraPosition));

        float forwardLengthSquared = cameraForward.LengthSquared();
        if (!IsFinite(cameraForward) ||
            !float.IsFinite(forwardLengthSquared) ||
            forwardLengthSquared <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraForward));
        }
    }

    private static int Compare<TPass>(
        QueueEntry<TPass> left,
        QueueEntry<TPass> right)
    {
        int comparison = ((int)left.Group.Bucket).CompareTo(
            (int)right.Group.Bucket);
        if (comparison != 0)
            return comparison;

        if (left.Group.Bucket == MapRenderEditorDrawBucket.Translucent)
        {
            comparison = right.Depth.CompareTo(left.Depth);
            if (comparison != 0)
                return comparison;
        }
        else
        {
            comparison = Nullable.Compare(
                left.Group.CameraIndependentSortKey,
                right.Group.CameraIndependentSortKey);
            if (comparison != 0)
                return comparison;
        }

        comparison = left.Group.SourceOrdinal.CompareTo(right.Group.SourceOrdinal);
        return comparison != 0
            ? comparison
            : left.InputOrdinal.CompareTo(right.InputOrdinal);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static class CacheStore<TPass>
    {
        internal static ConditionalWeakTable<object, QueueCache<TPass>>
            ByGroupList
        { get; } = new();
    }

    private sealed class QueueCache<TPass>
    {
        private readonly object _gate = new();
        private QueueLayoutEntry<TPass>[] _layout = [];
        private MapRenderEditorDrawGroup<TPass>[] _cameraIndependent = [];
        private QueueEntry<TPass>[] _translucentTemplates = [];
        private QueueEntry<TPass>[] _translucentScratch = [];
        private ReadOnlyCollection<MapRenderEditorDrawGroup<TPass>>?
            _cameraIndependentResult;
        private CameraSortKey? _lastCamera;
        private ReadOnlyCollection<MapRenderEditorDrawGroup<TPass>>?
            _lastResult;
        private MapRenderEditorDrawGroup<TPass>[] _frameResult = [];
        private ReadOnlyCollection<MapRenderEditorDrawGroup<TPass>>?
            _frameResultView;
        private CameraSortKey? _lastFrameCamera;
        private bool _initialized;

        internal IReadOnlyList<MapRenderEditorDrawGroup<TPass>> Sort(
            IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups,
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            lock (_gate)
            {
                EnsureCurrentLayout(groups);
                if (_translucentTemplates.Length == 0)
                    return _cameraIndependentResult!;

                var camera = new CameraSortKey(
                    cameraPosition,
                    cameraForward);
                if (_lastCamera == camera && _lastResult is not null)
                    return _lastResult;

                for (int index = 0;
                     index < _translucentTemplates.Length;
                     index++)
                {
                    QueueEntry<TPass> template =
                        _translucentTemplates[index];
                    float depth = template.Group.ResolveDepth(
                        cameraPosition,
                        cameraForward);
                    if (!float.IsFinite(depth))
                    {
                        throw new ArgumentException(
                            $"Editor draw group {template.InputOrdinal} produced a non-finite sort depth.",
                            nameof(groups));
                    }

                    _translucentScratch[index] = template with
                    {
                        Depth = depth
                    };
                }

                Array.Sort(
                    _translucentScratch,
                    QueueEntryComparison<TPass>.Instance);
                var result = new MapRenderEditorDrawGroup<TPass>[
                    _layout.Length];
                _cameraIndependent.CopyTo(result, 0);
                for (int index = 0;
                     index < _translucentScratch.Length;
                     index++)
                {
                    result[_cameraIndependent.Length + index] =
                        _translucentScratch[index].Group;
                }

                _lastCamera = camera;
                _lastResult = Array.AsReadOnly(result);
                return _lastResult;
            }
        }

        internal IReadOnlyList<MapRenderEditorDrawGroup<TPass>>
            SortImmutableFrame(
                MapRenderEditorDrawGroup<TPass>[] groups,
                Vector3 cameraPosition,
                Vector3 cameraForward)
        {
            lock (_gate)
            {
                if (!_initialized)
                    Rebuild(groups);
                if (_translucentTemplates.Length == 0)
                    return _cameraIndependentResult!;

                var camera = new CameraSortKey(
                    cameraPosition,
                    cameraForward);
                if (_lastFrameCamera == camera &&
                    _frameResultView is not null)
                {
                    return _frameResultView;
                }

                ResolveAndSortTranslucent(
                    groups,
                    cameraPosition,
                    cameraForward);
                for (int index = 0;
                     index < _translucentScratch.Length;
                     index++)
                {
                    _frameResult[_cameraIndependent.Length + index] =
                        _translucentScratch[index].Group;
                }
                _lastFrameCamera = camera;
                return _frameResultView!;
            }
        }

        private void ResolveAndSortTranslucent(
            IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups,
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            for (int index = 0;
                 index < _translucentTemplates.Length;
                 index++)
            {
                QueueEntry<TPass> template =
                    _translucentTemplates[index];
                float depth = template.Group.ResolveDepth(
                    cameraPosition,
                    cameraForward);
                if (!float.IsFinite(depth))
                {
                    throw new ArgumentException(
                        $"Editor draw group {template.InputOrdinal} produced a non-finite sort depth.",
                        nameof(groups));
                }
                _translucentScratch[index] = template with
                {
                    Depth = depth
                };
            }
            Array.Sort(
                _translucentScratch,
                QueueEntryComparison<TPass>.Instance);
        }

        private void EnsureCurrentLayout(
            IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups)
        {
            if (_initialized && MatchesCurrentLayout(groups))
                return;

            Rebuild(groups);
        }

        private bool MatchesCurrentLayout(
            IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups)
        {
            if (groups.Count != _layout.Length)
                return false;

            for (int inputOrdinal = 0;
                 inputOrdinal < _layout.Length;
                 inputOrdinal++)
            {
                MapRenderEditorDrawGroup<TPass>? group =
                    groups[inputOrdinal];
                QueueLayoutEntry<TPass> expected = _layout[inputOrdinal];
                if (group is null ||
                    !ReferenceEquals(group, expected.Group) ||
                    group.Bucket != expected.Bucket ||
                    group.SourceOrdinal != expected.SourceOrdinal ||
                    group.CameraIndependentSortKey !=
                        expected.CameraIndependentSortKey ||
                    inputOrdinal != expected.InputOrdinal)
                {
                    return false;
                }
            }

            return true;
        }

        private void Rebuild(
            IReadOnlyList<MapRenderEditorDrawGroup<TPass>> groups)
        {
            var layout = new QueueLayoutEntry<TPass>[groups.Count];
            int cameraIndependentCount = 0;
            int translucentCount = 0;
            for (int inputOrdinal = 0;
                 inputOrdinal < groups.Count;
                 inputOrdinal++)
            {
                MapRenderEditorDrawGroup<TPass> group = groups[inputOrdinal]
                    ?? throw new ArgumentException(
                        $"Editor draw group {inputOrdinal} is null.",
                        nameof(groups));
                layout[inputOrdinal] = new QueueLayoutEntry<TPass>(
                    group,
                    inputOrdinal,
                    group.Bucket,
                    group.SourceOrdinal,
                    group.CameraIndependentSortKey);
                if (group.Bucket ==
                    MapRenderEditorDrawBucket.Translucent)
                {
                    translucentCount++;
                }
                else
                {
                    cameraIndependentCount++;
                }
            }

            var cameraIndependentEntries = new QueueEntry<TPass>[
                cameraIndependentCount];
            var translucentTemplates = new QueueEntry<TPass>[
                translucentCount];
            int cameraIndependentIndex = 0;
            int translucentIndex = 0;
            foreach (QueueLayoutEntry<TPass> entry in layout)
            {
                var queueEntry = new QueueEntry<TPass>(
                    entry.Group,
                    entry.InputOrdinal,
                    Depth: 0f);
                if (entry.Bucket ==
                    MapRenderEditorDrawBucket.Translucent)
                {
                    translucentTemplates[translucentIndex++] = queueEntry;
                }
                else
                {
                    cameraIndependentEntries[cameraIndependentIndex++] =
                        queueEntry;
                }
            }

            Array.Sort(
                cameraIndependentEntries,
                QueueEntryComparison<TPass>.Instance);
            var cameraIndependent =
                new MapRenderEditorDrawGroup<TPass>[
                    cameraIndependentEntries.Length];
            for (int index = 0;
                 index < cameraIndependentEntries.Length;
                 index++)
            {
                cameraIndependent[index] =
                    cameraIndependentEntries[index].Group;
            }

            _layout = layout;
            _cameraIndependent = cameraIndependent;
            _translucentTemplates = translucentTemplates;
            _translucentScratch = new QueueEntry<TPass>[translucentCount];
            _cameraIndependentResult = translucentCount == 0
                ? Array.AsReadOnly(cameraIndependent)
                : null;
            _frameResult = new MapRenderEditorDrawGroup<TPass>[
                layout.Length];
            cameraIndependent.CopyTo(_frameResult, 0);
            _frameResultView = Array.AsReadOnly(_frameResult);
            _lastFrameCamera = null;
            _lastCamera = null;
            _lastResult = null;
            _initialized = true;
        }
    }

    private static class QueueEntryComparison<TPass>
    {
        internal static Comparison<QueueEntry<TPass>> Instance { get; } =
            MapRenderEditorDrawQueueSorter.Compare;
    }

    private readonly record struct CameraSortKey(
        Vector3 Position,
        Vector3 Forward);

    private readonly record struct QueueLayoutEntry<TPass>(
        MapRenderEditorDrawGroup<TPass> Group,
        int InputOrdinal,
        MapRenderEditorDrawBucket Bucket,
        long SourceOrdinal,
        long? CameraIndependentSortKey);

    private readonly record struct QueueEntry<TPass>(
        MapRenderEditorDrawGroup<TPass> Group,
        int InputOrdinal,
        float Depth);
}
