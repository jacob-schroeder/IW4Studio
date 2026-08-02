using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record FloatOperandValue(float Value, int EncodedBits) : OperandValue(EncodedBits);
