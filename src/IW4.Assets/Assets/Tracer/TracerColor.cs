namespace IW4.Assets.Assets.Tracer;

// One of five consecutive 0x10-byte color rows in the TracerDef root.
public readonly record struct TracerColor(float Red, float Green, float Blue, float Alpha)
{
    public const int SerializedSize = 0x10;
}
