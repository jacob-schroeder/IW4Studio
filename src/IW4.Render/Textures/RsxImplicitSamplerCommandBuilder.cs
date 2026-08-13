using IW4.Render.Commands;

namespace IW4.Render.Textures;

/// <summary>
/// Exact primary-context RSX packet writer used by PS3 Event20
/// (default_mp.elf 0x0039E080) for its three implicit world samplers.
/// </summary>
public static class RsxImplicitSamplerCommandBuilder
{
    public static IReadOnlyList<MapRenderRsxMethodPacket> BuildInitialization(
        MapRenderWorldRuntimeTextureKind kind)
    {
        ushort sampler = SamplerDestination(kind);
        uint filterPayload = kind == MapRenderWorldRuntimeTextureKind.ReflectionProbe
            ? 0x02063fa0u
            : 0x02023fa0u;
        return Array.AsReadOnly(
        [
            new MapRenderRsxMethodPacket(
                RsxSamplerDecoder.RsxTexEnableMethod(sampler),
                [0x80060000u]),
            new MapRenderRsxMethodPacket(
                RsxSamplerDecoder.RsxTexFilterMethod(sampler),
                [filterPayload]),
            new MapRenderRsxMethodPacket(
                RsxSamplerDecoder.RsxTexWrapMethod(sampler),
                [0x40010303u])
        ]);
    }

    public static ushort SamplerDestination(
        MapRenderWorldRuntimeTextureKind kind) => kind switch
        {
            MapRenderWorldRuntimeTextureKind.ReflectionProbe => 1,
            MapRenderWorldRuntimeTextureKind.SecondaryLightmap => 3,
            MapRenderWorldRuntimeTextureKind.PrimaryLightmap => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
