using IW4.Game.Math;
namespace IW4.Game.Assets.FxMap;

public sealed class FxGlassInitPieceState
{
    public const int SerializedSize = 0x34;

    public FxSpatialFrame Frame { get; init; }
    public float Radius { get; init; }
    public Vec2 TexCoordOrigin { get; init; }
    /// <summary>Bit 31 corresponds to the edge from vertex 0 to vertex 1.
    /// Successive edges use descending bits. Set means supported and clear means exposed.</summary>
    public uint SupportMask { get; init; }
    public float AreaX2 { get; init; }
    public byte DefIndex { get; init; }
    public byte VertCount { get; init; }
    public byte FanDataCount { get; init; }
    public byte Pad33 { get; init; }
}
