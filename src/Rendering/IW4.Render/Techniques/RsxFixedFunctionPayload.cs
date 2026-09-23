namespace IW4.Render.Techniques;

/// <summary>RSX comparison-function method payload.</summary>
public enum RsxCompareFunction : uint
{
    Never = 0x0200,
    Less = 0x0201,
    Equal = 0x0202,
    LessThanOrEqual = 0x0203,
    Greater = 0x0204,
    NotEqual = 0x0205,
    GreaterThanOrEqual = 0x0206,
    Always = 0x0207
}

/// <summary>RSX cull-face method payload.</summary>
public enum RsxCullFace : uint
{
    Front = 0x0404,
    Back = 0x0405
}

/// <summary>RSX polygon-mode method payload.</summary>
public enum RsxPolygonMode : uint
{
    Line = 0x1b01,
    Fill = 0x1b02
}

/// <summary>RSX blend-equation method payload.</summary>
public enum RsxBlendEquation : uint
{
    Add = 0x8006,
    Minimum = 0x8007,
    Maximum = 0x8008,
    Subtract = 0x800a,
    ReverseSubtract = 0x800b
}

/// <summary>RSX blend-factor method payload.</summary>
public enum RsxBlendFactor : uint
{
    Zero = 0,
    One = 1,
    SourceColor = 0x0300,
    OneMinusSourceColor = 0x0301,
    SourceAlpha = 0x0302,
    OneMinusSourceAlpha = 0x0303,
    DestinationAlpha = 0x0304,
    OneMinusDestinationAlpha = 0x0305,
    DestinationColor = 0x0306,
    OneMinusDestinationColor = 0x0307
}

/// <summary>RSX stencil-operation method payload.</summary>
public enum RsxStencilOperation : uint
{
    Zero = 0,
    Invert = 0x150a,
    Keep = 0x1e00,
    Replace = 0x1e01,
    IncrementSaturate = 0x1e02,
    DecrementSaturate = 0x1e03,
    IncrementWrap = 0x8507,
    DecrementWrap = 0x8508
}

/// <summary>
/// RSX primary-target color-mask payload. Each flag occupies the byte emitted
/// for its channel by the material-state writer.
/// </summary>
[Flags]
public enum RsxColorMask : uint
{
    None = 0,
    Blue = 0x00000001,
    Green = 0x00000100,
    Red = 0x00010000,
    Alpha = 0x01000000,
    Rgb = Red | Green | Blue,
    Rgba = Rgb | Alpha
}
