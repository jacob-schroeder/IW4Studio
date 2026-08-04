namespace IW4.Render.UI;

public enum UiMaterialExecutionDiagnosticSeverity
{
    Information = 0,
    Warning = 1,
    Blocker = 2
}

public enum UiMaterialExecutionDiagnosticCode
{
    CanonicalMaterialUnavailable = 0,
    CanonicalRevisionChanged = 1,
    UnsupportedTechniqueSet = 2,
    TechniqueSlotUnavailable = 3,
    UnsupportedTechnique = 4,
    UnsupportedPassCount = 5,
    ShaderArgumentsIncomplete = 6,
    UnsupportedShaderProgram = 7,
    UnsupportedVertexDeclaration = 8,
    UnsupportedShaderArguments = 9,
    MaterialStateUnavailable = 10,
    UnsupportedMaterialState = 11,
    TextureRowUnavailable = 12,
    TextureResourceUnavailable = 13,
    InvalidTextureAtlas = 14,
    TextureAtlasEvaluationRequired = 15,
    ShaderExecutionBlocked = 16,
    UnsupportedTextureTarget = 17,
    UnsupportedCpuPreviewCompositeState = 18
}

public sealed record UiMaterialExecutionDiagnostic(
    UiMaterialExecutionDiagnosticCode Code,
    UiMaterialExecutionDiagnosticSeverity Severity,
    string Message);
