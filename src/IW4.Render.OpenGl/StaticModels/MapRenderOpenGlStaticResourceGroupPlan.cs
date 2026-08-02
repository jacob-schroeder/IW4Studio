using IW4.Render.Geometry;
using IW4.Render.Scheduling;

namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// Immutable CPU-only grouping for progressive static-model resource
/// materialization. A group is the smallest authored unit that may become
/// executable: every pass sharing an EditorDrawGroupId is admitted together.
/// </summary>
internal sealed class MapRenderOpenGlStaticResourceGroupPlan
{
    private readonly Group[] _groups;
    private readonly Dictionary<MapRenderStaticModelReceiverIdentity, int>
        _receiverGroupByIdentity;
    private readonly Dictionary<ObjectLodKey, int[]>
        _groupsByObjectLod;
    private readonly int[] _allGroups;
    private readonly uint[] _selectionGenerations;
    private readonly int[] _selectedGroups;
    private uint _selectionGeneration;
    private int _selectedGroupCount;

    private MapRenderOpenGlStaticResourceGroupPlan(
        Group[] groups,
        Dictionary<MapRenderStaticModelReceiverIdentity, int>
            receiverGroupByIdentity,
        Dictionary<ObjectLodKey, int[]> groupsByObjectLod)
    {
        _groups = groups;
        _receiverGroupByIdentity = receiverGroupByIdentity;
        _groupsByObjectLod = groupsByObjectLod;
        _allGroups = Enumerable.Range(0, groups.Length).ToArray();
        _selectionGenerations = new uint[groups.Length];
        _selectedGroups = new int[groups.Length];
    }

    public int GroupCount => _groups.Length;

    public int BatchCount =>
        _groups.Sum(group => group.BatchOrdinals.Length);

    public ReadOnlySpan<int> AllGroups => _allGroups;

    public ReadOnlySpan<int> SelectedGroups =>
        _selectedGroups.AsSpan(0, _selectedGroupCount);

    public Group this[int groupIndex] =>
        (uint)groupIndex < (uint)_groups.Length
            ? _groups[groupIndex]
            : throw new ArgumentOutOfRangeException(nameof(groupIndex));

    public static MapRenderOpenGlStaticResourceGroupPlan Create(
        IReadOnlyList<MapRenderInstancedTexturedBatch> batches,
        bool requireReceiverIdentityClosure)
    {
        ArgumentNullException.ThrowIfNull(batches);

        Group[] groups = batches
            .Select((batch, ordinal) => new
            {
                Batch = batch ?? throw new ArgumentException(
                    "Static resource batches cannot contain null entries.",
                    nameof(batches)),
                Ordinal = ordinal,
                GroupIdentity = batch.EditorDrawGroupId >= 0
                    ? batch.EditorDrawGroupId
                    : checked(int.MinValue + ordinal)
            })
            .GroupBy(entry => entry.GroupIdentity)
            .OrderBy(group => group.Min(entry => entry.Ordinal))
            .Select((group, groupIndex) =>
            {
                int[] ordinals = group
                    .OrderBy(entry => entry.Batch.Pass.PassIndex)
                    .ThenBy(entry => entry.Ordinal)
                    .Select(entry => entry.Ordinal)
                    .ToArray();
                MapRenderStaticModelReceiverIdentity[] identities =
                    requireReceiverIdentityClosure
                        ? TryCreateAlignedReceiverIdentities(
                            batches,
                            ordinals,
                            out MapRenderStaticModelReceiverIdentity[]
                                aligned)
                            ? aligned
                            : []
                        : [];
                bool receiverStructureReady =
                    !requireReceiverIdentityClosure ||
                    (group.Key >= 0 && identities.Length != 0);
                return new Group(
                    groupIndex,
                    ordinals,
                    identities,
                    receiverStructureReady);
            })
            .ToArray();

        var receiverGroupByIdentity = new Dictionary<
            MapRenderStaticModelReceiverIdentity,
            int>();
        if (requireReceiverIdentityClosure)
        {
            // Match the historical eager walk deterministically: a later
            // authored group that aliases any already-owned receiver identity
            // is rejected as a whole, irrespective of materialization order.
            foreach (Group group in groups)
            {
                if (!group.ReceiverStructureReady ||
                    group.ReceiverIdentities.Any(
                        receiverGroupByIdentity.ContainsKey))
                {
                    group.ReceiverStructureReady = false;
                    continue;
                }

                foreach (MapRenderStaticModelReceiverIdentity identity in
                         group.ReceiverIdentities)
                {
                    receiverGroupByIdentity.Add(identity, group.GroupIndex);
                }
            }
        }

        var groupListsByObjectLod =
            new Dictionary<ObjectLodKey, List<int>>();
        foreach (Group group in groups)
        {
            var groupKeys = new HashSet<ObjectLodKey>();
            foreach (int batchOrdinal in group.BatchOrdinals)
            {
                MapRenderInstancedTexturedBatch batch =
                    batches[batchOrdinal];
                foreach (MapRenderStaticModelInstance instance in
                         batch.Instances)
                {
                    groupKeys.Add(new(
                        instance.ObjectIndex,
                        batch.LodIndex));
                }
            }

            foreach (ObjectLodKey key in groupKeys)
            {
                if (!groupListsByObjectLod.TryGetValue(
                        key,
                        out List<int>? groupIndices))
                {
                    groupIndices = [];
                    groupListsByObjectLod.Add(key, groupIndices);
                }
                groupIndices.Add(group.GroupIndex);
            }
        }
        Dictionary<ObjectLodKey, int[]> groupsByObjectLod =
            groupListsByObjectLod.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray());

