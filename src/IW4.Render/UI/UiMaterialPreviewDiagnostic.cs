namespace IW4.Render.UI;

public enum UiMaterialPreviewDiagnosticCode
{
    MaterialHasNoTextures = 0,
    TextureImageResolutionFailed = 1,
    NoResolvedTextureImage = 2,
    BaseColorBindingAmbiguous = 3,
    BaseColorImageUnavailable = 4,
    FallbackTextureSelected = 5,
    NonCanonicalImageFallback = 6,
    InvalidImageDimensions = 7,
    UnsupportedImageDepth = 8,
    NonTwoDimensionalImage = 9,
    InvalidTextureAtlas = 10,
    MaterialTechniqueNotEvaluated = 11,
    TextureAtlasFrameNotEvaluated = 12
}

public sealed record UiMaterialPreviewDiagnostic(
    UiMaterialPreviewDiagnosticCode Code,
    UiDiagnosticSeverity Severity,
    string Message,
    int? TextureTableOrdinal = null);
