using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

public readonly record struct MapRenderEditorShaderExecutionInput(
    MapRenderState DecodedState,
    bool AuthoredProgramAvailable,
    bool AuthoredProgramReady,
    bool GenericMaterialReady);
