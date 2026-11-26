using MapConverter.IW3.PC.Assets.World;
using System.Buffers.Binary;

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
        int skyCount = zone.Int32(root + 32);
        int surfaceCount = zone.Int32(root + 584);
        if (skyCount < 0 || skyCount > surfaceCount)
            throw new InvalidDataException("IW3 PC GfxWorld has an invalid sky surface count.");
        int skyTable = zone.Pointer(root + 36);
        if (skyCount != 0 && skyTable == 0)
            throw new InvalidDataException("IW3 PC GfxWorld has no sky surface table.");
        var skyStartSurfs = new int[skyCount];
        for (int index = 0; index < skyCount; index++)
        {
            // The compiled PC zone has already materialized sorted positions:
            // each entry selects its sky through sortedSurfIndex, not directly
            // through the physical surface table.
            int sortedPosition = BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(skyTable + index * 4, 4));
            if (sortedPosition < 0 || sortedPosition >= surfaceCount)
                throw new InvalidDataException($"IW3 PC sky surface {index} has an invalid sorted position.");
            skyStartSurfs[index] = sortedPosition;
        }
        int skyImage = zone.Pointer(root + 40);
        if (skyCount != 0 && skyImage == 0)
            throw new InvalidDataException("IW3 PC GfxWorld sky surfaces have no image.");
        string? skyImageName = skyImage == 0 ? null : zone.String(skyImage + 32);
        if (skyImage != 0 && string.IsNullOrWhiteSpace(skyImageName))
            throw new InvalidDataException("IW3 PC GfxWorld sky image has no name.");
        int sun = root + 380;
        byte[] sunBytes = zone.Bytes(sun, 96).ToArray();
        foreach (int offset in new[] { 12, 16, 20, 24, 28, 32, 44, 48, 52, 64, 68, 72, 84, 88, 92 })
            zone.Floats(sun + offset, 1);
        int sprite = zone.Pointer(sun + 4);
        int flare = zone.Pointer(sun + 8);
        string? spriteName = sprite == 0 ? null : zone.String(sprite);
        string? flareName = flare == 0 ? null : zone.String(flare);
        return new(atlasCount, outdoorName, zone.Floats(root + 476, 16),
            skyStartSurfs, skyImageName, zone.Bytes(root + 44, 1)[0],
            sunBytes, spriteName, flareName);
    }
}
