namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsCameraCellResolutionResult
{
    private MapRenderWorldDpvsCameraCellResolutionResult(
        int? cellIndex,
        MapRenderWorldDpvsCameraCellFailure? failure)
    {
        if ((cellIndex is null) == (failure is null))
            throw new ArgumentException("Camera-cell resolution requires exactly one cell value or failure.");
        CellIndex = cellIndex;
        Failure = failure;
    }

    /// <summary>
    /// PS3 return value. Minus one is a successful traversal whose leaf lies
    /// outside the world-cell array.
    /// </summary>
    public int? CellIndex { get; }

    public MapRenderWorldDpvsCameraCellFailure? Failure { get; }

    public bool IsSuccess => CellIndex.HasValue;

    internal static MapRenderWorldDpvsCameraCellResolutionResult Succeeded(
        int cellIndex) => new(cellIndex, null);

    internal static MapRenderWorldDpvsCameraCellResolutionResult Failed(
        MapRenderWorldDpvsCameraCellFailure failure) => new(null, failure);
}
