namespace IW4.Assets.Assets.Menu;

public sealed class NewsTickerDef
{
    public const int SerializedSize = 0x1c;

    public int FeedId { get; init; }
    public int Speed { get; init; }
    public int Spacing { get; init; }
    public int LastTime { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public float X { get; init; }
}
