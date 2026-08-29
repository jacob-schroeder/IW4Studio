using IW4.Render.Geometry;

namespace IW4.Render.SceneBuilding.Batching;

/// <summary>
/// Result of projecting prepared static-model pass groups into deterministic
/// instance runs using either host or native capacity policy.
/// </summary>
internal sealed record MapRenderStaticModelRunPlan(
    IReadOnlyList<MapRenderInstancedTexturedBatch> Batches,
    int SourceBatchCount,
    int SourceDrawGroupCount,
    int SelectedInstanceRowCount,
    int BucketCount,
    int RunCount,
    int AuxiliaryRunCount,
    int LargestRunInstanceCount,
    int RunCapacity)
{
    public int OutputBatchCount => Batches.Count;

    public int AdditionalRunCount => RunCount - BucketCount;
}

/// <summary>
/// Builds deterministic, native-shaped static-model runs from already
/// materialized geometry/pass groups. The PS3 producer groups by model,
/// primary light, and reflection probe, then flushes the eight
/// <c>(lod * 2) + auxiliaryParity</c> buckets. Its encoded run table permits
/// no more than 128 instances in one run.
///
/// The host scene does not retain an XModel pointer on each render batch, so
/// its existing draw-group identity is the authoritative geometry/surface
/// identity. Every pass in that group is split with the exact same ordered
/// instance subset, preserving authored multipass atomicity.
///
/// The host defaults to an unbounded run capacity because a compatible OpenGL
/// instanced draw should not be fragmented merely to reproduce an SPU command
/// encoding limit. <see cref="NativeMaximumInstancesPerRun"/> remains
/// available for parity diagnostics and native command serialization.
/// Likewise, scene construction has already split groups whose passes consume
/// a reflection probe, so the host does not repeat that split for compatible
/// non-reflective groups unless strict native identity is requested.
/// </summary>
internal static class MapRenderStaticModelRunPlanner
{
    internal const int NativeMaximumInstancesPerRun = 128;
    internal const int UnboundedHostRunCapacity = int.MaxValue;
    private const int MaximumNativeLodIndex = 3;

