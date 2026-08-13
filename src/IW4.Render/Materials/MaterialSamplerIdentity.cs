namespace IW4.Render.Materials;

public readonly record struct MaterialSamplerIdentity(
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    byte TextureSemantic);
