namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsSunShadowTraversalBuildResult
{
    private MapRenderWorldDpvsSunShadowTraversalBuildResult(
        MapRenderWorldDpvsSunShadowTraversal? traversal,
        MapRenderWorldDpvsSunShadowTraversalFailure? failure)
    {
        if ((traversal is null) == (failure is null))
        {
            throw new ArgumentException(
                "A secondary-view traversal result requires exactly one value or failure.");
        }
        Traversal = traversal;
        Failure = failure;
    }

    public MapRenderWorldDpvsSunShadowTraversal? Traversal { get; }

    public MapRenderWorldDpvsSunShadowTraversalFailure? Failure { get; }

    public bool IsSuccess => Traversal is not null;

    internal static MapRenderWorldDpvsSunShadowTraversalBuildResult Succeeded(
        MapRenderWorldDpvsSunShadowTraversal traversal) =>
        new(traversal, null);

    internal static MapRenderWorldDpvsSunShadowTraversalBuildResult Failed(
        MapRenderWorldDpvsSunShadowTraversalFailure failure) =>
        new(null, failure);
}

