using System.Collections.ObjectModel;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;

namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// Fully validated CPU stage for one renderer-facing static-model projection.
/// The OpenGL owner can prepare every retained placement and scheduling row
/// before committing any mutable renderer state.
/// </summary>
internal sealed class MapRenderOpenGlLiveStaticModelState
{
    internal MapRenderOpenGlLiveStaticModelState(
        MapRenderStaticModelSchedulingInfo[] scheduling,
        IReadOnlyDictionary<int, MapRenderStaticModelSchedulingInfo>
            schedulingByObjectIndex,
        MapRenderStaticModelInstance[][] instanceBatches,
        bool[] visibilityByObjectIndex,
        MapRenderStaticSunShadowCasterBatch[] sunShadowCasterBatches,
        MapRenderSunShadowStaticCasterExpectation[]
            sunShadowCasterExpectations)
    {
        Scheduling = scheduling ??
            throw new ArgumentNullException(nameof(scheduling));
        SchedulingByObjectIndex = schedulingByObjectIndex ??
            throw new ArgumentNullException(
                nameof(schedulingByObjectIndex));
        InstanceBatches = instanceBatches ??
            throw new ArgumentNullException(nameof(instanceBatches));
        VisibilityByObjectIndex = visibilityByObjectIndex ??
            throw new ArgumentNullException(
                nameof(visibilityByObjectIndex));
        SunShadowCasterBatches = sunShadowCasterBatches ??
            throw new ArgumentNullException(
                nameof(sunShadowCasterBatches));
        SunShadowCasterExpectations = sunShadowCasterExpectations ??
            throw new ArgumentNullException(
                nameof(sunShadowCasterExpectations));
    }

    internal MapRenderStaticModelSchedulingInfo[] Scheduling { get; }

    internal IReadOnlyDictionary<int, MapRenderStaticModelSchedulingInfo>
        SchedulingByObjectIndex { get; }

    internal MapRenderStaticModelInstance[][] InstanceBatches { get; }

    internal bool[] VisibilityByObjectIndex { get; }

    internal MapRenderStaticSunShadowCasterBatch[]
        SunShadowCasterBatches { get; }

    internal MapRenderSunShadowStaticCasterExpectation[]
        SunShadowCasterExpectations { get; }
}

