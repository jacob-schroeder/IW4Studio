namespace IW4.Render.EditorPreview;

/// <summary>
/// Preserves complete static-model technique pass groups and expands only
/// blended groups into independently sortable instances. Opaque and cutout
/// groups retain instancing.
/// </summary>
public static class MapRenderEditorStaticDrawPlanner
{
    public static IReadOnlyList<MapRenderEditorStaticDrawPlan> Create(
        IReadOnlyList<MapRenderEditorStaticPassBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);

        var result = new List<MapRenderEditorStaticDrawPlan>();
        foreach (IGrouping<int, MapRenderEditorStaticPassBatch> sourceGroup in
                 batches.GroupBy(batch => batch.DrawGroupId)
                     .OrderBy(group => group.Min(batch => batch.SourceOrdinal)))
        {
            if (sourceGroup.Key < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(batches),
                    sourceGroup.Key,
                    "Static editor draw-group identifiers must be non-negative.");
            }

            MapRenderEditorStaticPassBatch[] ordered = sourceGroup
                .OrderBy(batch => batch.PassIndex)
                .ThenBy(batch => batch.SourceOrdinal)
                .ToArray();
            int instanceCount = ordered[0].InstanceCount;
            if (instanceCount <= 0 ||
                ordered.Any(batch => batch.InstanceCount != instanceCount))
            {
                throw new InvalidDataException(
                    $"Static editor draw group {sourceGroup.Key} does not have one positive, shared instance count.");
            }

            MapRenderEditorDrawIsolationPlan isolation =
                MapRenderEditorDrawIsolationPlanner.Create(
                    MapRenderEditorDrawSourceKind.StaticModel,
                    ordered.Select(batch => batch.State).ToArray());
            int[] passSourceOrdinals = ordered
                .Select(batch => batch.SourceOrdinal)
                .ToArray();

            if (!isolation.RequiresIndependentSortGroup)
            {
                result.Add(new MapRenderEditorStaticDrawPlan(
                    sourceGroup.Key,
                    passSourceOrdinals,
                    InstanceIndex: null,
                    isolation.Classification));
                continue;
            }

            for (int instanceIndex = 0;
                 instanceIndex < instanceCount;
                 instanceIndex++)
            {
                result.Add(new MapRenderEditorStaticDrawPlan(
                    sourceGroup.Key,
                    passSourceOrdinals,
                    instanceIndex,
                    isolation.Classification));
            }
        }

        return result;
    }
}
