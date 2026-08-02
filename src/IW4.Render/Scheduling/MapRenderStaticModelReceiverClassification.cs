namespace IW4.Render.Scheduling;

/// <summary>
/// One same-revision static receiver classification. A null page is an
/// explicit exclusion; callers must not substitute another page.
/// </summary>
public readonly record struct MapRenderStaticModelReceiverClassification
{
    internal MapRenderStaticModelReceiverClassification(
        MapRenderStaticModelReceiverIdentity identity,
        MapRenderStaticModelReceiverPage? page)
    {
        Identity = identity;
        Page = page;
    }

    public MapRenderStaticModelReceiverIdentity Identity { get; }

    public MapRenderStaticModelReceiverPage? Page { get; }

    public bool IsIncluded => Page is not null;
}
