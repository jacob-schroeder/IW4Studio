using IW4.Render.Materials;
using IW4.Render.Textures;
using IW4.Render.UI;

namespace IW4.Studio.Rendering;

/// <summary>
/// Immutable, UI-neutral material preview payload. Callers receive their own
/// PNG copy so neither Avalonia nor another presentation framework leaks into
/// the Studio document and rendering boundaries.
/// </summary>
public sealed class MenuPreviewMaterialSnapshot
{
    private readonly byte[] _pngBytes;
    private readonly IReadOnlyList<UiMaterialPreviewDiagnostic> _diagnostics;

    internal MenuPreviewMaterialSnapshot(
        UiMaterialPreviewPlan plan,
        GfxImagePreviewSnapshot preview)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(preview);

        MaterialName = plan.Material.Name;
        ImageName = plan.SelectedImageMetadata?.Name ?? preview.Name;
        Role = plan.SelectedTexture?.Role ??
            MapRenderEditorMaterialTextureRole.Unknown;
        Width = preview.Width;
        Height = preview.Height;
        Format = preview.Format;
        HasTransparency = preview.HasTransparency;
        Fidelity = plan.Fidelity;
        Atlas = plan.Atlas;
        SamplerState = plan.SelectedSamplerState;
        _diagnostics = Array.AsReadOnly(plan.Diagnostics.ToArray());
        _pngBytes = preview.GetPngBytesCopy();
    }

    public string MaterialName { get; }

    public string ImageName { get; }

    public MapRenderEditorMaterialTextureRole Role { get; }

    public int Width { get; }

    public int Height { get; }

    public string Format { get; }

    public bool HasTransparency { get; }

    public UiMaterialPreviewFidelity Fidelity { get; }

    public UiMaterialPreviewAtlasMetadata Atlas { get; }

    public MapRenderSamplerState? SamplerState { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Diagnostics =>
        _diagnostics;

    public byte[] GetPngBytesCopy() => _pngBytes.ToArray();
}
