using IW4.Render.Techniques;
using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Classifies all passes in one editor draw group. Blending has precedence over
/// alpha test; unavailable pass state falls back to the opaque bucket.
/// </summary>
public static class MapRenderEditorDrawBucketClassifier
{
    public static MapRenderEditorDrawBucketClassification Classify(
        IReadOnlyList<RenderState> passStates)
    {
        ArgumentNullException.ThrowIfNull(passStates);
        if (passStates.Count == 0)
        {
            throw new ArgumentException(
                "A complete editor draw group must contain at least one pass state.",
                nameof(passStates));
        }

        bool hasBlend = false;
        bool hasAlphaTest = false;
        bool usesOpaqueStateFallback = false;
        for (int passOrdinal = 0; passOrdinal < passStates.Count; passOrdinal++)
        {
            RenderState state = passStates[passOrdinal];
            if (!state.HasState)
            {
                usesOpaqueStateFallback = true;
                continue;
            }

            hasBlend |= state.BlendEnabled;
            hasAlphaTest |= state.AlphaTestEnabled;
        }

        MapRenderEditorDrawBucket bucket = hasBlend
            ? MapRenderEditorDrawBucket.Translucent
            : hasAlphaTest
                ? MapRenderEditorDrawBucket.AlphaTest
                : MapRenderEditorDrawBucket.Opaque;
        return new MapRenderEditorDrawBucketClassification(
            bucket,
            usesOpaqueStateFallback);
    }
}
