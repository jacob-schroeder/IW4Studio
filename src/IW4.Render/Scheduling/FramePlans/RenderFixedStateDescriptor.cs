using System.Numerics;

namespace IW4.Render.Scheduling.FramePlans;

public enum RenderCullMode
{
    None,
    Front,
    Back
}

public enum RenderFrontFace
{
    CounterClockwise,
    Clockwise
}

public enum RenderPolygonMode
{
    Fill,
    Line,
    Point
}

public enum RenderCompareOperation
{
    Never,
    Less,
    Equal,
    LessOrEqual,
    Greater,
    NotEqual,
    GreaterOrEqual,
    Always
}

public enum RenderStencilOperation
{
    Keep,
    Zero,
    Replace,
    IncrementAndClamp,
    DecrementAndClamp,
    Invert,
    IncrementAndWrap,
    DecrementAndWrap
}

public enum RenderBlendFactor
{
    Zero,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
    ConstantColor,
    OneMinusConstantColor,
    ConstantAlpha,
    OneMinusConstantAlpha,
    SourceAlphaSaturate,
    Source1Color,
    OneMinusSource1Color,
    Source1Alpha,
    OneMinusSource1Alpha
}

public enum RenderBlendOperation
{
    Add,
    Subtract,
    ReverseSubtract,
    Minimum,
    Maximum
}

/// <summary>
/// Backend-neutral transfer applied to fragment color exports before fixed
/// blending. This is semantic RSX state, not an attachment format: using an
/// sRGB attachment would move the transfer across blending and change the
/// result for authored multiply/additive state.
/// </summary>
public enum RenderFragmentOutputTransfer
{
    Linear = 0,
    RsxShaderPackerSrgb
}

[Flags]
public enum RenderColorWriteMask : byte
{
    None = 0,
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2,
    Alpha = 1 << 3,
    Rgb = Red | Green | Blue,
    Rgba = Rgb | Alpha
}

