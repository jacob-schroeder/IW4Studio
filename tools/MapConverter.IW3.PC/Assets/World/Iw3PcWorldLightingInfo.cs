namespace MapConverter.IW3.PC.Assets.World;

internal sealed record Iw3PcWorldLightingInfo(
    int LightmapAtlasCount, string OutdoorImageName, float[] OutdoorLookupMatrix,
    int[] SkyStartSurfs, string? SkyImageName, byte SkySamplerState,
    byte[] Sun, string? SunSpriteMaterialName, string? SunFlareMaterialName);
