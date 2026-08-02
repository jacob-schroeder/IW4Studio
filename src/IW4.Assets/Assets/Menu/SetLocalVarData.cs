using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class SetLocalVarData
{
    public const int SerializedSize = 0x08;

    public XString LocalVarName { get; init; }
    public string? LocalVarNameString { get; set; }
    public XPointer<Statement> Expression { get; init; }
    public Statement? ExpressionStatement { get; set; }
}
