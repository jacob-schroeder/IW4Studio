using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public static class OperandValueFactory
{
    public static OperandValue FromEncoded(ExpDataType dataType, int encodedValue)
    {
        return dataType switch
        {
            ExpDataType.VAL_INT => new IntOperandValue(encodedValue),
            ExpDataType.VAL_FLOAT => new FloatOperandValue(BitConverter.Int32BitsToSingle(encodedValue), encodedValue),
            ExpDataType.VAL_STRING => new StringOperandValue(new XPointer<string>(encodedValue, XPointerResolutionMode.Direct)),
            ExpDataType.VAL_FUNCTION => new FunctionOperandValue(new XPointer<Statement>(encodedValue, XPointerResolutionMode.Direct)),
            _ => new ReservedOperandValue(encodedValue)
        };
    }
}
