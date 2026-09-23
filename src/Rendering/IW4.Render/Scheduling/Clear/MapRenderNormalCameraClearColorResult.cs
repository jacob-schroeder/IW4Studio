namespace IW4.Render.Scheduling.Clear;

/// <summary>Result of the PS3 <c>R_GetClearColor</c> semantic producer.</summary>
public sealed record MapRenderNormalCameraClearColorResult
{
    internal MapRenderNormalCameraClearColorResult(
        bool requestsColorClear,
        MapRenderNormalCameraClearColorSource source,
        float red,
        float green,
        float blue,
        float alpha)
    {
        Validate(red, nameof(red));
        Validate(green, nameof(green));
        Validate(blue, nameof(blue));
        Validate(alpha, nameof(alpha));

        RequestsColorClear = requestsColorClear;
        Source = source;
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public bool RequestsColorClear { get; }

    public MapRenderNormalCameraClearColorSource Source { get; }

    public float Red { get; }

    public float Green { get; }

    public float Blue { get; }

    public float Alpha { get; }

    private static void Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0.0f or > 1.0f)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