/// <summary>
/// Semantic polygon depth-bias intent. It is independent of API dynamic-state
/// policy and contains no backend values.
/// </summary>
public readonly record struct RenderDepthBiasDescriptor
{
    public RenderDepthBiasDescriptor(
        bool enabled,
        float constantFactor,
        float clamp,
        float slopeFactor)
    {
        ValidateFinite(constantFactor, nameof(constantFactor));
        ValidateFinite(clamp, nameof(clamp));
        ValidateFinite(slopeFactor, nameof(slopeFactor));
        if (!enabled &&
            (constantFactor != 0f || clamp != 0f || slopeFactor != 0f))
        {
            throw new ArgumentException(
                "Disabled depth bias requires zero factors.",
                nameof(enabled));
        }

        Enabled = enabled;
        ConstantFactor = CanonicalizeZero(constantFactor);
        Clamp = CanonicalizeZero(clamp);
        SlopeFactor = CanonicalizeZero(slopeFactor);
    }

    public bool Enabled { get; }

    public float ConstantFactor { get; }

    public float Clamp { get; }

    public float SlopeFactor { get; }

    public static RenderDepthBiasDescriptor Disabled { get; } =
        new(false, 0f, 0f, 0f);

    internal void Validate(string parameterName)
    {
        if (!float.IsFinite(ConstantFactor) ||
            !float.IsFinite(Clamp) ||
            !float.IsFinite(SlopeFactor) ||
            (!Enabled &&
             (ConstantFactor != 0f || Clamp != 0f || SlopeFactor != 0f)))
        {
            throw new ArgumentException(
                "Depth-bias state is not canonical and finite.",
                parameterName);
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static float CanonicalizeZero(float value) =>
        value == 0f ? 0f : value;
}

public readonly record struct RenderRasterStateDescriptor
{
    public RenderRasterStateDescriptor(
        RenderCullMode cullMode,
        RenderFrontFace frontFace,
        RenderPolygonMode polygonMode,
        RenderDepthBiasDescriptor depthBias)
        : this(
            cullMode,
            frontFace,
            polygonMode,
            depthBias,
            lineWidth: 1f)
    {
    }

    public RenderRasterStateDescriptor(
        RenderCullMode cullMode,
        RenderFrontFace frontFace,
        RenderPolygonMode polygonMode,
        RenderDepthBiasDescriptor depthBias,
        float lineWidth)
    {
        ValidateEnum(cullMode, nameof(cullMode));
        ValidateEnum(frontFace, nameof(frontFace));
        ValidateEnum(polygonMode, nameof(polygonMode));
        depthBias.Validate(nameof(depthBias));
        lineWidth = lineWidth == 0f ? 0f : lineWidth;
        if (!float.IsFinite(lineWidth) || lineWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(lineWidth));

        CullMode = cullMode;
        FrontFace = frontFace;
        PolygonMode = polygonMode;
        DepthBias = depthBias;
        LineWidth = lineWidth;
    }

    public RenderCullMode CullMode { get; }

    public RenderFrontFace FrontFace { get; }

    public RenderPolygonMode PolygonMode { get; }

    public RenderDepthBiasDescriptor DepthBias { get; }

    /// <summary>
    /// Requested rasterized line width in physical pixels. Backends retain
    /// ownership of capability checks and any documented compatibility
    /// lowering; this value contains no API-specific dynamic-state policy.
    /// </summary>
    public float LineWidth { get; }

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(CullMode) ||
            !Enum.IsDefined(FrontFace) ||
            !Enum.IsDefined(PolygonMode) ||
            !float.IsFinite(LineWidth) ||
            LineWidth <= 0f)
        {
            throw new ArgumentException(
                "Raster state contains an undefined semantic value or invalid line width.",
                parameterName);
        }

        DepthBias.Validate(parameterName);
    }

    private static void ValidateEnum<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public readonly record struct RenderDepthStateDescriptor
{
    public RenderDepthStateDescriptor(
        bool testEnabled,
        bool writeEnabled,
        RenderCompareOperation compareOperation)
    {
        if (!Enum.IsDefined(compareOperation))
            throw new ArgumentOutOfRangeException(nameof(compareOperation));
        if (!testEnabled &&
            (writeEnabled || compareOperation != RenderCompareOperation.Always))
        {
            throw new ArgumentException(
                "Disabled depth testing requires writes disabled and an Always comparison.",
                nameof(testEnabled));
        }

        TestEnabled = testEnabled;
        WriteEnabled = writeEnabled;
        CompareOperation = compareOperation;
    }

    public bool TestEnabled { get; }

    public bool WriteEnabled { get; }

    public RenderCompareOperation CompareOperation { get; }

    public static RenderDepthStateDescriptor Disabled { get; } =
        new(false, false, RenderCompareOperation.Always);

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(CompareOperation) ||
            (!TestEnabled &&
             (WriteEnabled ||
              CompareOperation != RenderCompareOperation.Always)))
        {
            throw new ArgumentException(
                "Depth state is undefined or not canonically disabled.",
                parameterName);
        }
    }
}

public readonly record struct RenderStencilFaceDescriptor
{
    public RenderStencilFaceDescriptor(
        RenderCompareOperation compareOperation,
        uint compareMask,
        uint writeMask,
        uint referenceValue,
        RenderStencilOperation failOperation,
        RenderStencilOperation depthFailOperation,
        RenderStencilOperation passOperation)
    {
        if (!Enum.IsDefined(compareOperation))
            throw new ArgumentOutOfRangeException(nameof(compareOperation));
        if (!Enum.IsDefined(failOperation))
            throw new ArgumentOutOfRangeException(nameof(failOperation));
        if (!Enum.IsDefined(depthFailOperation))
            throw new ArgumentOutOfRangeException(nameof(depthFailOperation));
        if (!Enum.IsDefined(passOperation))
            throw new ArgumentOutOfRangeException(nameof(passOperation));

        CompareOperation = compareOperation;
        CompareMask = compareMask;
        WriteMask = writeMask;
        ReferenceValue = referenceValue;
        FailOperation = failOperation;
        DepthFailOperation = depthFailOperation;
        PassOperation = passOperation;
    }

    public RenderCompareOperation CompareOperation { get; }

    public uint CompareMask { get; }

    public uint WriteMask { get; }

    public uint ReferenceValue { get; }

    public RenderStencilOperation FailOperation { get; }

    public RenderStencilOperation DepthFailOperation { get; }

    public RenderStencilOperation PassOperation { get; }

    public static RenderStencilFaceDescriptor Disabled { get; } = new(
        RenderCompareOperation.Always,
        uint.MaxValue,
        uint.MaxValue,
        0,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep);

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(CompareOperation) ||
            !Enum.IsDefined(FailOperation) ||
            !Enum.IsDefined(DepthFailOperation) ||
            !Enum.IsDefined(PassOperation))
        {
            throw new ArgumentException(
                "Stencil-face state contains an undefined semantic value.",
                parameterName);
        }
    }
}

