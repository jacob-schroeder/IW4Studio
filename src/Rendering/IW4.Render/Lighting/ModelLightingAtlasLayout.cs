using System.Numerics;

namespace IW4.Render.Lighting;

/// <summary>
/// Physical PS3 model-lighting atlas layout facts shared by map and XModel
/// consumers.
/// </summary>
public static class ModelLightingAtlasLayout
{
    public const int Width = 512;
    public const int Height = 256;
    public const int Depth = 4;
    public const int TileWidth = 4;
    public const int TileHeight = 4;
    public const int TileDepth = 4;
    public const int EntriesPerRow = Width / TileWidth;
    public const int RowsPerSlice = Height / TileHeight;
    public const int EntryCapacity = EntriesPerRow * RowsPerSlice;
    public const int DynamicEntryCapacity = 1024;
    public const int StaticEntryCapacity = EntryCapacity - DynamicEntryCapacity;
    public const int TilePixelCount = TileWidth * TileHeight * TileDepth;
    public const int TileByteCount = TilePixelCount * 4;
    public static Vector4 SamplerTransform { get; } = new(
        1.5f / Width, 1.5f / Height, 1.5f / Depth, 0f);

    public static Vector4 EntryCoordinates(int entryIndex)
    {
        if ((uint)entryIndex >= EntryCapacity)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        return new(
            (TileWidth * (entryIndex & (EntriesPerRow - 1)) + 2f) / Width,
            (TileHeight * (entryIndex / EntriesPerRow) + 2f) / Height,
            0.5f,
            1f);
    }
}
