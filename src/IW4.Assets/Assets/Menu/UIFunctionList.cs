using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class UIFunctionList
{
    public const int SerializedSize = 0x08;

    public int TotalFunctions { get; init; }
    public XPointer<XPointer<Statement>[]> Functions { get; init; }
    public IReadOnlyList<StatementReference> LoadedFunctions { get; set; } = [];
}
