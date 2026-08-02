using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxLightGrid
{
    public const int SerializedSize = 0x38;

    public uint HasLightRegions { get; init; }
    public uint SunPrimaryLightIndex { get; init; }
    public IReadOnlyList<ushort> Mins { get; init; } = [];
    public IReadOnlyList<ushort> Maxs { get; init; } = [];
    public uint RowAxis { get; init; }
    public uint ColAxis { get; init; }
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
