using IW4.Render.EditorPreview;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Single-consumer mailbox for revisioned render projections. A producer may
/// publish from any thread; an older revision can never replace newer pending
/// work. Divergent content at one revision is rejected at the boundary.
/// </summary>
internal sealed class MapRenderLiveSceneProjectionMailbox
{
    private MapRenderLiveSceneProjection? _pending;

    internal void Publish(MapRenderLiveSceneProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        while (true)
        {
            MapRenderLiveSceneProjection? current =
                Volatile.Read(ref _pending);
            if (current is not null)
            {
                if (projection.Revision < current.Revision)
                    return;
                if (projection.Revision == current.Revision)
                {
                    if (!string.Equals(
                            projection.ContentIdentity,
                            current.ContentIdentity,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Live Preview received divergent pending content for editor revision {projection.Revision}.");
                    }

                    return;
                }
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _pending,
                        projection,
                        current),
                    current))
            {
                return;
            }
        }
    }

    internal MapRenderLiveSceneProjection? Take() =>
        Interlocked.Exchange(ref _pending, null);

    internal void Clear() =>
        Volatile.Write(ref _pending, null);
}
