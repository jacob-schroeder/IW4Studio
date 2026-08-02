namespace IW4.Assets.Assets.GameMap;

public sealed class PathLink
{
    public const int SerializedSize = 0x0C;

    public float Distance { get; init; }
    public ushort NodeNumber { get; init; }
    public byte DisconnectCount { get; init; }
    public byte NegotiationLink { get; init; }
    public byte BadPlaceCount0 { get; init; }
    public byte BadPlaceCount1 { get; init; }
    public byte BadPlaceCount2 { get; init; }
    public byte BadPlaceCount3 { get; init; }
}
