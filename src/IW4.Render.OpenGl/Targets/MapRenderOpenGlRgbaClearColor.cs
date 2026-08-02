namespace IW4.Render.OpenGl.Targets;

/// <summary>Explicit runtime-supplied RGBA clear color.</summary>
public readonly record struct MapRenderOpenGlRgbaClearColor
{
    public MapRenderOpenGlRgbaClearColor(
        float red,
        float green,
        float blue,
        float alpha)
    {
        Validate(red, nameof(red));
        Validate(green, nameof(green));
        Validate(blue, nameof(blue));
        Validate(alpha, nameof(alpha));
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

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
