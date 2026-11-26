namespace MapConverter.IW3.PC.Assets.World;

internal sealed record Iw3PcWorldLightingInfo(
    int LightmapAtlasCount, string OutdoorImageName, float[] OutdoorLookupMatrix);
