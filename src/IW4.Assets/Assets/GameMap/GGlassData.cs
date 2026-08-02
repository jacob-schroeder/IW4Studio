using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GameMap;

public sealed class GGlassData
{
    public const int SerializedSize = 0x80;

    public int Offset { get; init; }

    // 0x00: G_GlassPiece*. PS3 loads pieceCount fixed 0x0C-byte rows when non-null.
    public XPointer<GGlassPiece[]> GlassPiecesPointer { get; init; }
    public IReadOnlyList<GGlassPiece> GlassPieces { get; init; } = [];

    // 0x04: G_GlassData.pieceCount. PS3 uses this as the G_GlassPiece array count.
    public int PieceCount { get; init; }

    // 0x08..0x0B: G_GlassData damage thresholds.
    public ushort DamageToWeaken { get; init; }
    public ushort DamageToDestroy { get; init; }

    // 0x0C: G_GlassData.glassNameCount. PS3 uses this as the G_GlassName array count.
    public int GlassNameCount { get; init; }

    // 0x10: G_GlassName*. PS3 loads glassNameCount fixed 0x0C-byte rows when non-null.
    public XPointer<GGlassName[]> GlassNamesPointer { get; init; }
    public IReadOnlyList<GGlassName> GlassNames { get; init; } = [];

    // 0x14..0x7F: preserved G_GlassData padding.
    public IReadOnlyList<byte> Pad14To7F { get; init; } = [];
}
