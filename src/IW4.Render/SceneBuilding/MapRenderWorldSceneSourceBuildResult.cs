namespace IW4.Render.SceneBuilding;

/// <summary>
/// Distinguishes a non-world scene from a world whose canonical source could
/// not be retained. EditorPreview construction remains available in the latter
/// case with an explicit source failure.
/// </summary>
public sealed class MapRenderWorldSceneSourceBuildResult
{
    private MapRenderWorldSceneSourceBuildResult(
        bool hasWorld,
        MapRenderWorldSceneSource? source,
        MapRenderWorldSceneSourceBuildFailure? failure)
    {
        if (!hasWorld && (source is not null || failure is not null))
        {
            throw new ArgumentException(
                "A non-world scene cannot retain world-source state.");
        }
        if (hasWorld && ((source is null) == (failure is null)))
        {
            throw new ArgumentException(
                "A world scene requires exactly one canonical source or failure.");
        }

        HasWorld = hasWorld;
        Source = source;
        Failure = failure;
    }

    public bool HasWorld { get; }

    public MapRenderWorldSceneSource? Source { get; }

    public MapRenderWorldSceneSourceBuildFailure? Failure { get; }

    public bool IsReady => Source is not null;

    public static MapRenderWorldSceneSourceBuildResult NoWorld { get; } =
        new(false, null, null);

    internal static MapRenderWorldSceneSourceBuildResult Succeeded(
        MapRenderWorldSceneSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(true, source, null);
    }

    internal static MapRenderWorldSceneSourceBuildResult Failed(
        MapRenderWorldSceneSourceBuildFailureKind kind,
        string detail)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(true, null, new(kind, detail));
    }
}
