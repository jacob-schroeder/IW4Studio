using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record FloatOperandValue(float Value, int EncodedBits) : OperandValue(EncodedBits);
