using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Determines the minimum practical EditorPreview isolation required before
/// queue sorting. Cutouts can remain batched because depth testing resolves
/// their visible samples; blended geometry must be independently sortable.
/// </summary>
public static class MapRenderEditorDrawIsolationPlanner
{
    public static MapRenderEditorDrawIsolationPlan Create(
        MapRenderEditorDrawSourceKind sourceKind,
        IReadOnlyList<MapRenderState> completePassStates)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown editor draw source kind.");
        }

        MapRenderEditorDrawBucketClassification classification =
            MapRenderEditorDrawBucketClassifier.Classify(completePassStates);
        MapRenderEditorDrawIsolation isolation = classification.Bucket switch
        {
            MapRenderEditorDrawBucket.Opaque or
            MapRenderEditorDrawBucket.AlphaTest =>
                MapRenderEditorDrawIsolation.MergeCompatibleGeometry,
            MapRenderEditorDrawBucket.Translucent => sourceKind switch
            {
                MapRenderEditorDrawSourceKind.WorldSurface =>
                    MapRenderEditorDrawIsolation.WorldSurfacePassGroup,
                MapRenderEditorDrawSourceKind.StaticModel =>
                    MapRenderEditorDrawIsolation.StaticModelInstancePassGroup,
                _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification.Bucket,
                "Unknown editor draw bucket.")
        };

        return new MapRenderEditorDrawIsolationPlan(classification, isolation);
    }
}
