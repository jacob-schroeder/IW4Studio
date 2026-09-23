namespace IW4.Render.EditorPreview;

/// <summary>
/// Immutable result for one complete material pass group.
/// </summary>
public sealed class MapRenderEditorDrawBucketClassification
{
    internal MapRenderEditorDrawBucketClassification(
        MapRenderEditorDrawBucket bucket,
        bool usesOpaqueStateFallback)
    {
        if (!Enum.IsDefined(bucket))
            throw new ArgumentOutOfRangeException(nameof(bucket));

        Bucket = bucket;
        UsesOpaqueStateFallback = usesOpaqueStateFallback;
    }

    public MapRenderEditorDrawBucket Bucket { get; }

    public bool UsesOpaqueStateFallback { get; }
}
