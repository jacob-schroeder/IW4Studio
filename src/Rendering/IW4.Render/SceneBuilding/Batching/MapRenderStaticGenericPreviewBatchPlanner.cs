using IW4.Render.Geometry;

namespace IW4.Render.SceneBuilding.Batching;

/// <summary>
/// Describes one generic-preview static batch together with every exact
/// authored probe bucket that contributes instances to its first pass.
/// </summary>
internal sealed record MapRenderStaticGenericPreviewBatchGroup<T>(
    StaticTexturedBatchKey BatchKey,
    StaticTexturedDrawGroupKey DrawGroupKey,
    IReadOnlyList<KeyValuePair<StaticTexturedBatchKey, T>> ExactBatches);

/// <summary>
/// Projects exact static-model technique groups into one conservative generic
/// material pass. The generic program cannot consume an authored reflection
/// probe or reproduce later authored correction passes, so probe identity is
/// collapsed and only the first authored pass supplies the fallback draw.
/// Exact normal-camera and receiver sidecars retain the complete original
/// group, sampler bindings, and pass order.
/// </summary>
internal static class MapRenderStaticGenericPreviewBatchPlanner
{
    public static IReadOnlyList<
        MapRenderStaticGenericPreviewBatchGroup<T>> Create<T>(
            IEnumerable<KeyValuePair<StaticTexturedBatchKey, T>> exactBatches)
    {
        ArgumentNullException.ThrowIfNull(exactBatches);

        var grouped = new Dictionary<
            StaticTexturedDrawGroupKey,
            List<KeyValuePair<StaticTexturedBatchKey, T>>>();
        foreach (KeyValuePair<StaticTexturedBatchKey, T> exactBatch in
                 exactBatches)
        {
            StaticTexturedBatchKey batchKey = exactBatch.Key;
            var genericDrawGroupKey = new StaticTexturedDrawGroupKey(
                batchKey.LodIndex,
                batchKey.Surface,
                batchKey.Material,
                batchKey.SelectedTechniqueSlot,
                ReflectionProbeIndex: null,
                batchKey.SceneLightIndex);
            if (!grouped.TryGetValue(
                    genericDrawGroupKey,
                    out List<KeyValuePair<StaticTexturedBatchKey, T>>?
                        contributors))
            {
                contributors = [];
                grouped.Add(genericDrawGroupKey, contributors);
            }

            contributors.Add(exactBatch);
        }

        return grouped
            .Select(entry =>
            {
                KeyValuePair<StaticTexturedBatchKey, T> representative =
                    entry.Value
                        .OrderBy(contributor =>
                            contributor.Key.PassIndex)
                        .ThenBy(contributor =>
                            contributor.Key.TechniqueSlot)
                        .ThenBy(contributor =>
                            contributor.Key.SamplerArgIndex)
                        .ThenBy(contributor =>
                            contributor.Key.SamplerHash)
                        .ThenBy(contributor =>
                            contributor.Key.ReflectionProbeIndex.HasValue)
                        .ThenBy(contributor =>
                            contributor.Key.ReflectionProbeIndex)
                        .First();
                StaticTexturedBatchKey firstPassKey =
                    representative.Key;
                KeyValuePair<StaticTexturedBatchKey, T>[] exactContributors =
                    entry.Value
                        .Where(contributor =>
                            contributor.Key.TechniqueSlot ==
                                firstPassKey.TechniqueSlot &&
                            contributor.Key.PassIndex ==
                                firstPassKey.PassIndex &&
                            contributor.Key.SamplerArgIndex ==
                                firstPassKey.SamplerArgIndex &&
                            contributor.Key.SamplerHash ==
                                firstPassKey.SamplerHash)
                        .OrderBy(contributor =>
                            contributor.Key.ReflectionProbeIndex.HasValue)
                        .ThenBy(contributor =>
                            contributor.Key.ReflectionProbeIndex)
                        .ToArray();
                return new MapRenderStaticGenericPreviewBatchGroup<T>(
                    firstPassKey with
                    {
                        ReflectionProbeIndex = null
                    },
                    entry.Key,
                    exactContributors);
            })
            .ToArray();
    }
}
