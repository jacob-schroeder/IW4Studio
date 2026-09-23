using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record StringOperandValue(XPointer<string> StringPointer) : OperandValue(StringPointer.Raw);
