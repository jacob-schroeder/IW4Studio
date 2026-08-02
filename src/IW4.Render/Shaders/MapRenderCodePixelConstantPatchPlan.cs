namespace IW4.Render.Shaders;

/// <summary>
/// Immutable translation result for one stable authored CodePixel argument.
/// Only directly resolved plans may source occurrence-owned runtime values.
/// </summary>
public sealed class MapRenderCodePixelConstantPatchPlan
{
    private readonly MapRenderCodePixelConstantPatchSite[] _patchSites;

    public MapRenderCodePixelConstantPatchPlan(
        int argumentOrdinal,
        ushort destination,
        int argumentRaw,
        ushort codeIndex,
        MapRenderCodePixelConstantPatchStatus status,
        IReadOnlyList<MapRenderCodePixelConstantPatchSite> patchSites,
        string? detail = null)
    {
        if (argumentOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(argumentOrdinal));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(patchSites);
        _patchSites = patchSites
            .Select(site => site is null
                ? throw new ArgumentException(
                    "CodePixel patch plans cannot contain null sites.",
                    nameof(patchSites))
                : site with { })
            .ToArray();
        if (_patchSites
            .Select(site => site.RelativePatchOffset)
            .Distinct()
            .Count() != _patchSites.Length)
        {
            throw new ArgumentException(
                "CodePixel patch offsets must be unique within one plan.",
                nameof(patchSites));
        }

        if (status ==
            MapRenderCodePixelConstantPatchStatus.DirectSourceResolved)
        {
            if (codeIndex >= MapRenderCodeConstantLayout.Float4Count)
                throw new ArgumentOutOfRangeException(nameof(codeIndex));
            if (_patchSites.Length == 0 ||
                _patchSites.Any(site => !site.InstructionIndex.HasValue))
            {
                throw new ArgumentException(
                    "Directly resolved CodePixel plans require exact matched patch sites.",
                    nameof(patchSites));
            }
        }

        ArgumentOrdinal = argumentOrdinal;
        Destination = destination;
        ArgumentRaw = argumentRaw;
        CodeIndex = codeIndex;
        Status = status;
        Detail = detail;
        PatchSites = Array.AsReadOnly(_patchSites);
    }

    public int ArgumentOrdinal { get; }

    public MapRenderSelectedPassConstantKind Kind =>
        MapRenderSelectedPassConstantKind.CodePixel;

    public ushort Destination { get; }

    public int ArgumentRaw { get; }

    public ushort CodeIndex { get; }

    public MapRenderCodePixelConstantPatchStatus Status { get; }

    public IReadOnlyList<MapRenderCodePixelConstantPatchSite> PatchSites { get; }

    public string? Detail { get; }

    public bool IsDirectSourceResolved =>
        Status ==
            MapRenderCodePixelConstantPatchStatus.DirectSourceResolved;
}
