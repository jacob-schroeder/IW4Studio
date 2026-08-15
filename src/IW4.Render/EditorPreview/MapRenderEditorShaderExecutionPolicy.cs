using IW4.Render.Techniques;
using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Pure EditorPreview shader-path policy. It performs no OpenGL calls and
/// consumes no scheduler or capture state.
/// </summary>
public static class MapRenderEditorShaderExecutionPolicy
{
    public static MapRenderEditorShaderExecutionDecision Decide(
        MapRenderEditorShaderExecutionInput input)
    {
        if (input.AuthoredProgramReady && !input.AuthoredProgramAvailable)
        {
            throw new ArgumentException(
                "An authored program cannot be ready when it is unavailable.",
                nameof(input));
        }

        return DecideEditor(input);
    }

    private static MapRenderEditorShaderExecutionDecision DecideEditor(
        MapRenderEditorShaderExecutionInput input)
    {
        if (input.AuthoredProgramAvailable && input.AuthoredProgramReady)
        {
            return Decision(
                MapRenderEditorShaderExecutionChoice.TranslatedAuthored,
                MapRenderEditorShaderExecutionReason.TranslatedAuthoredReady,
                input.DecodedState,
                input.DecodedState);
        }

        if (input.GenericMaterialReady)
        {
            MapRenderEditorShaderExecutionReason fallbackReason =
                input.AuthoredProgramAvailable
                    ? MapRenderEditorShaderExecutionReason
                        .EditorAuthoredProgramNotReadyGenericFallback
                    : MapRenderEditorShaderExecutionReason
                        .EditorAuthoredProgramUnavailableGenericFallback;
            return Decision(
                MapRenderEditorShaderExecutionChoice.GenericEditorMaterial,
                fallbackReason,
                input.DecodedState,
                input.DecodedState);
        }

        MapRenderEditorShaderExecutionReason skipReason = input.AuthoredProgramAvailable
            ? MapRenderEditorShaderExecutionReason
                .EditorAuthoredProgramNotReadyAndGenericMaterialUnavailable
            : MapRenderEditorShaderExecutionReason
                .EditorAuthoredProgramUnavailableAndGenericMaterialUnavailable;
        return Decision(
            MapRenderEditorShaderExecutionChoice.Skip,
            skipReason,
            input.DecodedState,
            input.DecodedState);
    }

    private static MapRenderEditorShaderExecutionDecision Decision(
        MapRenderEditorShaderExecutionChoice choice,
        MapRenderEditorShaderExecutionReason reason,
        RenderState originalState,
        RenderState effectiveState) =>
        new(choice, reason, originalState, effectiveState);
}
