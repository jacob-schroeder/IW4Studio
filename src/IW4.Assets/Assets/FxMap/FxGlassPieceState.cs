namespace IW4.Assets.Assets.FxMap;

public sealed class FxGlassPieceState
{
    public const int SerializedSize = 0x20;

    public FxVec2 TexCoordOrigin { get; init; }
    public uint SupportMask { get; init; }
    public ushort InitIndex { get; init; }
    public ushort GeoDataStart { get; init; }
    public byte DefIndex { get; init; }
    public IReadOnlyList<byte> Pad11 { get; init; } = [];
    public byte VertCount { get; init; }
    public byte HoleDataCount { get; init; }
    public byte CrackDataCount { get; init; }
    public byte FanDataCount { get; init; }
    public ushort Flags { get; init; }
    public float AreaX2 { get; init; }
}
