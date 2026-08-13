using IW4.Assets.Assets.Image;

namespace IW4.Render.Materials;

public readonly record struct MaterialSamplerIdentity(
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    TextureSemantic TextureSemantic);