        return new MapRenderOpenGlStaticResourceGroupPlan(
            groups,
            receiverGroupByIdentity,
            groupsByObjectLod);
    }

    /// <summary>
    /// Selects groups into plan-owned scratch. This method is intentionally
    /// non-thread-safe: the renderer invokes it only on its owning OpenGL
    /// thread, and the returned span remains valid until this plan's next
    /// selection.
    /// </summary>
    public ReadOnlySpan<int> SelectRequiredGroups(
        ReadOnlySpan<bool> visibleByObject,
        ReadOnlySpan<int> selectedLodByObject)
    {
        if (visibleByObject.Length != selectedLodByObject.Length)
        {
            throw new ArgumentException(
                "Static visibility and selected-LOD storage must have the same capacity.");
        }

        _selectionGeneration = unchecked(_selectionGeneration + 1);
        if (_selectionGeneration == 0)
        {
            Array.Clear(_selectionGenerations);
            _selectionGeneration = 1;
        }

        _selectedGroupCount = 0;
        for (int objectIndex = 0;
             objectIndex < visibleByObject.Length;
             objectIndex++)
        {
            if (!visibleByObject[objectIndex] ||
                !_groupsByObjectLod.TryGetValue(
                    new(
                        objectIndex,
                        selectedLodByObject[objectIndex]),
                    out int[]? groupIndices))
            {
                continue;
            }

            foreach (int groupIndex in groupIndices)
            {
                if (_selectionGenerations[groupIndex] ==
                    _selectionGeneration)
                {
                    continue;
                }
                _selectionGenerations[groupIndex] =
                    _selectionGeneration;
                _selectedGroups[_selectedGroupCount++] = groupIndex;
            }
        }

        Array.Sort(
            _selectedGroups,
            0,
            _selectedGroupCount);
        return _selectedGroups.AsSpan(0, _selectedGroupCount);
    }

    public bool TryGetReceiverGroup(
        MapRenderStaticModelReceiverIdentity identity,
        out int groupIndex) =>
        _receiverGroupByIdentity.TryGetValue(identity, out groupIndex);

    public static bool IsSelected(
        int objectIndex,
        int lodIndex,
        ReadOnlySpan<bool> visibleByObject,
        ReadOnlySpan<int> selectedLodByObject) =>
        (uint)objectIndex < (uint)visibleByObject.Length &&
        visibleByObject[objectIndex] &&
        selectedLodByObject[objectIndex] == lodIndex;

    private static bool TryCreateAlignedReceiverIdentities(
        IReadOnlyList<MapRenderInstancedTexturedBatch> batches,
        IReadOnlyList<int> batchOrdinals,
        out MapRenderStaticModelReceiverIdentity[] identities)
    {
        identities = [];
        if (batchOrdinals.Count == 0)
            return false;

        MapRenderInstancedTexturedBatch first =
            batches[batchOrdinals[0]];
        int instanceCount = first.Instances.Count;
        if (instanceCount == 0 ||
            batchOrdinals.Any(ordinal =>
                batches[ordinal].Instances.Count != instanceCount))
        {
            return false;
        }

        identities =
            new MapRenderStaticModelReceiverIdentity[instanceCount];
        for (int instanceIndex = 0;
             instanceIndex < instanceCount;
             instanceIndex++)
        {
            var identity = new MapRenderStaticModelReceiverIdentity(
                first.Instances[instanceIndex],
                first.LodIndex);
            if (batchOrdinals.Any(ordinal =>
                    new MapRenderStaticModelReceiverIdentity(
                        batches[ordinal].Instances[instanceIndex],
                        batches[ordinal].LodIndex) != identity))
            {
                identities = [];
                return false;
            }
            identities[instanceIndex] = identity;
        }

        return true;
    }

    internal sealed class Group
    {
        internal Group(
            int groupIndex,
            int[] batchOrdinals,
            MapRenderStaticModelReceiverIdentity[] receiverIdentities,
            bool receiverStructureReady)
        {
            GroupIndex = groupIndex;
            BatchOrdinals = batchOrdinals;
            ReceiverIdentities = receiverIdentities;
            ReceiverStructureReady = receiverStructureReady;
        }

        public int GroupIndex { get; }

        public int[] BatchOrdinals { get; }

        public MapRenderStaticModelReceiverIdentity[] ReceiverIdentities
        {
            get;
        }

        public bool ReceiverStructureReady { get; internal set; }
    }

    private readonly record struct ObjectLodKey(
        int ObjectIndex,
        int LodIndex);
}
