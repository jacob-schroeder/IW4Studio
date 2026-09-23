using System.Numerics;

namespace IW4.Render.Scheduling.FramePlans;

/// <summary>
/// Stable semantic identity for one attachment. The value names renderer
/// intent and is never a graphics-API resource handle.
/// </summary>
public readonly record struct RenderAttachmentIdentity
{
    public RenderAttachmentIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Stable semantic identity for one ordered render pass.
/// </summary>
public readonly record struct RenderPassIdentity
{
    public RenderPassIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Stable semantic identity for a scene-lifetime render resource or
/// descriptor. The kind prevents an identity from silently changing roles.
/// </summary>
public readonly record struct RenderSemanticIdentity
{
    public RenderSemanticIdentity(
        RenderSemanticResourceKind kind,
        string value)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Kind = kind;
        Value = value;
    }

    public RenderSemanticResourceKind Kind { get; }

    public string Value { get; }

    public override string ToString() => $"{Kind}:{Value}";
}

public enum RenderSemanticResourceKind
{
    Draw,
    Pipeline,
    Material,
    ShaderProgram,
    VertexLayout,
    FixedState,
    Geometry,
    Instances,
    Texture,
    Sampler,
    DynamicConstant,
    InstanceLayout
}

public enum RenderAttachmentRole
{
    Color,
    DepthStencil,
    Picking
}

/// <summary>
/// Authored attachment format requested by renderer intent. Backends choose
/// their own compatible API format and report failure when none exists.
/// </summary>
public enum RenderAttachmentPixelFormat
{
    Rgba8Unorm,
    Rgba8Srgb,
    Depth24Stencil8,
    R32UnsignedInteger
}

public enum RenderAttachmentLoadRequirement
{
    Preserve,
    Clear,
    Discard
}

public enum RenderAttachmentStoreRequirement
{
    Preserve,
    Discard
}

public readonly record struct RenderColorClearValue
{
    public RenderColorClearValue(
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

    public Vector4 Vector => new(Red, Green, Blue, Alpha);

    private static void Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public enum RenderAttachmentClearValueKind : byte
{
    NormalizedColor = 1,
    UnsignedInteger = 2
}

/// <summary>
/// Format-neutral attachment clear intent. The frame validates the selected
/// representation against the attachment format before either backend sees
/// it.
/// </summary>
public readonly record struct RenderAttachmentClearValue
{
    private RenderAttachmentClearValue(
        RenderAttachmentClearValueKind kind,
        RenderColorClearValue normalizedColor,
        uint unsignedInteger)
    {
        Kind = kind;
        NormalizedColor = normalizedColor;
        UnsignedInteger = unsignedInteger;
    }

    public RenderAttachmentClearValueKind Kind { get; }

    public RenderColorClearValue NormalizedColor { get; }

    public uint UnsignedInteger { get; }

    public static RenderAttachmentClearValue FromNormalizedColor(
        RenderColorClearValue value) => new(
            RenderAttachmentClearValueKind.NormalizedColor,
            value,
            unsignedInteger: 0);

    public static RenderAttachmentClearValue FromUnsignedInteger(
        uint value) => new(
            RenderAttachmentClearValueKind.UnsignedInteger,
            normalizedColor: default,
            value);

    public static implicit operator RenderAttachmentClearValue(
        RenderColorClearValue value) => FromNormalizedColor(value);

    internal void Validate(string parameterName)
    {
        if (Kind is not
            (RenderAttachmentClearValueKind.NormalizedColor or
             RenderAttachmentClearValueKind.UnsignedInteger))
        {
            throw new ArgumentException(
                "An attachment clear value requires a typed representation.",
                parameterName);
        }
    }
}

/// <summary>
/// Viewport in attachment pixels with an upper-left plan origin. Backends
/// explicitly lower this convention to their API coordinate system.
/// </summary>
public readonly record struct RenderViewport
{
    public RenderViewport(
        int x,
        int y,
        int width,
        int height,
        float minimumDepth = 0f,
        float maximumDepth = 1f)
    {
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!float.IsFinite(minimumDepth) ||
            !float.IsFinite(maximumDepth) ||
            minimumDepth is < 0f or > 1f ||
            maximumDepth is < 0f or > 1f ||
            minimumDepth > maximumDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDepth));
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        MinimumDepth = minimumDepth;
        MaximumDepth = maximumDepth;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public float MinimumDepth { get; }

    public float MaximumDepth { get; }

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);
}

/// <summary>
/// Scissor in attachment pixels with an upper-left plan origin.
/// </summary>
public readonly record struct RenderScissor
{
    public RenderScissor(int x, int y, int width, int height)
    {
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);
}

public enum RenderPassPurpose
{
    NormalCameraScene,
    SunShadow,
    Sky,
    Diagnostics,
    DepthPrepass,
    WorldOpaque,
    WorldCutout,
    StaticOpaque,
    StaticCutout,
    Translucent,
    Wireframe,
    Presentation,
    Picking,
    Preview
}

public enum RenderPrimitiveTopology
{
    TriangleList,
    LineList,
    TriangleStrip
}

public enum RenderIndexFormat
{
    Unsigned16,
    Unsigned32
}

[Flags]
public enum RenderPreviewDrawRequirement
{
    None = 0,
    VisibleInPreview = 1 << 0,
    EligibleForScreenshot = 1 << 1,
    EligibleForIsolation = 1 << 2
}

public enum RenderPickingMode
{
    None,
    SinglePixel,
    Region
}
