namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsCameraTraversalBuildResult
{
    private MapRenderWorldDpvsCameraTraversalBuildResult(
        MapRenderWorldDpvsCameraTraversal? traversal,
        MapRenderWorldDpvsCameraTraversalFailure? failure)
    {
        Traversal = traversal;
        Failure = failure;
    }

    public MapRenderWorldDpvsCameraTraversal? Traversal { get; }

    public MapRenderWorldDpvsCameraTraversalFailure? Failure { get; }

    public bool IsSuccess => Traversal is not null && Failure is null;

    public static MapRenderWorldDpvsCameraTraversalBuildResult Succeeded(
        MapRenderWorldDpvsCameraTraversal traversal) => new(traversal, null);

    public static MapRenderWorldDpvsCameraTraversalBuildResult Failed(
        MapRenderWorldDpvsCameraTraversalFailure failure) => new(null, failure);
}
