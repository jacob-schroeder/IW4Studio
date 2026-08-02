using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record ReservedOperandValue(int Reserved) : OperandValue(Reserved);
