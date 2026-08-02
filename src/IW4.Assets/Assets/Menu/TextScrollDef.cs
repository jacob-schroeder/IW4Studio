using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Menu;

public sealed class TextScrollDef
{
    public const int SerializedSize = 0x04;

    // Destination of the copied start-time cache in XBlock memory.
    public XBlockAddress? DestinationAddress { get; init; }

    public int StartTime { get; init; }
}
