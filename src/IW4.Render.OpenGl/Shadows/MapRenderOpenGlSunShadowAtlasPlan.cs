namespace IW4.Render.OpenGl.Shadows;

/// <summary>Physical tile identity inside the PS3 sun-shadow atlas.</summary>
public enum MapRenderOpenGlSunShadowAtlasPartition
{
    Partition0 = 0,
    Partition1 = 1
}

/// <summary>
/// Host depth storage selected for the PS3 render-target descriptor. The raw
/// The PS3 format word maps to the DepthComponent24 OpenGL adapter.
/// </summary>
public enum MapRenderOpenGlSunShadowAtlasDepthStorage
{
    DepthComponent24 = 0
}

/// <summary>Host filtering for raw code sampler 6.</summary>
public enum MapRenderOpenGlSunShadowAtlasFilter
{
    LinearWithoutMipmaps = 0
}

/// <summary>Host addressing for raw code sampler 6.</summary>
public enum MapRenderOpenGlSunShadowAtlasWrap
{
    ClampToEdge = 0
}

/// <summary>
/// Host comparison for the PS3 RSX projected comparison convention, mapped to
/// OpenGL LESS.
/// </summary>
public enum MapRenderOpenGlSunShadowAtlasCompare
{
    CompareReferenceToTextureLess = 0
}

/// <summary>One vertical atlas tile in host framebuffer coordinates.</summary>
public sealed record MapRenderOpenGlSunShadowAtlasTile
{
    internal MapRenderOpenGlSunShadowAtlasTile(
        MapRenderOpenGlSunShadowAtlasPartition partition,
        int x,
        int y,
        int width,
        int height)
    {
        if (!Enum.IsDefined(partition))
            throw new ArgumentOutOfRangeException(nameof(partition));
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Partition = partition;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public MapRenderOpenGlSunShadowAtlasPartition Partition { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}

/// <summary>
/// Pure PS3 sun-shadow atlas allocation and sampling plan. The 1024x2048
/// descriptor uses two vertical 1024 tiles. Host storage and sampler enum
/// mappings are OpenGL adapters.
/// </summary>
public sealed class MapRenderOpenGlSunShadowAtlasPlan
{
    private readonly MapRenderOpenGlSunShadowAtlasTile[] _tiles;

    internal MapRenderOpenGlSunShadowAtlasPlan(
        int width,
        int height,
        IReadOnlyList<MapRenderOpenGlSunShadowAtlasTile> tiles,
        MapRenderOpenGlSunShadowAtlasDepthStorage hostDepthStorage,
        MapRenderOpenGlSunShadowAtlasFilter hostFilter,
        MapRenderOpenGlSunShadowAtlasWrap hostWrap,
        MapRenderOpenGlSunShadowAtlasCompare hostCompare)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(tiles);
        if (!Enum.IsDefined(hostDepthStorage))
            throw new ArgumentOutOfRangeException(nameof(hostDepthStorage));
        if (!Enum.IsDefined(hostFilter))
            throw new ArgumentOutOfRangeException(nameof(hostFilter));
        if (!Enum.IsDefined(hostWrap))
            throw new ArgumentOutOfRangeException(nameof(hostWrap));
        if (!Enum.IsDefined(hostCompare))
            throw new ArgumentOutOfRangeException(nameof(hostCompare));

        _tiles = tiles.ToArray();
        MapRenderOpenGlSunShadowAtlasPartition[] expected =
        [
            MapRenderOpenGlSunShadowAtlasPartition.Partition0,
            MapRenderOpenGlSunShadowAtlasPartition.Partition1
        ];
        if (!_tiles.Select(tile => tile.Partition).SequenceEqual(expected))
        {
            throw new ArgumentException(
                "The sun-shadow atlas must contain partitions 0 and 1 in exact order.",
                nameof(tiles));
        }
        if (_tiles.Any(tile =>
                tile.X + tile.Width > width ||
                tile.Y + tile.Height > height))
        {
            throw new ArgumentException(
                "Every sun-shadow tile must fit inside the atlas.",
                nameof(tiles));
        }

        Width = width;
        Height = height;
        HostDepthStorage = hostDepthStorage;
        HostFilter = hostFilter;
        HostWrap = hostWrap;
        HostCompare = hostCompare;
        Tiles = Array.AsReadOnly(_tiles);
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<MapRenderOpenGlSunShadowAtlasTile> Tiles { get; }

    public int PartitionCount => _tiles.Length;

    public MapRenderOpenGlSunShadowAtlasDepthStorage HostDepthStorage { get; }

    public MapRenderOpenGlSunShadowAtlasFilter HostFilter { get; }

    public MapRenderOpenGlSunShadowAtlasWrap HostWrap { get; }

    public MapRenderOpenGlSunShadowAtlasCompare HostCompare { get; }

    public MapRenderOpenGlSunShadowAtlasTile GetTile(
        MapRenderOpenGlSunShadowAtlasPartition partition)
    {
        if (!Enum.IsDefined(partition))
            throw new ArgumentOutOfRangeException(nameof(partition));
        return _tiles[(int)partition];
    }
}

/// <summary>Constructs the normal two-partition PS3 sun-shadow plan.</summary>
public static class MapRenderOpenGlSunShadowAtlasPlanner
{
    public const int Ps3Width = 1024;
    public const int Ps3Height = 2048;
    public const int Ps3PartitionSize = 1024;

    public static MapRenderOpenGlSunShadowAtlasPlan CreatePs3Normal() =>
        new(
            Ps3Width,
            Ps3Height,
            [
                new MapRenderOpenGlSunShadowAtlasTile(
                    MapRenderOpenGlSunShadowAtlasPartition.Partition0,
                    0,
                    0,
                    Ps3PartitionSize,
                    Ps3PartitionSize),
                new MapRenderOpenGlSunShadowAtlasTile(
                    MapRenderOpenGlSunShadowAtlasPartition.Partition1,
                    0,
                    Ps3PartitionSize,
                    Ps3PartitionSize,
                    Ps3PartitionSize)
            ],
            MapRenderOpenGlSunShadowAtlasDepthStorage.DepthComponent24,
            MapRenderOpenGlSunShadowAtlasFilter.LinearWithoutMipmaps,
            MapRenderOpenGlSunShadowAtlasWrap.ClampToEdge,
            MapRenderOpenGlSunShadowAtlasCompare
                .CompareReferenceToTextureLess);
}
