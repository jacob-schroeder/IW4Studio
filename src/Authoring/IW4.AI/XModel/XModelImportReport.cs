namespace IW4.AI.XModel;

/// <summary>Known editor context. A null count means the map was unavailable.</summary>
public sealed record XModelUsage(int? AuthoredPlacements = null, bool PlayerBody = false, bool ViewHands = false)
{
    public string? Description
    {
        get
        {
            var details = new List<string>();
            if (AuthoredPlacements is int count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                details.Add(count switch
                {
                    0 => "No authored placements in this map.",
                    1 => "One authored placement in this map.",
                    _ => $"{count:N0} authored placements in this map. Multiple copies may be visible together."
                });
            }
            if (PlayerBody)
                details.Add("Player body. Several visible players may use this model at once.");
            if (ViewHands)
                details.Add("First-person hands. This model can stay in view during play.");
            return details.Count == 0 ? null : string.Join(" ", details);
        }
    }
}

public sealed record XModelLodAssessment(int LodIndex, LodFeatures? Features, ImportAssessment? Assessment);

public sealed record XModelImportReport(
    IReadOnlyList<XModelLodAssessment> Lods,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> LodTransitions,
    string? UsageDescription)
{
    // Origin tags are for the import interruption only; the public report stays unchanged.
    internal IReadOnlyList<(int LodIndex, int? AdjacentLodIndex, string Reason)> ImportRisks { get; init; } = [];

    public IReadOnlyList<string> ImportRisksFor(int? lodIndex) => ImportRisks
        .Where(risk => lodIndex is null || risk.LodIndex == lodIndex || risk.AdjacentLodIndex == lodIndex)
        .Select(risk => risk.Reason)
        .ToArray();
}
