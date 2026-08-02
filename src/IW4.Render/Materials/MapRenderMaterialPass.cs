using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Materials;

public sealed record MapRenderMaterialPass(
    string MaterialName,
    string TechniqueSetName,
    int TechniqueSlot,
    string TechniqueName,
    string PassClass,
    int PassIndex,
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    byte TextureSemantic,
    byte TexCoordSource,
    byte CustomSamplerFlags)
{
    public string TexCoordSourceName => MapRenderUvRoute.StreamSourceName(TexCoordSource);
}
