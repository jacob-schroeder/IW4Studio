using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Menu;

public sealed class StaticDvar
{
    public const int SerializedSize = 0x08;

    // Destination of the copied runtime-cache/name pair in XBlock memory.
    public XBlockAddress? DestinationAddress { get; init; }

    // Runtime cache slot populated from DvarName on first STATICDVAR* use.
    public XPointer<DvarRuntimeHandle> Dvar { get; init; }

    public XPointer<string> DvarName { get; init; }
    public string? DvarNameString { get; set; }
}
