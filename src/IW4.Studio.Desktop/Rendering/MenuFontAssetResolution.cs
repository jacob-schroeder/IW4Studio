using IW4.Assets.Assets.Font;
using IW4.Render.UI.Text;

namespace IW4.Studio.Desktop.Rendering;

public enum MenuFontAssetResolutionStatus
{
    Resolved = 0,
    UnknownFontEnum = 1,
    MissingFontAsset = 2,
    ResourceRevisionChanged = 3
}

/// <summary>
/// Typed canonical-font lookup result. A known enum mapping and a complete
/// active Font provider are separate decisions so preview diagnostics can
/// distinguish missing scenario inputs from missing dependencies.
/// </summary>
public sealed class MenuFontAssetResolution
{
    private readonly UiTextDiagnostic[] _diagnostics;

    private MenuFontAssetResolution(
        MenuFontAssetResolutionStatus status,
        MenuFontEnumResolution mapping,
        FontAsset? font,
        MenuTextResourceRevision resourceRevision,
        IEnumerable<UiTextDiagnostic> diagnostics)
    {
        Status = status;
        Mapping = mapping;
        Font = font;
        ResourceRevision = resourceRevision;
        _diagnostics = diagnostics.ToArray();
        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    public MenuFontAssetResolutionStatus Status { get; }

    public MenuFontEnumResolution Mapping { get; }

    public FontAsset? Font { get; }

    public MenuTextResourceRevision ResourceRevision { get; }

    public IReadOnlyList<UiTextDiagnostic> Diagnostics { get; }

    public bool IsResolved =>
        Status == MenuFontAssetResolutionStatus.Resolved && Font is not null;

    internal static MenuFontAssetResolution Resolved(
        MenuFontEnumResolution mapping,
        FontAsset font,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuFontAssetResolutionStatus.Resolved,
            mapping,
            font,
            resourceRevision,
            []);

    internal static MenuFontAssetResolution Unknown(
        MenuFontEnumResolution mapping,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuFontAssetResolutionStatus.UnknownFontEnum,
            mapping,
            null,
            resourceRevision,
            [
                new UiTextDiagnostic(
                    UiTextDiagnosticCode.UnknownFontEnum,
                    UiTextDiagnosticSeverity.Blocker,
                    mapping.Failure ??
                    $"Font enum {mapping.FontEnum} cannot be mapped.")
            ]);

    internal static MenuFontAssetResolution Missing(
        MenuFontEnumResolution mapping,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuFontAssetResolutionStatus.MissingFontAsset,
            mapping,
            null,
            resourceRevision,
            [
                new UiTextDiagnostic(
                    UiTextDiagnosticCode.FontAssetNotFound,
                    UiTextDiagnosticSeverity.Blocker,
                    $"Font '{mapping.LookupName}' is not available from a complete active provider in the asset pool.")
            ]);

    internal static MenuFontAssetResolution RevisionChanged(
        MenuFontEnumResolution mapping,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuFontAssetResolutionStatus.ResourceRevisionChanged,
            mapping,
            null,
            resourceRevision,
            [
                new UiTextDiagnostic(
                    UiTextDiagnosticCode.TextResourcesChanged,
                    UiTextDiagnosticSeverity.Blocker,
                    $"Menu text resources changed while Font " +
                    $"'{mapping.LookupName}' was being resolved.")
            ]);
}