    public static MapRenderStaticModelRunPlan Create(
        IReadOnlyList<MapRenderInstancedTexturedBatch> sourceBatches,
        Func<MapRenderStaticModelInstance, bool>? isAuxiliary = null,
        int runCapacity = UnboundedHostRunCapacity,
        bool preserveNativeReflectionProbeIdentity = false)
    {
        ArgumentNullException.ThrowIfNull(sourceBatches);
        if (runCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(runCapacity));

        SourceBatch[] sources = sourceBatches
            .Select((batch, ordinal) => new SourceBatch(
                ordinal,
                batch ?? throw new ArgumentException(
                    "Static-model run sources cannot contain null batches.",
                    nameof(sourceBatches))))
            .ToArray();
        IGrouping<SourceDrawGroupKey, SourceBatch>[] sourceGroups = sources
            .GroupBy(source => CreateSourceDrawGroupKey(source))
            .ToArray();

        var output = new List<MapRenderInstancedTexturedBatch>();
        int selectedInstanceCount = 0;
        int bucketCount = 0;
        int runCount = 0;
        int auxiliaryRunCount = 0;
        int largestRunInstanceCount = 0;
        int nextOutputDrawGroupId = 0;

        foreach (IGrouping<SourceDrawGroupKey, SourceBatch> sourceGroup in
                 sourceGroups)
        {
            SourceBatch[] orderedPasses = sourceGroup
                .OrderBy(source =>
                    source.Batch.Pass.TechniquePass.PassIndex)
                .ThenBy(source => source.SourceOrdinal)
                .ToArray();
            if (orderedPasses.Length == 0)
                continue;

            int lodIndex = sourceGroup.Key.LodIndex;
            if (lodIndex is < 0 or > MaximumNativeLodIndex)
            {
                throw new InvalidDataException(
                    $"Static-model draw group {Describe(sourceGroup.Key)} has LOD {lodIndex}; native Event22 buckets require LOD 0 through {MaximumNativeLodIndex}.");
            }

            // The interactive host path has no native 128-instance encoding
            // limit, no auxiliary classifier, and has already split groups
            // that bind a reflection probe. It therefore produces exactly
            // one run per non-empty source group. Preserve every validation
            // below, but avoid materializing bucket rows, re-sorting every
            // authored pass, and copying the same instance array per pass.
            if (isAuxiliary is null &&
                runCapacity == UnboundedHostRunCapacity &&
                !preserveNativeReflectionProbeIdentity)
            {
                IReadOnlyList<MapRenderStaticModelInstance>
                    representativeSourceInstances =
                        orderedPasses[0].Batch.Instances;
                for (int instanceIndex = 0;
                     instanceIndex < representativeSourceInstances.Count;
                     instanceIndex++)
                {
                    if (representativeSourceInstances[instanceIndex]
                            .PrimaryLightIndex !=
                        sourceGroup.Key.SceneLightIndex)
                    {
                        throw new InvalidDataException(
                            $"Static-model draw group {Describe(sourceGroup.Key)} mixes an instance primary-light identity with scene-light bucket {sourceGroup.Key.SceneLightIndex}; the selected authored pass cannot be preserved.");
                    }
                }

                bool sourceSequencesAligned = true;
                for (int passIndex = 1;
                     passIndex < orderedPasses.Length;
                     passIndex++)
                {
                    if (!HaveSameInstanceSequence(
                            orderedPasses[passIndex].Batch.Instances,
                            representativeSourceInstances))
                    {
                        sourceSequencesAligned = false;
                        break;
                    }
                }

                IReadOnlyList<MapRenderStaticModelInstance>
                    sharedOrderedInstances;
                if (sourceSequencesAligned)
                {
                    sharedOrderedInstances = IsCanonicalInstanceOrder(
                            representativeSourceInstances)
                        ? representativeSourceInstances
                        : OrderInstances(representativeSourceInstances)
                            .Select(row => row.Instance)
                            .ToArray();
                }
                else
                {
                    OrderedInstance[][] fastOrderedInstancesByPass =
                        orderedPasses
                            .Select(source =>
                                OrderInstances(source.Batch.Instances))
                            .ToArray();
                    OrderedInstance[] fastRepresentativeInstances =
                        fastOrderedInstancesByPass[0];
                    for (int passIndex = 1;
                         passIndex < fastOrderedInstancesByPass.Length;
                         passIndex++)
                    {
                        OrderedInstance[] candidate =
                            fastOrderedInstancesByPass[passIndex];
                        if (candidate.Length !=
                                fastRepresentativeInstances.Length ||
                            !HaveSameOrderedInstanceSequence(
                                candidate,
                                fastRepresentativeInstances))
                        {
                            throw new InvalidDataException(
                                $"Static-model draw group {Describe(sourceGroup.Key)} has misaligned authored pass instances; unbounded host runs cannot preserve multipass ownership.");
                        }
                    }

                    sharedOrderedInstances = fastRepresentativeInstances
                        .Select(row => row.Instance)
                        .ToArray();
                }

                int instanceCount = sharedOrderedInstances.Count;
                selectedInstanceCount = checked(
                    selectedInstanceCount + instanceCount);
                if (instanceCount == 0)
                    continue;

                int outputDrawGroupId = nextOutputDrawGroupId;
                nextOutputDrawGroupId = checked(
                    nextOutputDrawGroupId + 1);
                foreach (SourceBatch source in orderedPasses)
                {
                    MapRenderInstancedTexturedBatch batch = source.Batch;
                    output.Add(
                        batch.EditorDrawGroupId == outputDrawGroupId &&
                        ReferenceEquals(
                            batch.Instances,
                            sharedOrderedInstances)
                            ? batch
                            : batch with
                            {
                                Instances = sharedOrderedInstances,
                                EditorDrawGroupId = outputDrawGroupId
                            });
                }

                bucketCount = checked(bucketCount + 1);
                runCount = checked(runCount + 1);
                largestRunInstanceCount = Math.Max(
                    largestRunInstanceCount,
                    instanceCount);
                continue;
            }

            OrderedInstance[][] orderedInstancesByPass = orderedPasses
                .Select(source => OrderInstances(source.Batch.Instances))
                .ToArray();
            OrderedInstance[] representativeInstances =
                orderedInstancesByPass[0];
            if (representativeInstances.Any(row =>
                    row.Instance.PrimaryLightIndex !=
                    sourceGroup.Key.SceneLightIndex))
            {
                throw new InvalidDataException(
                    $"Static-model draw group {Describe(sourceGroup.Key)} mixes an instance primary-light identity with scene-light bucket {sourceGroup.Key.SceneLightIndex}; the selected authored pass cannot be preserved.");
            }
            for (int passIndex = 1;
                 passIndex < orderedInstancesByPass.Length;
                 passIndex++)
            {
                OrderedInstance[] candidate =
                    orderedInstancesByPass[passIndex];
                if (candidate.Length != representativeInstances.Length ||
                    !candidate
                        .Select(row => row.Instance)
                        .SequenceEqual(
                            representativeInstances.Select(row =>
                                row.Instance)))
                {
                    throw new InvalidDataException(
                        $"Static-model draw group {Describe(sourceGroup.Key)} has misaligned authored pass instances; bounded runs cannot preserve multipass ownership.");
                }
            }

            selectedInstanceCount = checked(
                selectedInstanceCount + representativeInstances.Length);
            var selectedRows = representativeInstances
                .Select((row, orderedIndex) => new SelectedInstance(
                    orderedIndex,
                    row.Instance,
                    isAuxiliary?.Invoke(row.Instance) ?? false))
                .ToArray();
            // Scene construction has already split a draw group when any
            // selected pass binds the reflection-probe sampler. A host run can
            // therefore retain that resource-compatible grouping without
            // fragmenting non-reflective draws. Native serialization can opt
            // back into the stricter model/light/probe identity.
            bool splitReflectionProbeBuckets =
                preserveNativeReflectionProbeIdentity;
            IGrouping<RunBucketKey, SelectedInstance>[] buckets =
                selectedRows
                    .GroupBy(row => new RunBucketKey(
                        row.Instance.PrimaryLightIndex,
                        splitReflectionProbeBuckets
                            ? row.Instance.ReflectionProbeIndex
                            : null,
                        lodIndex,
                        row.IsAuxiliary))
                    .OrderBy(bucket => bucket.Key.PrimaryLightIndex)
                    .ThenBy(bucket =>
                        bucket.Key.ReflectionProbeIndex.HasValue)
                    .ThenBy(bucket => bucket.Key.ReflectionProbeIndex)
                    .ThenBy(bucket => bucket.Key.NativeBucketIndex)
                    .ToArray();

            bucketCount = checked(bucketCount + buckets.Length);
            foreach (IGrouping<RunBucketKey, SelectedInstance> bucket in
                     buckets)
            {
                SelectedInstance[] bucketRows = bucket
                    .OrderBy(row => row.Instance.ObjectIndex)
                    .ThenBy(row => row.Instance.SurfaceIndex)
                    .ThenBy(row => row.OrderedIndex)
                    .ToArray();
                int start = 0;
                while (start < bucketRows.Length)
                {
                    int length = Math.Min(
                        runCapacity,
                        bucketRows.Length - start);
                    int outputDrawGroupId = nextOutputDrawGroupId;
                    nextOutputDrawGroupId = checked(
                        nextOutputDrawGroupId + 1);
                    for (int passIndex = 0;
                         passIndex < orderedPasses.Length;
                         passIndex++)
                    {
                        OrderedInstance[] passInstances =
                            orderedInstancesByPass[passIndex];
                        var runInstances =
                            new MapRenderStaticModelInstance[length];
                        for (int runIndex = 0;
                             runIndex < length;
                             runIndex++)
                        {
                            runInstances[runIndex] = passInstances[
                                bucketRows[start + runIndex]
                                    .OrderedIndex].Instance;
                        }

                        output.Add(orderedPasses[passIndex].Batch with
                        {
                            Instances = runInstances,
                            EditorDrawGroupId = outputDrawGroupId
                        });
                    }

                    runCount = checked(runCount + 1);
                    if (bucket.Key.IsAuxiliary)
                    {
                        auxiliaryRunCount = checked(
                            auxiliaryRunCount + 1);
                    }
                    largestRunInstanceCount = Math.Max(
                        largestRunInstanceCount,
                        length);
                    start = checked(start + length);
                }
            }
        }

        return new MapRenderStaticModelRunPlan(
            output.ToArray(),
            sourceBatches.Count,
            sourceGroups.Length,
            selectedInstanceCount,
            bucketCount,
            runCount,
            auxiliaryRunCount,
            largestRunInstanceCount,
            runCapacity);
    }

