using System.Numerics;

namespace IW4.Formats.SourceFormat.Material;

// The same reference-preset controls drive the GPU preview and converted RSX program.
public sealed record OceanWaveSettings(float Height, float Wavelength, float Speed, float Direction)
{
    public float FadeWidth => Wavelength * 0.1575f;
    public float MeshSpacing => Wavelength / 36;
    public float ShoreDepth => 12;
    // Preserve a full transmission column even when geometric waves are disabled.
    public float DepthRange => 2 * Height + 96;
    public float SwashDepth => Height * 0.12f;
    public float GradientScale => Math.Max(4 / Math.Max(Height, 1), 1.5f / FadeWidth);

    public (Vector4 Shape, Vector4 Motion) GetParameters()
    {
        float angle = Direction * (MathF.PI / 180);
        // The longest reference wave spans 6 / 0.06 = 100 source units.
        // The four reference vertical wave components have a conservative
        // envelope of 2.3668; scaling by Height/2.4 stays within the +/-Height
        // render bounds.
        return (new(MathF.Cos(angle), MathF.Sin(angle), 100 / Wavelength, Height / 2.4f),
            new(-2 * MathF.PI * Speed / Wavelength, GradientScale,
                DepthRange / ShoreDepth, -Height / ShoreDepth));
    }

    internal OceanWaveSettings Normalize()
    {
        Range(Height, 0, 256, "Ocean height");
        Range(Wavelength, 32, 4096, "Ocean wavelength");
        Range(Speed, 0, 512, "Ocean speed");
        Range(Direction, 0, 360, "Ocean direction");
        return this with { Height = Height == 0 ? 0 : Height, Speed = Speed == 0 ? 0 : Speed,
            Direction = Direction is 0 or 360 ? 0 : Direction };

        static void Range(float value, float minimum, float maximum, string name)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
                throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }
    }
}
