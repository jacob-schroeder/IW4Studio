using IW4.Render.UI;

namespace IW4.Studio.Rendering;

/// <summary>
/// UI-neutral outcome of resolving and decoding one Menu material. Typed
/// renderer diagnostics survive the Studio boundary for presentation and
/// future higher-fidelity backends.
/// </summary>
public sealed class MenuPreviewMaterialResolution
{
    private readonly IReadOnlyList<UiMaterialPreviewDiagnostic> _diagnostics;

    private MenuPreviewMaterialResolution(
        MenuPreviewMaterialSnapshot? snapshot,
        string? failure,
        long poolRevision,
        IEnumerable<UiMaterialPreviewDiagnostic>? diagnostics)
    {
        Snapshot = snapshot;
        Failure = string.IsNullOrWhiteSpace(failure) ? null : failure.Trim();
        PoolRevision = poolRevision;
        UiMaterialPreviewDiagnostic[] diagnosticSnapshot =
            diagnostics?.ToArray() ?? [];
        _diagnostics = Array.AsReadOnly(diagnosticSnapshot);
    }

    public MenuPreviewMaterialSnapshot? Snapshot { get; }

    public string? Failure { get; }

    public long PoolRevision { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Diagnostics =>
        _diagnostics;

    public bool IsResolved => Snapshot is not null;

    public static MenuPreviewMaterialResolution Failed(
        string failure,
        long poolRevision = -1,
        IEnumerable<UiMaterialPreviewDiagnostic>? diagnostics = null) =>
        new(
            null,
            string.IsNullOrWhiteSpace(failure)
                ? throw new ArgumentException(
                    "A material-preview failure message is required.",
                    nameof(failure))
                : failure,
            poolRevision,
            diagnostics);

    internal static MenuPreviewMaterialResolution Resolved(
        MenuPreviewMaterialSnapshot snapshot,
        long poolRevision) =>
        new(
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
            null,
            poolRevision,
            snapshot.Diagnostics);

    public MenuPreviewMaterialStatus CreateStatus(string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        string[] warnings = Diagnostics
            .Where(diagnostic => diagnostic.Severity is
                UiMaterialPreviewDiagnosticSeverity.Warning or
                UiMaterialPreviewDiagnosticSeverity.Blocker)
            .Select(diagnostic => diagnostic.Message)
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
            snapshot.Fidelity switch
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