public readonly record struct RenderStencilStateDescriptor
{
    public RenderStencilStateDescriptor(
        bool enabled,
        RenderStencilFaceDescriptor front,
        RenderStencilFaceDescriptor back)
    {
        front.Validate(nameof(front));
        back.Validate(nameof(back));
        if (!enabled &&
            (front != RenderStencilFaceDescriptor.Disabled ||
             back != RenderStencilFaceDescriptor.Disabled))
        {
            throw new ArgumentException(
                "Disabled stencil testing requires canonical front and back state.",
                nameof(enabled));
        }

        Enabled = enabled;
        Front = front;
        Back = back;
    }

    public bool Enabled { get; }

    public RenderStencilFaceDescriptor Front { get; }

    public RenderStencilFaceDescriptor Back { get; }

    public static RenderStencilStateDescriptor Disabled { get; } = new(
        false,
        RenderStencilFaceDescriptor.Disabled,
        RenderStencilFaceDescriptor.Disabled);

    internal void Validate(string parameterName)
    {
        Front.Validate(parameterName);
        Back.Validate(parameterName);
        if (!Enabled &&
            (Front != RenderStencilFaceDescriptor.Disabled ||
             Back != RenderStencilFaceDescriptor.Disabled))
        {
            throw new ArgumentException(
                "Stencil state is not canonically disabled.",
                parameterName);
        }
    }
}

