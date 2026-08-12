using IW4.Assets.Assets.Image;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.UI;

/// <summary>
/// Backend-neutral decision for a UI material preview. It identifies the
/// texture that a host may decode or upload, but contains no graphics-API or
/// presentation-framework resource.
/// </summary>
public sealed class UiMaterialPreviewPlan
{
    private readonly UiMaterialPreviewDiagnostic[] _diagnostics;
    private readonly UiMaterialPreviewDiagnostic[] _blockers;

    internal UiMaterialPreviewPlan(
        UiMaterialPreviewMaterialMetadata material,
        UiMaterialPreviewAtlasMetadata atlas,
        MapRenderEditorMaterialTexturePlan textureTable,
        MapRenderEditorMaterialTextureBinding? selectedTexture,
        UiMaterialPreviewImageAuthority selectedImageAuthority,
        UiMaterialPreviewImageMetadata? selectedImageMetadata,
        UiMaterialPreviewFidelity fidelity,
        IReadOnlyList<UiMaterialPreviewDiagnostic> diagnostics)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
        Atlas = atlas;
        TextureTable = textureTable ??
            throw new ArgumentNullException(nameof(textureTable));
        SelectedTexture = selectedTexture;
        SelectedImageAuthority = selectedImageAuthority;
        SelectedImageMetadata = selectedImageMetadata;
        Fidelity = fidelity;

        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics.ToArray();
        _blockers = _diagnostics
            .Where(diagnostic =>
                diagnostic.Severity ==
                UiDiagnosticSeverity.Blocker)
            .ToArray();
        Diagnostics = Array.AsReadOnly(_diagnostics);
        Blockers = Array.AsReadOnly(_blockers);
    }

    public UiMaterialPreviewMaterialMetadata Material { get; }

    public UiMaterialPreviewAtlasMetadata Atlas { get; }

    public MapRenderEditorMaterialTexturePlan TextureTable { get; }

    public MapRenderEditorMaterialTextureBinding? SelectedTexture { get; }

    public GfxImageAsset? SelectedImage => SelectedTexture?.Image;

    public MapRenderSamplerState? SelectedSamplerState =>
        SelectedTexture?.DecodedSamplerState;

    public UiMaterialPreviewImageAuthority SelectedImageAuthority { get; }

    public UiMaterialPreviewImageMetadata? SelectedImageMetadata { get; }

    public UiMaterialPreviewFidelity Fidelity { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Diagnostics { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Blockers { get; }

    public bool CanAttemptTextureDecode =>
        SelectedImage is not null && Blockers.Count == 0;
}
