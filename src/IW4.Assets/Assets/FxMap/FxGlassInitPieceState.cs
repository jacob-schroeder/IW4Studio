namespace IW4.Assets.Assets.FxMap;

public sealed class FxGlassInitPieceState
{
    public const int SerializedSize = 0x34;

    public FxSpatialFrame Frame { get; init; }
    public float Radius { get; init; }
    public FxVec2 TexCoordOrigin { get; init; }
    public uint SupportMask { get; init; }
    public float AreaX2 { get; init; }
    public byte DefIndex { get; init; }
    public byte VertCount { get; init; }
    public byte FanDataCount { get; init; }
    public byte Pad33 { get; init; }
}
