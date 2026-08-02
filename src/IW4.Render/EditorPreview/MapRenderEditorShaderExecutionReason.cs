namespace IW4.Render.EditorPreview;

public enum MapRenderEditorShaderExecutionReason
{
    TranslatedAuthoredReady = 0,
    EditorAlphaTestRequiresGenericMaterial = 1,
    EditorStencilDisabledGenericApproximation = 2,
    EditorAlphaTestAndStencilDisabledGenericApproximation = 3,
    EditorAuthoredProgramUnavailableGenericFallback = 4,
    EditorAuthoredProgramNotReadyGenericFallback = 5,
    EditorAlphaTestGenericMaterialUnavailable = 6,
    EditorStencilGenericMaterialUnavailable = 7,
    EditorAlphaTestAndStencilGenericMaterialUnavailable = 8,
    EditorAuthoredProgramUnavailableAndGenericMaterialUnavailable = 9,
    EditorAuthoredProgramNotReadyAndGenericMaterialUnavailable = 10
}
