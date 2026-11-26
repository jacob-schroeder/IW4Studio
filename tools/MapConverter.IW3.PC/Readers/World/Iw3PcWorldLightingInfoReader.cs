using MapConverter.IW3.PC.Assets.World;

namespace MapConverter.IW3.PC.Readers.World;

internal static class Iw3PcWorldLightingInfoReader
{
    internal static Iw3PcWorldLightingInfo Read(Iw3PcZone zone, int root)
    {
        int atlasCount = zone.Int32(root + 264);
        if (atlasCount is < 0 or > 31)
            throw new InvalidDataException("IW3 PC GfxWorld has an invalid lightmap atlas count.");
        int outdoor = zone.Pointer(root + 540);
        if (outdoor == 0)
            throw new InvalidDataException("IW3 PC GfxWorld has no outdoor image.");
        string outdoorName = zone.String(outdoor + 32);
        if (outdoorName.Length == 0)
            throw new InvalidDataException("IW3 PC GfxWorld outdoor image has no name.");
        return new(atlasCount, outdoorName, zone.Floats(root + 476, 16));
    }
}
