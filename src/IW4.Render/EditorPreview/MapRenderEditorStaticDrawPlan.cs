namespace IW4.Render.EditorPreview;

/// <summary>
/// One sortable static-model draw unit. A null instance index means all
/// compatible instances remain in one instanced draw; a concrete index means
/// one blended instance is replayed independently with every authored pass.
/// </summary>
public sealed record MapRenderEditorStaticDrawPlan(
    int DrawGroupId,
    IReadOnlyList<int> PassSourceOrdinals,
    int? InstanceIndex,
    MapRenderEditorDrawBucketClassification Classification)
{
    public bool IsPerInstance => InstanceIndex.HasValue;
}
