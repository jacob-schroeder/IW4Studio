namespace IW4.Game.Assets.MapEnts;

public sealed class TriggerModel
{
    public const int SerializedSize = 0x08;

    public int Contents { get; init; }
    public ushort HullCount { get; init; }
    public ushort FirstHull { get; init; }
}
