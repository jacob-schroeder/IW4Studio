
using IW4.Render.Textures;

namespace IW4.Render.Materials;

public sealed record MaterialColorLayer(
    int LayerIndex,
    MaterialSamplerIdentity Identity,
    Texture Texture,
    UvRoute UvRoute,
    int BlendWeightComponent);
