using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// One native symmetric Gaussian pass. Tap rows map to code constants
/// 0x0A..0x11 and both the vertex and pixel material arguments.
/// </summary>
public struct MapRenderEditorPreviewGlowFilterPass
{
    public int TapHalfCount { get; internal set; }

    public Vector4 Tap0;
    public Vector4 Tap1;
    public Vector4 Tap2;
    public Vector4 Tap3;
    public Vector4 Tap4;
    public Vector4 Tap5;
    public Vector4 Tap6;
    public Vector4 Tap7;

    public readonly Vector4 GetTap(int index) => index switch
    {
        0 => Tap0,
        1 => Tap1,
        2 => Tap2,
        3 => Tap3,
        4 => Tap4,
        5 => Tap5,
        6 => Tap6,
        7 => Tap7,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal void SetTap(int index, Vector4 value)
    {
        switch (index)
        {
            case 0: Tap0 = value; break;
            case 1: Tap1 = value; break;
            case 2: Tap2 = value; break;
            case 3: Tap3 = value; break;
            case 4: Tap4 = value; break;
            case 5: Tap5 = value; break;
            case 6: Tap6 = value; break;
            case 7: Tap7 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

/// <summary>
/// Allocation-free Gaussian-chain producer. The setup/downsample pass is
/// deliberately outside this span and precedes these passes.
/// </summary>
public static class MapRenderEditorPreviewGlowFilterPlanner
{
    public const int MaximumGaussianPassCount = 15;
    public const int MaximumTapHalfCount = 8;

    internal const float MinimumRemainingRadius = 0.3295051157474518f;
    internal const float MaximumCombinedRadius = 1.389560461044312f;
    internal const float MaximumOneDimensionalRadius = 6.497750282287598f;
    internal const float MaximumOneDimensionalRadiusSquared =
        42.22076034545898f;

    public static int Generate(
        float virtualRadius,
        int sceneWidth,
        int sceneHeight,
        Span<MapRenderEditorPreviewGlowFilterPass> destination)
    {
        if (!float.IsFinite(virtualRadius) || virtualRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(virtualRadius));
        if (sceneWidth < 4)
            throw new ArgumentOutOfRangeException(nameof(sceneWidth));
        if (sceneHeight < 4)
            throw new ArgumentOutOfRangeException(nameof(sceneHeight));
        if (destination.Length < MaximumGaussianPassCount)
        {
            throw new ArgumentException(
                $"Glow planning requires {MaximumGaussianPassCount} reusable pass slots.",
                nameof(destination));
        }

        int quarterWidth = sceneWidth >> 2;
        int quarterHeight = sceneHeight >> 2;
        float radius = virtualRadius * 0.25f;
        float radiusY = sceneHeight * radius / 480f;
        float scenePixelAspect =
            (sceneWidth * 3f) / (sceneHeight * 4f);
        float radiusX = radiusY * scenePixelAspect;
        int passCount = 0;

        while (passCount < MaximumGaussianPassCount &&
               (radiusX >= MinimumRemainingRadius ||
                radiusY >= MinimumRemainingRadius))
        {
            if (MathF.Abs(radiusX - radiusY) < MinimumRemainingRadius)
            {
                float combinedRadius = (radiusX + radiusY) * 0.5f;
                if (combinedRadius <= MaximumCombinedRadius)
                {
                    GenerateTwoDimensional(
                        combinedRadius,
                        quarterWidth,
                        quarterHeight,
                        ref destination[passCount++]);
                    break;
                }
            }

            if (radiusY >= radiusX)
            {
                float passRadius;
                if (radiusY > MaximumOneDimensionalRadius)
                {
                    passRadius = MaximumOneDimensionalRadius;
                    radiusY = MathF.Sqrt(
                        radiusY * radiusY -
                        MaximumOneDimensionalRadiusSquared);
                }
                else
                {
                    passRadius = radiusY;
                    radiusY = 0f;
                }
                GenerateOneDimensional(
                    passRadius,
                    quarterHeight,
                    axis: 1,
                    ref destination[passCount++]);
            }
            else
            {
                float passRadius;
                if (radiusX >= MaximumOneDimensionalRadius)
                {
                    passRadius = MaximumOneDimensionalRadius;
                    radiusX = MathF.Sqrt(
                        radiusX * radiusX -
                        MaximumOneDimensionalRadiusSquared);
                }
                else
                {
                    passRadius = radiusX;
                    radiusX = 0f;
                }
                GenerateOneDimensional(
                    passRadius,
                    quarterWidth,
                    axis: 0,
                    ref destination[passCount++]);
            }
        }

        if (radiusX >= MinimumRemainingRadius ||
            radiusY >= MinimumRemainingRadius)
        {
            throw new InvalidOperationException(
                "The PS3 fifteen-pass glow filter limit was exceeded.");
        }

        return passCount;
    }

    private static void GenerateOneDimensional(
        float radius,
        int resolution,
        int axis,
        ref MapRenderEditorPreviewGlowFilterPass pass)
    {
        Span<float> offsets = stackalloc float[MaximumTapHalfCount];
        Span<float> weights = stackalloc float[MaximumTapHalfCount];
        int tapHalfCount = GeneratePointsOneDimensional(
            radius,
            resolution,
            resolution,
            MaximumTapHalfCount,
            offsets,
            weights);
        pass = default;
        pass.TapHalfCount = tapHalfCount;
        for (int tapIndex = 0;
             tapIndex < MaximumTapHalfCount;
             tapIndex++)
        {
            pass.SetTap(
                tapIndex,
                axis == 0
                    ? new Vector4(
                        offsets[tapIndex],
                        0f,
                        0f,
                        weights[tapIndex])
                    : new Vector4(
                        0f,
                        offsets[tapIndex],
                        0f,
                        weights[tapIndex]));
        }
    }

    private static void GenerateTwoDimensional(
        float radius,
        int width,
        int height,
        ref MapRenderEditorPreviewGlowFilterPass pass)
    {
        Span<float> offsetsX = stackalloc float[2];
        Span<float> weightsX = stackalloc float[2];
        Span<float> offsetsY = stackalloc float[2];
        Span<float> weightsY = stackalloc float[2];
        _ = GeneratePointsOneDimensional(
            radius,
            width,
            width,
            2,
            offsetsX,
            weightsX);
        _ = GeneratePointsOneDimensional(
            radius,
            height,
            height,
            2,
            offsetsY,
            weightsY);

        pass = default;
        pass.TapHalfCount = MaximumTapHalfCount;
        int pairIndex = 0;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                float weight = weightsX[x] * weightsY[y];
                pass.SetTap(
                    pairIndex * 2,
                    new Vector4(
                        -offsetsX[x],
                        offsetsY[y],
                        0f,
                        weight));
                pass.SetTap(
                    pairIndex * 2 + 1,
                    new Vector4(
                        offsetsX[x],
                        offsetsY[y],
                        0f,
                        weight));
                pairIndex++;
            }
        }
    }

    private static int GeneratePointsOneDimensional(
        float pixels,
        int sourceResolution,
        int destinationResolution,
        int tapLimit,
        Span<float> offsets,
        Span<float> weights)
    {
        int resolutionRatio = (int)MathF.Floor(
            (float)sourceResolution / destinationResolution + 0.5f);
        float bias = (resolutionRatio & 1) != 0 ? 0f : 0.5f;
        float gaussianExponent = -0.5f / (pixels * pixels);
        float totalWeight = 0f;
        for (int tapIndex = 0; tapIndex < tapLimit; tapIndex++)
        {
            float sample0 = 2f * tapIndex + bias;
            float sample1 = 2f * tapIndex + 1f + bias;
            float weight0 = MathF.Exp(
                sample0 * sample0 * gaussianExponent);
            float weight1 = MathF.Exp(
                sample1 * sample1 * gaussianExponent);
            if (tapIndex == 0 && bias == 0f)
                weight0 *= 0.5f;
            float pairWeight = weight0 + weight1;
            weights[tapIndex] = pairWeight;
            offsets[tapIndex] = pairWeight == 0f
                ? (sample0 + sample1) * 0.5f / sourceResolution
                : (sample0 * weight0 + sample1 * weight1) /
                  (sourceResolution * pairWeight);
            totalWeight += pairWeight;
        }

        if (totalWeight <= 0.001f)
        {
            weights[0] = 0.5f;
            return 1;
        }

        int tapHalfCount = tapLimit;
        float weightScale = 0.5f / totalWeight;
        for (int tapIndex = tapLimit - 1; tapIndex >= 0; tapIndex--)
        {
            weights[tapIndex] *= weightScale;
            if (weights[tapIndex] < 0.01f)
                tapHalfCount = tapIndex + 1;
        }
        return tapHalfCount;
    }
}
