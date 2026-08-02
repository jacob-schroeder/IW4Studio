using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record StringOperandValue(XPointer<string> StringPointer) : OperandValue(StringPointer.Raw);
