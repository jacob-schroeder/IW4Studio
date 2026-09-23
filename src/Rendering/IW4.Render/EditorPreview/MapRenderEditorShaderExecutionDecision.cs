using IW4.Render.Techniques;
using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Immutable shader-path decision. OriginalState always preserves decoded asset
/// state; EffectiveState contains the explicitly bounded editor approximation,
/// if one was required.
/// </summary>
public sealed class MapRenderEditorShaderExecutionDecision
{
    internal MapRenderEditorShaderExecutionDecision(
        MapRenderEditorShaderExecutionChoice choice,
        MapRenderEditorShaderExecutionReason reason,
        RenderState originalState,
        RenderState effectiveState)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        Choice = choice;
        Reason = reason;
        OriginalState = originalState;
        EffectiveState = effectiveState;
        Status = StatusFor(reason);
    }

    public MapRenderEditorShaderExecutionChoice Choice { get; }

    public MapRenderEditorShaderExecutionReason Reason { get; }

    public string Status { get; }

    public RenderState OriginalState { get; }

    public RenderState EffectiveState { get; }

    public bool IsExecutable => Choice != MapRenderEditorShaderExecutionChoice.Skip;

    public bool UsesGenericMaterial =>
        Choice == MapRenderEditorShaderExecutionChoice.GenericEditorMaterial;

    public bool UsesStateApproximation => OriginalState != EffectiveState;

    private static string StatusFor(MapRenderEditorShaderExecutionReason reason) =>
        reason switch
        {
            MapRenderEditorShaderExecutionReason.TranslatedAuthoredReady =>
                "TRANSLATED_AUTHORED_READY",
            MapRenderEditorShaderExecutionReason.EditorAlphaTestRequiresGenericMaterial =>
                "EDITOR_GENERIC_ALPHA_TEST_REQUIRED",
            MapRenderEditorShaderExecutionReason.EditorStencilDisabledGenericApproximation =>
                "EDITOR_GENERIC_STENCIL_DISABLED_APPROXIMATION",
            MapRenderEditorShaderExecutionReason.EditorAlphaTestAndStencilDisabledGenericApproximation =>
                "EDITOR_GENERIC_ALPHA_TEST_AND_STENCIL_DISABLED_APPROXIMATION",
            MapRenderEditorShaderExecutionReason.EditorAuthoredProgramUnavailableGenericFallback =>
                "EDITOR_GENERIC_AUTHORED_PROGRAM_UNAVAILABLE_FALLBACK",
            MapRenderEditorShaderExecutionReason.EditorAuthoredProgramNotReadyGenericFallback =>
                "EDITOR_GENERIC_AUTHORED_PROGRAM_NOT_READY_FALLBACK",
            MapRenderEditorShaderExecutionReason.EditorAlphaTestGenericMaterialUnavailable =>
                "EDITOR_SKIP_ALPHA_TEST_GENERIC_MATERIAL_UNAVAILABLE",
            MapRenderEditorShaderExecutionReason.EditorStencilGenericMaterialUnavailable =>
                "EDITOR_SKIP_STENCIL_GENERIC_MATERIAL_UNAVAILABLE",
            MapRenderEditorShaderExecutionReason.EditorAlphaTestAndStencilGenericMaterialUnavailable =>
                "EDITOR_SKIP_ALPHA_TEST_AND_STENCIL_GENERIC_MATERIAL_UNAVAILABLE",
            MapRenderEditorShaderExecutionReason.EditorAuthoredProgramUnavailableAndGenericMaterialUnavailable =>
                "EDITOR_SKIP_AUTHORED_PROGRAM_AND_GENERIC_MATERIAL_UNAVAILABLE",
            MapRenderEditorShaderExecutionReason.EditorAuthoredProgramNotReadyAndGenericMaterialUnavailable =>
                "EDITOR_SKIP_AUTHORED_PROGRAM_NOT_READY_AND_GENERIC_MATERIAL_UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