/// <summary>
/// Reconciles the complete semantic Gfx ordinal catalog against immutable
/// renderer topology. The result contains no OpenGL handles and is safe to
/// discard if any placement, schedule, or cardinality validation fails.
/// </summary>
internal static class MapRenderOpenGlLiveStaticModelStateReconciler
{
    internal static MapRenderOpenGlLiveStaticModelState Reconcile(
        MapRenderLiveSceneProjection projection,
        int loadedSourceCount,
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        IReadOnlyList<IReadOnlyList<MapRenderStaticModelInstance>>
            instanceBatches,
        IReadOnlyList<MapRenderStaticSunShadowCasterBatch>?
            sunShadowCasterBatches = null,
        IReadOnlyList<MapRenderSunShadowStaticCasterExpectation>?
            sunShadowCasterExpectations = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(scheduling);
        ArgumentNullException.ThrowIfNull(instanceBatches);
        sunShadowCasterBatches ??= [];
        sunShadowCasterExpectations ??= [];

        MapRenderLiveStaticModelTranslationReconciliation reconciliation =
            MapRenderLiveStaticModelTranslationReconciler.Reconcile(
                projection,
                loadedSourceCount);

        var stagedScheduling =
            new MapRenderStaticModelSchedulingInfo[scheduling.Count];
        var stagedSchedulingByObject =
            new Dictionary<int, MapRenderStaticModelSchedulingInfo>(
                scheduling.Count);
        for (int index = 0; index < scheduling.Count; index++)
        {
            MapRenderStaticModelSchedulingInfo source =
                scheduling[index] ??
                throw new InvalidOperationException(
                    "Loaded static-model scheduling cannot contain null rows.");
            MapRenderStaticModelSchedulingInfo staged =
                reconciliation.Reconcile(source);
            if (!stagedSchedulingByObject.TryAdd(
                    staged.ObjectIndex,
                    staged))
            {
                throw new InvalidOperationException(
                    $"Loaded static-model scheduling contains duplicate object index {staged.ObjectIndex}.");
            }

            stagedScheduling[index] = staged;
        }

        var stagedBatches =
            new MapRenderStaticModelInstance[instanceBatches.Count][];
        for (int batchIndex = 0;
             batchIndex < instanceBatches.Count;
             batchIndex++)
        {
            IReadOnlyList<MapRenderStaticModelInstance> sourceBatch =
                instanceBatches[batchIndex] ??
                throw new InvalidOperationException(
                    "Loaded static-model instance batches cannot contain null collections.");
            var stagedBatch =
                new MapRenderStaticModelInstance[sourceBatch.Count];
            for (int instanceIndex = 0;
                 instanceIndex < sourceBatch.Count;
                 instanceIndex++)
            {
                stagedBatch[instanceIndex] =
                    reconciliation.Reconcile(
                        sourceBatch[instanceIndex]);
            }
            stagedBatches[batchIndex] = stagedBatch;
        }

        var visibilityByObject = new bool[loadedSourceCount];
        for (int sourceOrdinal = 0;
             sourceOrdinal < visibilityByObject.Length;
             sourceOrdinal++)
        {
            visibilityByObject[sourceOrdinal] =
                reconciliation.IsVisible(sourceOrdinal);
        }

        var stagedSunShadowBatches =
            new MapRenderStaticSunShadowCasterBatch[
                sunShadowCasterBatches.Count];
        for (int batchIndex = 0;
             batchIndex < stagedSunShadowBatches.Length;
             batchIndex++)
        {
            MapRenderStaticSunShadowCasterBatch source =
                sunShadowCasterBatches[batchIndex] ??
                throw new InvalidOperationException(
                    "Loaded static-model sun-shadow batches cannot contain null rows.");
            MapRenderSunShadowStaticCasterInstance[] instances =
                source.Instances
                    .Select(instance => instance with
                    {
                        Instance = reconciliation.Reconcile(
                            instance.Instance),
                        GameOrigin = ResolveGameOrigin(
                            projection,
                            instance.ObjectIndex)
                    })
                    .ToArray();
            stagedSunShadowBatches[batchIndex] =
                new MapRenderStaticSunShadowCasterBatch(
                    source.Model,
                    source.Lod,
                    source.LodIndex,
                    source.Surface,
                    source.SurfaceOffset,
                    source.MaterialSurfaceIndex,
                    source.MaterialEligibility,
                    source.Material,
                    source.Geometry,
                    source.CutoutUvRoute,
                    source.CutoutTexture,
                    instances);
        }

        MapRenderSunShadowStaticCasterExpectation[]
            stagedSunShadowExpectations =
                sunShadowCasterExpectations
                    .Select(expectation => expectation with
                    {
                        GameOrigin = ResolveGameOrigin(
                            projection,
                            expectation.ObjectIndex)
                    })
                    .ToArray();

        return new MapRenderOpenGlLiveStaticModelState(
            stagedScheduling,
            new ReadOnlyDictionary<
                int,
                MapRenderStaticModelSchedulingInfo>(
                stagedSchedulingByObject),
            stagedBatches,
            visibilityByObject,
            stagedSunShadowBatches,
            stagedSunShadowExpectations);
    }

    private static System.Numerics.Vector3 ResolveGameOrigin(
        MapRenderLiveSceneProjection projection,
        int objectIndex)
    {
        if ((uint)objectIndex >=
                (uint)projection.StaticModelTranslations.Count ||
            projection.StaticModelTranslations[objectIndex]
                .SourceOrdinal != objectIndex)
        {
            throw new InvalidOperationException(
                $"Live Preview has no canonical static-model origin for sun-shadow object index {objectIndex}.");
        }

        return projection.StaticModelTranslations[objectIndex].Origin;
    }
}