public readonly record struct RenderBlendStateDescriptor
{
    public RenderBlendStateDescriptor(
        bool enabled,
        RenderBlendFactor sourceColorFactor,
        RenderBlendFactor destinationColorFactor,
        RenderBlendOperation colorOperation,
        RenderBlendFactor sourceAlphaFactor,
        RenderBlendFactor destinationAlphaFactor,
        RenderBlendOperation alphaOperation,
        Vector4 constantColor)
    {
        ValidateEnum(sourceColorFactor, nameof(sourceColorFactor));
        ValidateEnum(destinationColorFactor, nameof(destinationColorFactor));
        ValidateEnum(colorOperation, nameof(colorOperation));
        ValidateEnum(sourceAlphaFactor, nameof(sourceAlphaFactor));
        ValidateEnum(destinationAlphaFactor, nameof(destinationAlphaFactor));
        ValidateEnum(alphaOperation, nameof(alphaOperation));
        ValidateFinite(constantColor, nameof(constantColor));
        if (!enabled &&
            (sourceColorFactor != RenderBlendFactor.One ||
             destinationColorFactor != RenderBlendFactor.Zero ||
             colorOperation != RenderBlendOperation.Add ||
             sourceAlphaFactor != RenderBlendFactor.One ||
             destinationAlphaFactor != RenderBlendFactor.Zero ||
             alphaOperation != RenderBlendOperation.Add ||
             constantColor != Vector4.Zero))
        {
            throw new ArgumentException(
                "Disabled blending requires canonical factors, operations, and constant color.",
                nameof(enabled));
        }

        Enabled = enabled;
        SourceColorFactor = sourceColorFactor;
        DestinationColorFactor = destinationColorFactor;
        ColorOperation = colorOperation;
        SourceAlphaFactor = sourceAlphaFactor;
        DestinationAlphaFactor = destinationAlphaFactor;
        AlphaOperation = alphaOperation;
        ConstantColor = CanonicalizeZero(constantColor);
    }

    public bool Enabled { get; }

    public RenderBlendFactor SourceColorFactor { get; }

    public RenderBlendFactor DestinationColorFactor { get; }

    public RenderBlendOperation ColorOperation { get; }

    public RenderBlendFactor SourceAlphaFactor { get; }

    public RenderBlendFactor DestinationAlphaFactor { get; }

    public RenderBlendOperation AlphaOperation { get; }

    public Vector4 ConstantColor { get; }

    public static RenderBlendStateDescriptor Disabled { get; } = new(
        false,
        RenderBlendFactor.One,
        RenderBlendFactor.Zero,
        RenderBlendOperation.Add,
        RenderBlendFactor.One,
        RenderBlendFactor.Zero,
        RenderBlendOperation.Add,
        Vector4.Zero);

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(SourceColorFactor) ||
            !Enum.IsDefined(DestinationColorFactor) ||
            !Enum.IsDefined(ColorOperation) ||
            !Enum.IsDefined(SourceAlphaFactor) ||
            !Enum.IsDefined(DestinationAlphaFactor) ||
            !Enum.IsDefined(AlphaOperation) ||
            !IsFinite(ConstantColor) ||
            (!Enabled && this != Disabled))
        {
            throw new ArgumentException(
                "Blend state is undefined, non-finite, or not canonically disabled.",
                parameterName);
        }
    }

    private static void ValidateEnum<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateFinite(
        Vector4 value,
        string parameterName)
    {
        if (!IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static Vector4 CanonicalizeZero(Vector4 value) => new(
        value.X == 0f ? 0f : value.X,
        value.Y == 0f ? 0f : value.Y,
        value.Z == 0f ? 0f : value.Z,
        value.W == 0f ? 0f : value.W);
}

/// <summary>
/// Immutable backend-neutral fixed-function rendering intent. Backends lower
/// this semantic state independently and retain ownership of API objects.
/// </summary>
public sealed class RenderFixedStateDescriptor
{
    public RenderFixedStateDescriptor(
        RenderSemanticIdentity identity,
        RenderRasterStateDescriptor raster,
        RenderDepthStateDescriptor depth,
        RenderStencilStateDescriptor stencil,
        RenderBlendStateDescriptor blend,
        RenderColorWriteMask colorWriteMask,
        RenderFragmentOutputTransfer fragmentOutputTransfer =
            RenderFragmentOutputTransfer.Linear)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.FixedState);
        raster.Validate(nameof(raster));
        depth.Validate(nameof(depth));
        stencil.Validate(nameof(stencil));
        blend.Validate(nameof(blend));
        if ((colorWriteMask & ~RenderColorWriteMask.Rgba) != 0)
            throw new ArgumentOutOfRangeException(nameof(colorWriteMask));
        if (!Enum.IsDefined(fragmentOutputTransfer))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fragmentOutputTransfer));
        }

        Identity = identity;
        Raster = raster;
        Depth = depth;
        Stencil = stencil;
        Blend = blend;
        ColorWriteMask = colorWriteMask;
        FragmentOutputTransfer = fragmentOutputTransfer;
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderRasterStateDescriptor Raster { get; }

    public RenderDepthStateDescriptor Depth { get; }

    public RenderStencilStateDescriptor Stencil { get; }

    public RenderBlendStateDescriptor Blend { get; }

    public RenderColorWriteMask ColorWriteMask { get; }

    /// <summary>
    /// Semantic transfer applied by each backend's authored-program lowering
    /// before the blend operation. It does not request an sRGB attachment.
    /// </summary>
    public RenderFragmentOutputTransfer FragmentOutputTransfer { get; }
}

public static class RenderFixedStatePresets
{
    public const int SkyVersion = 1;
    public const int DiagnosticsVersion = 1;
    public const int WireframeVersion = 1;

    public static RenderFixedStateDescriptor SkyV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.sky.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled),
        new RenderDepthStateDescriptor(
            testEnabled: true,
            writeEnabled: false,
            RenderCompareOperation.LessOrEqual),
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);

    public static RenderFixedStateDescriptor DiagnosticsV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.diagnostics.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled),
        new RenderDepthStateDescriptor(
            testEnabled: true,
            writeEnabled: true,
            RenderCompareOperation.LessOrEqual),
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);

    /// <summary>
    /// Legacy collision-wireframe intent: vertex-colored indexed lines are
    /// overlaid without reading or updating scene depth at a 1.25-pixel width.
    /// </summary>
    public static RenderFixedStateDescriptor WireframeV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.wireframe.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled,
            lineWidth: 1.25f),
        RenderDepthStateDescriptor.Disabled,
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);
}
