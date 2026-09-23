using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record StatementReference(
    int Index,
    XPointer<Statement> Pointer,
    Statement? Statement);
