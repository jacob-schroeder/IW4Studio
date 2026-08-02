using System.Collections.ObjectModel;

namespace IW4.Studio.MapEditor.Editing.SavePlanning;

public sealed record MapSavePlanEntry(
    MapPendingEdit Edit,
    MapEditImpact Impact);

public enum MapSavePlanNormalizationKind
{
    VerifiedNetZeroOmission
}

/// <summary>
/// Auditable proof that a serialized command-journal subset does not require
/// candidate mutation. The original entries remain in the plan; this record
/// explains why the normalized subset is intentionally omitted from staging.
/// </summary>
public sealed class MapSavePlanNormalization
{
    private readonly IReadOnlyList<MapPendingEdit> _edits;

    internal MapSavePlanNormalization(
        MapSavePlanNormalizationKind kind,
        MapAssetKind assetKind,
        IEnumerable<MapPendingEdit> edits,
        string baselineContentDigest,
        string candidateContentDigest,
        string evidence)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(assetKind))
            throw new ArgumentOutOfRangeException(nameof(assetKind));
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineContentDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateContentDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);

        MapPendingEdit[] editCopy = edits.ToArray();
        if (editCopy.Length == 0 ||
            editCopy.Any(edit => edit is null))
        {
            throw new ArgumentException(
                "A save-plan normalization requires at least one exact edit.",
                nameof(edits));
        }
        if (kind == MapSavePlanNormalizationKind.VerifiedNetZeroOmission &&
            !string.Equals(
                baselineContentDigest,
                candidateContentDigest,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A verified net-zero omission requires equal baseline and " +
                "candidate content digests.",
                nameof(candidateContentDigest));
        }

        Kind = kind;
        AssetKind = assetKind;
        _edits = new ReadOnlyCollection<MapPendingEdit>(editCopy);
        BaselineContentDigest = baselineContentDigest;
        CandidateContentDigest = candidateContentDigest;
        Evidence = evidence;
    }

    public MapSavePlanNormalizationKind Kind { get; }
    public MapAssetKind AssetKind { get; }
    public IReadOnlyList<MapPendingEdit> Edits => _edits;
    public string BaselineContentDigest { get; }
    public string CandidateContentDigest { get; }
    public string Evidence { get; }
}

public sealed class MapSavePlan
{
    private readonly IReadOnlyList<MapSavePlanEntry> _entries;
    private readonly IReadOnlyList<string> _blockers;
    private readonly IReadOnlyList<MapSavePlanNormalization> _normalizations;

    internal MapSavePlan(
        long documentRevision,
        long sourcePoolRevision,
        long sourceEditingSessionRevision,
        string baselineDigest,
        IEnumerable<MapSavePlanEntry> entries,
        IEnumerable<string> blockers,
        IEnumerable<MapSavePlanNormalization>? normalizations = null)
    {
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        if (sourcePoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourcePoolRevision));
        if (sourceEditingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceEditingSessionRevision));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDigest);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(blockers);
        normalizations ??= [];

        DocumentRevision = documentRevision;
        SourcePoolRevision = sourcePoolRevision;
        SourceEditingSessionRevision = sourceEditingSessionRevision;
        BaselineDigest = baselineDigest;
        MapSavePlanEntry[] entryCopy = entries.ToArray();
        if (entryCopy.Any(entry =>
                entry is null ||
                entry.Edit is null ||
                entry.Impact is null))
        {
            throw new ArgumentException(
                "Save plans cannot contain null entries, edits, or impacts.",
                nameof(entries));
        }

        _entries = new ReadOnlyCollection<MapSavePlanEntry>(entryCopy);
        MapSavePlanNormalization[] normalizationCopy =
            normalizations.ToArray();
        if (normalizationCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Save plans cannot contain null normalizations.",
                nameof(normalizations));
        }
        if (normalizationCopy
            .SelectMany(value => value.Edits)
            .Any(normalizedEdit =>
                !entryCopy.Any(entry =>
                    ReferenceEquals(
                        entry.Edit,
                        normalizedEdit))))
        {
            throw new ArgumentException(
                "Every normalized edit must be retained as the exact " +
                "corresponding save-plan entry.",
                nameof(normalizations));
        }
        _normalizations =
            new ReadOnlyCollection<MapSavePlanNormalization>(
                normalizationCopy);
        IEnumerable<string> unsafeEntryBlockers = entryCopy
            .Where(entry => !IsSaveable(entry.Impact.Classification))
            .Select(entry =>
                $"{entry.Edit.Description}: " +
                (entry.Impact.SaveBlocker ??
                 $"classification {entry.Impact.Classification} is not saveable."));
        _blockers = new ReadOnlyCollection<string>(
            blockers.Concat(unsafeEntryBlockers)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    internal MapSavePlan(
        long documentRevision,
        long sourcePoolRevision,
        string baselineDigest,
        IEnumerable<MapSavePlanEntry> entries,
        IEnumerable<string> blockers)
        : this(
            documentRevision,
            sourcePoolRevision,
            sourceEditingSessionRevision: 0,
            baselineDigest,
            entries,
            blockers)
    {
    }

    public long DocumentRevision { get; }
    public long SourcePoolRevision { get; }
    public long SourceEditingSessionRevision { get; }
    public string BaselineDigest { get; }
    public IReadOnlyList<MapSavePlanEntry> Entries => _entries;
    public IReadOnlyList<string> Blockers => _blockers;
    public IReadOnlyList<MapSavePlanNormalization> Normalizations =>
        _normalizations;
    public bool HasNormalizations => _normalizations.Count != 0;
    public bool CanSave =>
        _blockers.Count == 0 &&
        _entries.All(entry => IsSaveable(entry.Impact.Classification));
    public bool HasSerializedEdits => _entries.Any(
        entry =>
            entry.Impact.Classification !=
                MapSaveClassification.EditorOnly &&
            !_normalizations
                .SelectMany(value => value.Edits)
                .Any(normalizedEdit =>
                    ReferenceEquals(
                        normalizedEdit,
                        entry.Edit)));

    private static bool IsSaveable(MapSaveClassification classification) =>
        classification is
            MapSaveClassification.EditorOnly or
            MapSaveClassification.PatchSaveable;
}
