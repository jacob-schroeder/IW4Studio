using System.Numerics;

namespace IW4.AssetExchange.SourceFormat.Material;

// The same packed waves drive Radiant's GPU preview and the packaged RSX program.
public sealed record OceanWaveSettings(float Height, float Wavelength, float Speed, float Direction)
{
    public float FadeWidth => Wavelength * 0.1575f;
    public float MeshSpacing => Wavelength * (0.63f / 12);

    public (Vector4 First, Vector4 Second) GetWaves()
    {
        float angle = Direction * (MathF.PI / 180);
        return (Wave(angle, Wavelength, Speed, Height * 0.65f),
            Wave(angle + MathF.PI * 0.37f, Wavelength * 0.63f, Speed * 0.8f, Height * 0.35f));

        static Vector4 Wave(float angle, float length, float speed, float amplitude)
        {
            float frequency = 2 * MathF.PI / length;
            return new(MathF.Cos(angle) * frequency, MathF.Sin(angle) * frequency, -speed * frequency, amplitude);
        }
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
