using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxLightGrid
{
    public const int SerializedSize = 0x38;

    public uint HasLightRegionsRaw { get; init; }
    public bool HasLightRegions => HasLightRegionsRaw != 0;
    public uint SunPrimaryLightIndex { get; init; }
    public IReadOnlyList<ushort> Mins { get; init; } = [];
    public IReadOnlyList<ushort> Maxs { get; init; } = [];
    public GfxLightGridHorizontalAxis RowAxis { get; init; }
    public GfxLightGridHorizontalAxis ColAxis { get; init; }
    public XPointer<ushort[]> RowDataStartPointer { get; init; }
    public IReadOnlyList<ushort> RowDataStart { get; init; } = [];
    public uint RawRowDataSize { get; init; }
    public XPointer<byte[]> RawRowDataPointer { get; init; }
    public IReadOnlyList<byte> RawRowData { get; init; } = [];
    public uint EntryCount { get; init; }
    public XPointer<GfxLightGridEntry[]> EntriesPointer { get; init; }
    public IReadOnlyList<GfxLightGridEntry> Entries { get; init; } = [];
    public uint ColorCount { get; init; }
    public XPointer<GfxLightGridColors[]> ColorsPointer { get; init; }
    public IReadOnlyList<GfxLightGridColors> Colors { get; init; } = [];
}
