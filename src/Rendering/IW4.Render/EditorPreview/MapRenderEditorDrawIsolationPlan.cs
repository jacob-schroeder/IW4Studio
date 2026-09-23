namespace IW4.Render.EditorPreview;

public sealed record MapRenderEditorDrawIsolationPlan(
    MapRenderEditorDrawBucketClassification Classification,
    MapRenderEditorDrawIsolation Isolation)
{
    public bool RequiresIndependentSortGroup =>
        Isolation != MapRenderEditorDrawIsolation.MergeCompatibleGeometry;
}
