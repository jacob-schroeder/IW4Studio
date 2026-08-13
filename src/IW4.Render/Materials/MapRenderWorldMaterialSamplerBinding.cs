using IW4.Render.Textures;

namespace IW4.Render.Materials;

/// <summary>
/// Map-scene ownership of a generic material sampler and, where applicable,
/// the world-runtime texture slot that supplied it.
/// </summary>
public sealed record MapRenderWorldMaterialSamplerBinding(
    MaterialSamplerBinding Binding,
    MapRenderWorldRuntimeTextureIdentity? RuntimeTextureIdentity = null);
