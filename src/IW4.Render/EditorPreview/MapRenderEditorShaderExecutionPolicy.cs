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
        bool alphaTest = input.DecodedState.AlphaTestEnabled;
        bool stencil = input.DecodedState.Stencil.Enabled;
        if (stencil)
        {
            if (!input.GenericMaterialReady)
            {
                MapRenderEditorShaderExecutionReason unavailableReason =
                    (alphaTest, stencil) switch
                    {
                        (true, true) => MapRenderEditorShaderExecutionReason
                            .EditorAlphaTestAndStencilGenericMaterialUnavailable,
                        (true, false) => MapRenderEditorShaderExecutionReason
                            .EditorAlphaTestGenericMaterialUnavailable,
                        (false, true) => MapRenderEditorShaderExecutionReason
                            .EditorStencilGenericMaterialUnavailable,
                        _ => throw new InvalidOperationException()
                    };
                return Decision(
                    MapRenderEditorShaderExecutionChoice.Skip,
                    unavailableReason,
                    input.DecodedState,
                    input.DecodedState);
            }

            MapRenderState effectiveState =
                input.DecodedState with
                {
                    Stencil = MapRenderStencilState.Disabled
                };
            MapRenderEditorShaderExecutionReason genericReason =
                (alphaTest, stencil) switch
                {
                    (true, true) => MapRenderEditorShaderExecutionReason
                        .EditorAlphaTestAndStencilDisabledGenericApproximation,
                    (true, false) => MapRenderEditorShaderExecutionReason
                        .EditorAlphaTestRequiresGenericMaterial,
                    (false, true) => MapRenderEditorShaderExecutionReason
                        .EditorStencilDisabledGenericApproximation,
                    _ => throw new InvalidOperationException()
                };
            return Decision(
                MapRenderEditorShaderExecutionChoice.GenericEditorMaterial,
                genericReason,
                input.DecodedState,
                effectiveState);
        }

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
        MapRenderState originalState,
        MapRenderState effectiveState) =>
        new(choice, reason, originalState, effectiveState);
}