    private static SourceDrawGroupKey CreateSourceDrawGroupKey(
        SourceBatch source)
    {
        MapRenderInstancedTexturedBatch batch = source.Batch;
        return new SourceDrawGroupKey(
            HasAuthoredDrawGroup: batch.EditorDrawGroupId >= 0,
            AuthoredDrawGroupId: batch.EditorDrawGroupId,
            UngroupedSourceOrdinal: batch.EditorDrawGroupId >= 0
                ? -1
                : source.SourceOrdinal,
            batch.LodIndex,
            batch.SceneLightIndex);
    }

    private static OrderedInstance[] OrderInstances(
        IReadOnlyList<MapRenderStaticModelInstance> instances) =>
        instances
            .Select((instance, sourceOrdinal) => new OrderedInstance(
                sourceOrdinal,
                instance))
            .OrderBy(row => row.Instance.ObjectIndex)
            .ThenBy(row => row.Instance.SurfaceIndex)
            .ThenBy(row => row.Instance.PrimaryLightIndex)
            .ThenBy(row => row.Instance.ReflectionProbeIndex)
            .ThenBy(row => row.Instance.CameraRegion)
            .ThenBy(row => row.Instance.Name, StringComparer.Ordinal)
            .ThenBy(
                row => row.Instance.AuthoredMaterialName,
                StringComparer.Ordinal)
            .ThenBy(row => row.SourceOrdinal)
            .ToArray();

