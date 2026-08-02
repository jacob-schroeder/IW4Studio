using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record FunctionOperandValue(XPointer<Statement> StatementPointer) : OperandValue(StatementPointer.Raw);
