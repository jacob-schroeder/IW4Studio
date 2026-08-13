using IW4.Render.Techniques;
using IW4.Render.Materials;

namespace IW4.Render.Execution;

/// <summary>
/// Immutable identity and fixed state for one authored material pass selected
/// for translated shader execution. Resource payloads remain outside this
/// selection so world and UI scene builders can share the same contract
/// construction pipeline.
/// </summary>
internal sealed record ShaderExecutionPassSelection(
    MaterialPassIdentity Pass,
    MaterialSamplerIdentity? PrimarySampler,
    RenderState State,
    string FallbackTextureName);
