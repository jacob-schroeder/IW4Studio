using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record FunctionOperandValue(XPointer<Statement> StatementPointer) : OperandValue(StatementPointer.Raw);
