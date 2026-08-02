using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Validation;

public enum MapPreservationCoverageStatus
{
    Incomplete,
    Proven
}

/// <summary>
/// Reviewable evidence boundary for one narrow imported-asset patcher. A
/// patcher is not registered with the save planner unless its coverage is
/// proven and its validator checks every declared preserved field.
/// </summary>
public sealed class MapPreservationCoverage
{
    private readonly IReadOnlyList<string> _preservedFields;
    private readonly IReadOnlyList<string> _mutableFields;

    public MapPreservationCoverage(
        MapAssetKind assetKind,
        string patchCapability,
        MapPreservationCoverageStatus status,
        IEnumerable<string> preservedFields,
        IEnumerable<string> mutableFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patchCapability);
        ArgumentNullException.ThrowIfNull(preservedFields);
        ArgumentNullException.ThrowIfNull(mutableFields);

        string[] preserved = preservedFields
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] mutable = mutableFields
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (status == MapPreservationCoverageStatus.Proven &&
            (preserved.Length == 0 || mutable.Length == 0))
        {
            throw new ArgumentException(
                "Proven preservation coverage requires explicit preserved and mutable field sets.");
        }

        AssetKind = assetKind;
        PatchCapability = patchCapability;
        Status = status;
        _preservedFields = new ReadOnlyCollection<string>(preserved);
        _mutableFields = new ReadOnlyCollection<string>(mutable);
    }

    public MapAssetKind AssetKind { get; }
    public string PatchCapability { get; }
    public MapPreservationCoverageStatus Status { get; }
    public IReadOnlyList<string> PreservedFields => _preservedFields;
    public IReadOnlyList<string> MutableFields => _mutableFields;
    public bool IsProven => Status == MapPreservationCoverageStatus.Proven;
}

public sealed class MapPatchValidation
{
    private readonly IReadOnlyList<string> _diagnostics;

    public MapPatchValidation(IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = new ReadOnlyCollection<string>(
            diagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public bool IsValid => _diagnostics.Count == 0;
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public static MapPatchValidation Valid { get; } = new([]);
}
