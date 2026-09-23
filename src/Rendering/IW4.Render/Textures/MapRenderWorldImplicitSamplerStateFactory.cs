namespace IW4.Render.Textures;

/// <summary>
/// Produces the exact operational sampler state emitted by Event20 for its
/// fixed reflection and lightmap destinations.
/// </summary>
public static class MapRenderWorldImplicitSamplerStateFactory
{
    public static RsxSamplerState Create(
        MapRenderWorldRuntimeTextureKind kind)
    {
        byte rawState = kind switch
        {
            MapRenderWorldRuntimeTextureKind.ReflectionProbe =>
                RsxImplicitSamplerStateEncoding.ReflectionProbe,
            MapRenderWorldRuntimeTextureKind.SecondaryLightmap or
            MapRenderWorldRuntimeTextureKind.PrimaryLightmap =>
                RsxImplicitSamplerStateEncoding.Lightmap,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        RsxSamplerState decoded = RsxSamplerDecoder.Decode(rawState);
        IReadOnlyList<Commands.MapRenderRsxMethodPacket> packets =
            RsxImplicitSamplerCommandBuilder.BuildInitialization(kind);
        uint[] payloads = packets.SelectMany(packet => packet.Payloads).ToArray();
        if (payloads.Length != 3 ||
            payloads[0] != decoded.RsxTexEnablePayload ||
            payloads[1] != decoded.RsxTexFilterPayload ||
            payloads[2] != decoded.RsxTexWrapPayload)
        {
            throw new InvalidOperationException(
                "The implicit sampler operational decode does not reproduce the Event20 packet.");
        }

        return decoded;
    }
}