    private static bool HaveSameInstanceSequence(
        IReadOnlyList<MapRenderStaticModelInstance> candidate,
        IReadOnlyList<MapRenderStaticModelInstance> expected)
    {
        if (candidate.Count != expected.Count)
            return false;

        for (int index = 0; index < candidate.Count; index++)
        {
            if (candidate[index] != expected[index])
                return false;
        }

        return true;
    }

    private static bool HaveSameOrderedInstanceSequence(
        IReadOnlyList<OrderedInstance> candidate,
        IReadOnlyList<OrderedInstance> expected)
    {
        if (candidate.Count != expected.Count)
            return false;

        for (int index = 0; index < candidate.Count; index++)
        {
            if (candidate[index].Instance != expected[index].Instance)
                return false;
        }

        return true;
    }

    private static bool IsCanonicalInstanceOrder(
        IReadOnlyList<MapRenderStaticModelInstance> instances)
    {
        for (int index = 1; index < instances.Count; index++)
        {
            if (CompareInstanceKeys(
                    instances[index - 1],
                    instances[index]) > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareInstanceKeys(
        MapRenderStaticModelInstance first,
        MapRenderStaticModelInstance second)
    {
        int comparison = first.ObjectIndex.CompareTo(second.ObjectIndex);
        if (comparison != 0)
            return comparison;
        comparison = first.SurfaceIndex.CompareTo(second.SurfaceIndex);
        if (comparison != 0)
            return comparison;
        comparison = first.PrimaryLightIndex.CompareTo(
            second.PrimaryLightIndex);
        if (comparison != 0)
            return comparison;
        comparison = first.ReflectionProbeIndex.CompareTo(
            second.ReflectionProbeIndex);
        if (comparison != 0)
            return comparison;
        comparison = first.CameraRegion.CompareTo(second.CameraRegion);
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.Ordinal.Compare(
            first.Name,
            second.Name);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                first.AuthoredMaterialName,
                second.AuthoredMaterialName);
    }

    private static string Describe(SourceDrawGroupKey key) =>
        key.HasAuthoredDrawGroup
            ? key.AuthoredDrawGroupId.ToString()
            : $"source {key.UngroupedSourceOrdinal}";

    private readonly record struct SourceBatch(
        int SourceOrdinal,
        MapRenderInstancedTexturedBatch Batch);

    private readonly record struct SourceDrawGroupKey(
        bool HasAuthoredDrawGroup,
        int AuthoredDrawGroupId,
        int UngroupedSourceOrdinal,
        int LodIndex,
        byte SceneLightIndex);

    private readonly record struct OrderedInstance(
        int SourceOrdinal,
        MapRenderStaticModelInstance Instance);

    private readonly record struct SelectedInstance(
        int OrderedIndex,
        MapRenderStaticModelInstance Instance,
        bool IsAuxiliary);

    private readonly record struct RunBucketKey(
        int PrimaryLightIndex,
        byte? ReflectionProbeIndex,
        int LodIndex,
        bool IsAuxiliary)
    {
        public int NativeBucketIndex =>
            checked((LodIndex * 2) + (IsAuxiliary ? 1 : 0));
    }
}
