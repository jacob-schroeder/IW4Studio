using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// Immutable active direct-table ownership authorized for one translated
/// program. Dynamic rows remain unavailable until their runtime producer
/// publishes them, rather than becoming implicit zero backend bindings.
/// </summary>
public sealed class TranslatedProgramDirectCodeConstantPlan
{
    private readonly Dictionary<ushort, DirectCodeConstantRow> _rows;

    internal TranslatedProgramDirectCodeConstantPlan(
        string producerIdentity,
        IReadOnlyList<DirectCodeConstantRow> rows,
        IReadOnlySet<ushort>? dynamicSourceRows = null,
        int? sceneLightIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        ArgumentNullException.ThrowIfNull(rows);
        DirectCodeConstantRow[] copied = rows.Select(row => row is null
            ? throw new ArgumentException("Editor direct-code plans cannot contain null rows.", nameof(rows))
            : new DirectCodeConstantRow(row.SourceRowIndex, row.Value)).ToArray();
        if (copied.Any(row => !IsFinite(row.Value)))
            throw new ArgumentException("Editor direct-code plans cannot contain non-finite values.", nameof(rows));
        _rows = new(copied.Length);
        foreach (DirectCodeConstantRow row in copied)
        {
            ushort sourceRow = checked((ushort)row.SourceRowIndex);
            if (!_rows.TryAdd(sourceRow, row))
                throw new ArgumentException($"Editor direct-code row 0x{sourceRow:X2} is duplicated.", nameof(rows));
        }
        ushort[] dynamicRows = (dynamicSourceRows ?? new HashSet<ushort>()).Distinct().Order().ToArray();
        if (dynamicRows.Any(row => !_rows.ContainsKey(row) && !TranslatedProgramDirectCodeConstantRows.IsRuntimeOwnedSourceRow(row)))
            throw new ArgumentException("Dynamic direct-code rows without placeholder values must have a supported runtime owner.", nameof(dynamicSourceRows));
        if (sceneLightIndex is < 1 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if (dynamicRows.Any(TranslatedProgramDirectCodeConstantRows.IsDynamicSceneLightSourceRow) && !sceneLightIndex.HasValue)
            throw new ArgumentException("A dynamic scene-light row requires exact invocation light identity.", nameof(sceneLightIndex));
        ProducerIdentity = producerIdentity;
        Rows = Array.AsReadOnly(copied);
        DynamicSourceRows = Array.AsReadOnly(dynamicRows);
        SceneLightIndex = sceneLightIndex;
    }

    public string ProducerIdentity { get; }
    public IReadOnlyList<DirectCodeConstantRow> Rows { get; }
    public IReadOnlyList<ushort> DynamicSourceRows { get; }
    public int? SceneLightIndex { get; }
    public bool IsDynamicSourceRow(ushort sourceRowIndex) => DynamicSourceRows.Contains(sourceRowIndex);
    public bool TryGetRow(ushort sourceRowIndex, out DirectCodeConstantRow? row) => _rows.TryGetValue(sourceRowIndex, out row);
    private static bool IsFinite(ShaderConstantValue value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

public sealed record TranslatedProgramDirectCodeConstantPlanBuildResult(
    TranslatedProgramDirectCodeConstantPlan? Plan,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Plan is not null && Blockers.Count == 0;
}
