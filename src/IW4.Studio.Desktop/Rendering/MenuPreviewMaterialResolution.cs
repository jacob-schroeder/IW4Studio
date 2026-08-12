using IW4.Render.UI;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// UI-neutral outcome of resolving and decoding one Menu material. Typed
/// renderer diagnostics survive the Studio boundary for presentation and
/// future higher-fidelity backends.
/// </summary>
public sealed class MenuPreviewMaterialResolution
{
    private readonly IReadOnlyList<UiMaterialPreviewDiagnostic> _diagnostics;
    private readonly IReadOnlyList<UiMaterialExecutionDiagnostic>
        _executionDiagnostics;

    private MenuPreviewMaterialResolution(
        MenuPreviewMaterialSnapshot? snapshot,
        string? failure,
        long poolRevision,
        IEnumerable<UiMaterialPreviewDiagnostic>? diagnostics,
        IEnumerable<UiMaterialExecutionDiagnostic>? executionDiagnostics)
    {
        Snapshot = snapshot;
        Failure = string.IsNullOrWhiteSpace(failure) ? null : failure.Trim();
        PoolRevision = poolRevision;
        UiMaterialPreviewDiagnostic[] diagnosticSnapshot =
            diagnostics?.ToArray() ?? [];
        _diagnostics = Array.AsReadOnly(diagnosticSnapshot);
        _executionDiagnostics = Array.AsReadOnly(
            executionDiagnostics?.ToArray() ?? []);
    }

    public MenuPreviewMaterialSnapshot? Snapshot { get; }

    public string? Failure { get; }

    public long PoolRevision { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Diagnostics =>
        _diagnostics;

    public IReadOnlyList<UiMaterialExecutionDiagnostic>
        ExecutionDiagnostics => _executionDiagnostics;

    public bool IsResolved => Snapshot is not null;

    public static MenuPreviewMaterialResolution Failed(
        string failure,
        long poolRevision = -1,
        IEnumerable<UiMaterialPreviewDiagnostic>? diagnostics = null,
        IEnumerable<UiMaterialExecutionDiagnostic>?
            executionDiagnostics = null) =>
        new(
            null,
            string.IsNullOrWhiteSpace(failure)
                ? throw new ArgumentException(
                    "A material-preview failure message is required.",
                    nameof(failure))
                : failure,
            poolRevision,
            diagnostics,
            executionDiagnostics);

    internal static MenuPreviewMaterialResolution Resolved(
        MenuPreviewMaterialSnapshot snapshot,
        long poolRevision) =>
        new(
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
            null,
            poolRevision,
            snapshot.Diagnostics,
            ActiveExecutionDiagnostics(snapshot));

    private static IEnumerable<UiMaterialExecutionDiagnostic>
        ActiveExecutionDiagnostics(MenuPreviewMaterialSnapshot snapshot)
    {
        IEnumerable<UiMaterialExecutionDiagnostic> generic =
            snapshot.ExecutionDiagnostics;
        if (snapshot.CpuPreviewCompositeState is not null)
        {
            generic = generic.Where(diagnostic =>
                diagnostic.Code !=
                UiMaterialExecutionDiagnosticCode.UnsupportedMaterialState);
        }

        return generic.Concat(snapshot.CpuPreviewDiagnostics);
    }

    public MenuPreviewMaterialStatus CreateStatus(string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        string[] warnings = Diagnostics
            .Where(diagnostic => diagnostic.Severity is
                UiDiagnosticSeverity.Warning or
                UiDiagnosticSeverity.Blocker)
            .Select(diagnostic => diagnostic.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        warnings = warnings
            .Concat(ExecutionDiagnostics
                .Where(diagnostic => diagnostic.Severity is
                    UiDiagnosticSeverity.Warning or
                    UiDiagnosticSeverity.Blocker)
                .Select(diagnostic => diagnostic.Message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (Snapshot is not { } snapshot)
        {
            string failureDetail =
                Failure ?? "Material preview is unavailable.";
            string[] supplementalWarnings = warnings
                .Where(warning => !failureDetail.Contains(
                    warning,
                    StringComparison.Ordinal))
                .ToArray();
            if (supplementalWarnings.Length > 0)
            {
                failureDetail =
                    $"{failureDetail} " +
                    string.Join(" ", supplementalWarnings);
            }
            return new MenuPreviewMaterialStatus(
                requestedName,
                false,
                warnings.Length,
                failureDetail);
        }

        string materialIdentity = string.Equals(
            requestedName,
            snapshot.MaterialName,
            StringComparison.Ordinal)
                ? requestedName
                : $"{requestedName} [{snapshot.MaterialName}]";
        string summary =
            $"{materialIdentity} → {snapshot.ImageName} ({snapshot.Role})";
        var metadata = new List<string>
        {
            $"{snapshot.Width:N0}×{snapshot.Height:N0} {snapshot.Format}",
            snapshot.HasTransparency ? "alpha" : "opaque",
            snapshot.CpuPreviewCompositeState is not null
                ? "texture approximation with decoded fixed-function state"
                : snapshot.Fidelity switch
                {
                    UiMaterialPreviewFidelity.TextureApproximation =>
                        "texture approximation",
                    _ => "unavailable"
                }
        };
        if (snapshot.Atlas.IsEnabled)
        {
            metadata.Add(
                $"atlas {snapshot.Atlas.AuthoredRowCount}×" +
                snapshot.Atlas.AuthoredColumnCount);
        }
        if (snapshot.SamplerState is { } sampler)
        {
            metadata.Add(
                $"sampler {sampler.MinFilter}/{sampler.MagFilter}, " +
                $"{sampler.AddressU}/{sampler.AddressV}");
        }
        if (snapshot.ExecutionTemplate is { } execution)
        {
            metadata.Add(
                $"{execution.Identity.TechniqueSetName}" +
                $"[{execution.Identity.TechniqueSlot}]/" +
                execution.Identity.TechniqueName);
            metadata.Add(execution.ShaderExecution.ProgramExecutionStatus);
        }

        string detail = $"{summary}; {string.Join("; ", metadata)}.";
        if (warnings.Length > 0)
            detail = $"{detail} {string.Join(" ", warnings)}";
        return new MenuPreviewMaterialStatus(
            requestedName,
            true,
            warnings.Length,
            detail);
    }
}
