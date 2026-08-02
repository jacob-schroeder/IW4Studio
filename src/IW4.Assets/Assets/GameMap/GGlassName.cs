using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GameMap;

public sealed class GGlassName
{
    public const int SerializedSize = 0x0C;

    public int Offset { get; init; }

    // 0x00: XString nameStr. PS3 stores row+0x00 into varXString and calls Load_XString.
    public XPointer<string> NameStrPointer { get; init; }
    public string? NameStr { get; init; }

    // 0x04: G_GlassName.name script-string index.
    public ushort Name { get; init; }

    // 0x06: G_GlassName.pieceCount. PS3 uses this as the pieceIndices ushort count.
    public ushort PieceCount { get; init; }

    // 0x08: ushort*. PS3 aligns to 2 and loads pieceCount indices when non-null.
    public XPointer<ushort[]> PieceIndicesPointer { get; init; }
    public IReadOnlyList<ushort> PieceIndices { get; init; } = [];
}
