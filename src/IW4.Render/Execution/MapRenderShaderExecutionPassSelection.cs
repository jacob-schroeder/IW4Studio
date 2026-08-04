using IW4.Render.Materials;

namespace IW4.Render.Execution;

/// <summary>
/// Immutable identity and fixed state for one authored material pass selected
/// for translated shader execution. Resource payloads remain outside this
/// selection so world and UI scene builders can share the same contract
/// construction pipeline.
/// </summary>
internal sealed record MapRenderShaderExecutionPassSelection(
    MapRenderMaterialPass Pass,
    MapRenderState State,
    string FallbackTextureName);
