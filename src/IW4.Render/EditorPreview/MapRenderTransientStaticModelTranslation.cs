using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Renderer-local translation draft layered over the latest committed
/// semantic projection. It has no document revision because it is neither a
/// semantic edit nor a persistence input.
/// </summary>
internal readonly record struct MapRenderTransientStaticModelTranslation
{
    internal MapRenderTransientStaticModelTranslation(
        int sourceOrdinal,
        Vector3 gameOrigin)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!IsFinite(gameOrigin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameOrigin),
                "Transient static-model origins must be finite.");
        }

        SourceOrdinal = sourceOrdinal;
        GameOrigin = gameOrigin;
    }

    internal int SourceOrdinal { get; }

    internal Vector3 GameOrigin { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

/// <summary>
/// Pure composition policy for one renderer-local translation draft. The
/// complete committed catalog remains the authority for cardinality,
/// visibility, every other origin, and the semantic revision.
/// </summary>
internal static class MapRenderTransientStaticModelProjectionComposer
{
    internal static MapRenderLiveSceneProjection Compose(
        MapRenderLiveSceneProjection committed,
        MapRenderTransientStaticModelTranslation? transient)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (transient is null)
            return committed;
        if (!committed.HasStaticModelTranslationCatalog)
        {
            throw new InvalidOperationException(
                "A transient static-model translation requires a committed authoritative static-model catalog.");
        }

        IReadOnlyList<MapRenderLiveStaticModelTranslation> catalog =
            committed.StaticModelTranslations;
        for (int sourceOrdinal = 0;
             sourceOrdinal < catalog.Count;
             sourceOrdinal++)
        {
            if (catalog[sourceOrdinal].SourceOrdinal != sourceOrdinal)
            {
                throw new InvalidOperationException(
                    $"The committed static-model catalog has no row for source ordinal {sourceOrdinal}.");
            }
        }

        MapRenderTransientStaticModelTranslation draft = transient.Value;
        if ((uint)draft.SourceOrdinal >= (uint)catalog.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transient),
                $"Transient static-model source ordinal {draft.SourceOrdinal} is outside the committed catalog of {catalog.Count} rows.");
        }

        MapRenderLiveStaticModelTranslation[] effective =
            catalog.ToArray();
        MapRenderLiveStaticModelTranslation baseline =
            effective[draft.SourceOrdinal];
        effective[draft.SourceOrdinal] =
            new MapRenderLiveStaticModelTranslation(
                baseline.SourceOrdinal,
                draft.GameOrigin,
                baseline.IsVisible);
        return new MapRenderLiveSceneProjection(
            committed.Revision,
            committed.PrimaryLights,
            effective);
    }
}
