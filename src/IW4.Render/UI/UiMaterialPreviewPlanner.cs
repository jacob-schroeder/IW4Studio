using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Materials;

namespace IW4.Render.UI;

/// <summary>
/// Chooses one deterministic texture-only approximation for an IW4 UI
/// material. Canonical asset selection is injected by the host; shader and
/// backend execution remain outside this planner.
/// </summary>
public static class UiMaterialPreviewPlanner
{
    public static UiMaterialPreviewPlan Plan(
        MaterialAsset material,
        Func<int, MaterialTextureDef,
            UiMaterialPreviewImageResolution?>? resolveImage = null)
    {
        ArgumentNullException.ThrowIfNull(material);

        IReadOnlyList<MaterialTextureDef> textures = material.Textures ??
            throw new InvalidDataException(
                "A UI material preview requires a texture table collection.");
        var resolutions = new UiMaterialPreviewImageResolution[textures.Count];
        var diagnostics = new List<UiMaterialPreviewDiagnostic>();
        for (int ordinal = 0; ordinal < textures.Count; ordinal++)
        {
            MaterialTextureDef row = textures[ordinal] ??
                throw new InvalidDataException(
                    $"Material texture row {ordinal} is null.");
            UiMaterialPreviewImageResolution resolution = resolveImage is null
                ? ResolveMaterialRowFallback(row)
                : resolveImage(ordinal, row) ??
                  UiMaterialPreviewImageResolution.Unavailable(
                      "The canonical image resolver returned no result.");
            resolutions[ordinal] = resolution;

            if (resolution.Image is null)
            {
                diagnostics.Add(new UiMaterialPreviewDiagnostic(
                    UiMaterialPreviewDiagnosticCode
                        .TextureImageResolutionFailed,
                    UiMaterialPreviewDiagnosticSeverity.Warning,
                    resolution.Failure ??
                    "The material texture row has no resolved image provider.",
                    ordinal));
            }
        }

        MapRenderEditorMaterialTexturePlan textureTable =
            MapRenderEditorMaterialTexturePlanner.Plan(
                textures,
                (ordinal, _) =>
                    new MapRenderEditorMaterialTextureResolution(
                        resolutions[ordinal].Image,
                        null));
        UiMaterialPreviewAtlasMetadata atlas = new(
            material.Info.TextureAtlasRowCount,
            material.Info.TextureAtlasColumnCount);
        if (!atlas.IsValid)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.InvalidTextureAtlas,
                UiMaterialPreviewDiagnosticSeverity.Warning,
                $"Material atlas dimensions {atlas.AuthoredRowCount}x" +
                $"{atlas.AuthoredColumnCount} are incomplete; the full " +
                "texture will be shown."));
        }
        else if (atlas.EffectiveCellCount > 1)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.TextureAtlasFrameNotEvaluated,
                UiMaterialPreviewDiagnosticSeverity.Warning,
                "Material atlas frame selection is not evaluated; the full " +
                $"{atlas.AuthoredRowCount}x{atlas.AuthoredColumnCount} " +
                "texture atlas will be shown."));
        }

        MapRenderEditorMaterialTextureBinding? selected = SelectTexture(
            textureTable,
            diagnostics);
        UiMaterialPreviewImageAuthority selectedAuthority =
            UiMaterialPreviewImageAuthority.None;
        UiMaterialPreviewImageMetadata? selectedImageMetadata = null;
        if (selected is null || selected.Image is null)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                textures.Count == 0
                    ? UiMaterialPreviewDiagnosticCode.MaterialHasNoTextures
                    : UiMaterialPreviewDiagnosticCode.NoResolvedTextureImage,
                UiMaterialPreviewDiagnosticSeverity.Blocker,
                textures.Count == 0
                    ? "The material has no texture rows to preview."
                    : "None of the material texture rows resolves to an image that can be previewed."));
        }
        else
        {
            UiMaterialPreviewImageResolution selectedResolution =
                resolutions[selected.TextureTableOrdinal];
            selectedAuthority = selectedResolution.Authority;
            GfxImageAsset image = selected.Image;
            selectedImageMetadata = SnapshotImage(image);

            if (selectedAuthority !=
                UiMaterialPreviewImageAuthority.CanonicalProvider)
            {
                diagnostics.Add(new UiMaterialPreviewDiagnostic(
                    UiMaterialPreviewDiagnosticCode
                        .NonCanonicalImageFallback,
                    UiMaterialPreviewDiagnosticSeverity.Warning,
                    "The selected image comes from the material row without " +
                    "proof that it is the active canonical asset provider.",
                    selected.TextureTableOrdinal));
            }
            if (image.Width == 0 || image.Height == 0)
            {
                diagnostics.Add(new UiMaterialPreviewDiagnostic(
                    UiMaterialPreviewDiagnosticCode.InvalidImageDimensions,
                    UiMaterialPreviewDiagnosticSeverity.Blocker,
                    $"The selected image has invalid dimensions {image.Width}x{image.Height}.",
                    selected.TextureTableOrdinal));
            }
            if (image.Depth > 1)
            {
                diagnostics.Add(new UiMaterialPreviewDiagnostic(
                    UiMaterialPreviewDiagnosticCode.UnsupportedImageDepth,
                    UiMaterialPreviewDiagnosticSeverity.Blocker,
                    $"The selected image is a depth-{image.Depth} texture; " +
                    "UI texture approximation supports only a single 2D " +
                    "slice.",
                    selected.TextureTableOrdinal));
            }
            if (!IsTwoDimensional(image))
            {
                diagnostics.Add(new UiMaterialPreviewDiagnostic(
                    UiMaterialPreviewDiagnosticCode.NonTwoDimensionalImage,
                    UiMaterialPreviewDiagnosticSeverity.Warning,
                    "The selected image descriptor is not a standard PS3 2D " +
                    $"texture (map type 0x{image.MapType:X2}, dimensions " +
                    $"{image.DimensionCount}, multi-face " +
                    $"0x{image.MultiFaceControl:X2}).",
                    selected.TextureTableOrdinal));
            }

            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.MaterialTechniqueNotEvaluated,
                UiMaterialPreviewDiagnosticSeverity.Information,
                "The preview shows a texture approximation; the material " +
                "technique, shaders, constants, and render state are not " +
                "evaluated.",
                selected.TextureTableOrdinal));
        }

        bool blocked = diagnostics.Any(diagnostic =>
            diagnostic.Severity ==
            UiMaterialPreviewDiagnosticSeverity.Blocker);
        return new UiMaterialPreviewPlan(
            SnapshotMaterial(material),
            atlas,
            textureTable,
            selected,
            selectedAuthority,
            selectedImageMetadata,
            blocked
                ? UiMaterialPreviewFidelity.Unavailable
                : UiMaterialPreviewFidelity.TextureApproximation,
            diagnostics);
    }

    private static UiMaterialPreviewImageResolution ResolveMaterialRowFallback(
        MaterialTextureDef row) =>
        row.Image is null
            ? UiMaterialPreviewImageResolution.Unavailable(
                "The material texture row has no resolved image.")
            : UiMaterialPreviewImageResolution.MaterialRowFallback(row.Image);

    private static MapRenderEditorMaterialTextureBinding? SelectTexture(
        MapRenderEditorMaterialTexturePlan textureTable,
        ICollection<UiMaterialPreviewDiagnostic> diagnostics)
    {
        MapRenderEditorMaterialTextureBinding[] baseColorBindings =
            textureTable.Bindings
                .Where(binding =>
                    binding.Role ==
                    MapRenderEditorMaterialTextureRole.BaseColor)
                .ToArray();
        MapRenderEditorMaterialTextureBinding[] resolvedBaseColorBindings =
            baseColorBindings
                .Where(binding => binding.Image is not null)
                .ToArray();

        if (baseColorBindings.Length > 1)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.BaseColorBindingAmbiguous,
                UiMaterialPreviewDiagnosticSeverity.Warning,
                $"The material has {baseColorBindings.Length} base-color " +
                "texture rows; the first resolved row in deterministic " +
                "table order will be shown."));
        }
        if (resolvedBaseColorBindings.Length > 0)
            return resolvedBaseColorBindings[0];

        if (baseColorBindings.Length > 0)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.BaseColorImageUnavailable,
                UiMaterialPreviewDiagnosticSeverity.Warning,
                "The material's base-color texture rows do not resolve to an image."));
        }

        MapRenderEditorMaterialTextureBinding? fallback =
            textureTable.Bindings.FirstOrDefault(binding =>
                binding.Image is not null);
        if (fallback is not null)
        {
            diagnostics.Add(new UiMaterialPreviewDiagnostic(
                UiMaterialPreviewDiagnosticCode.FallbackTextureSelected,
                UiMaterialPreviewDiagnosticSeverity.Warning,
                $"Texture role '{fallback.Role}' is shown because no resolved base-color row is available.",
                fallback.TextureTableOrdinal));
        }

        return fallback;
    }

    private static UiMaterialPreviewMaterialMetadata SnapshotMaterial(
        MaterialAsset material) =>
        new(
            material.Info.Name ?? "<unnamed material>",
            material.Info.GameFlags,
            material.Info.SortKey,
            material.StateFlags,
            material.CameraRegion,
            material.TechniqueSet?.Name);

    private static UiMaterialPreviewImageMetadata SnapshotImage(
        GfxImageAsset image) =>
        new(
            image.Name ?? "<unnamed image>",
            image.Width,
            image.Height,
            image.Depth,
            image.Format,
            image.LevelCount,
            image.MapType,
            image.DimensionCount,
            image.MultiFaceControl);

    private static bool IsTwoDimensional(GfxImageAsset image) =>
        image.MapType == 3 &&
        image.DimensionCount == 2 &&
        image.MultiFaceControl == 0 &&
        image.Depth == 1;
}
