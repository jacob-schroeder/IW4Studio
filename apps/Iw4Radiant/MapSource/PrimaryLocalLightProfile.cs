using System.Numerics;
using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.LightDef;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.LightDef;

namespace Iw4Radiant.MapSource;

/// <summary>The recovered PS3 light_point_linear attenuation lookup.</summary>
internal static class PrimaryLocalLightProfile
{
    internal const int Width = 32;
    internal const int LookupStart = 1;
    private static readonly Lazy<byte[]> BaseSamples = new(LoadBaseSamples);

    internal static ReadOnlySpan<byte> Samples => BaseSamples.Value;

    internal static float Sample(float normalizedDistance)
    {
        if (!float.IsFinite(normalizedDistance))
            throw new ArgumentOutOfRangeException(nameof(normalizedDistance));
        ReadOnlySpan<byte> samples = Samples;
        float texel = Math.Clamp(Math.Clamp(normalizedDistance, 0, 1) * Width - 0.5f, 0, Width - 1);
        int first = (int)MathF.Floor(texel);
        int second = Math.Min(first + 1, Width - 1);
        float fraction = texel - first;
        float filtered = (samples[first] + (samples[second] - samples[first]) * fraction) / 255f;
        return filtered * filtered;
    }

    internal static float SpotAttenuation(float cosine, float innerCos, float outerCos, float exponent)
    {
        float cone = Math.Clamp((cosine - outerCos) / (innerCos - outerCos), 0, 1);
        // The native shader keeps zero outside the cone, including exponent zero.
        return cone > 0 ? MathF.Pow(cone, exponent) : 0;
    }

    internal static bool SpotIntersectsBounds(Vector3 origin, Vector3 forward, float outerCos,
        Vector3 minimum, Vector3 maximum)
    {
        // Cull the box's enclosing sphere against the infinite cone. This is
        // conservative for authored face/model assignment, not the stock baker's
        // ownership heuristic. The caller separately checks the light radius.
        Vector3 halfSize = (maximum - minimum) * 0.5f;
        Vector3 delta = minimum + halfSize - origin;
        double queryRadius = halfSize.Length();
        double axial = Vector3.Dot(delta, forward);
        double lengthSquared = delta.LengthSquared();
        double radial = Math.Sqrt(Math.Max(0, lengthSquared - axial * axial));
        double sine = Math.Sqrt(Math.Max(0, 1 - (double)outerCos * outerCos));
        if (radial * outerCos - axial * sine > queryRadius) return false;
        // Behind the cone tip, the nearest point may be its apex rather than a side.
        return axial * outerCos + radial * sine >= 0 || lengthSquared <= queryRadius * queryRadius;
    }

    internal static void WriteLookupRow(Span<byte> secondaryRgba)
    {
        if (secondaryRgba.Length < (Width + LookupStart + 1) * 4)
            throw new ArgumentException("The secondary lightmap row is too short for the point-light lookup.", nameof(secondaryRgba));
        ReadOnlySpan<byte> samples = Samples;
        for (int x = 0; x < LookupStart + Width + 1; x++)
        {
            byte value = samples[Math.Clamp(x - LookupStart, 0, Width - 1)];
            int offset = x * 4;
            secondaryRgba[offset] = value;
            secondaryRgba[offset + 1] = value;
            secondaryRgba[offset + 2] = value;
            secondaryRgba[offset + 3] = byte.MaxValue;
        }
    }

    private static byte[] LoadBaseSamples()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
        LightDefAsset definition = new LightDefExchange().LinkPointLinear(root,
            name => new ImageExchange().Link(root, name));
        GfxImageAsset image = definition.Image ??
            throw new InvalidDataException("The primary local-light profile has no attenuation image.");
        return image.PayloadBytes.Take(Width).ToArray();
    }
}
