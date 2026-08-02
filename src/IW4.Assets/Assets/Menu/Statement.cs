using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Menu;

public sealed class Statement
{
    public const int SerializedSize = 0x18;

    // Destination of the copied 0x18-byte object. UI runtime services use the
    // address to update the evaluator cache without conflating it with the
    // serialized source offset or an XAsset identity.
    public XBlockAddress? DestinationAddress { get; init; }

    public int NumEntries { get; init; }
    public XPointer<ExpressionEntry[]> Entries { get; init; }
    public IReadOnlyList<ExpressionEntry> LoadedEntries { get; set; } = [];
    public XPointer<ExpressionSupportingData> SupportingData { get; init; }
    public ExpressionSupportingData? SupportingDataValue { get; set; }

    // Runtime evaluator cache stamp compared with the UI expression clock.
    public int LastExecuteTime { get; init; }

    // Runtime expression-result cache.
    public Operand LastResult { get; init; } = new();
}
