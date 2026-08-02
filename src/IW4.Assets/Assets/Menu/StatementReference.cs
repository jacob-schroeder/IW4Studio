using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record StatementReference(
    int Index,
    XPointer<Statement> Pointer,
    Statement? Statement);
