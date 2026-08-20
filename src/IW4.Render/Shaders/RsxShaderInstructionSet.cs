namespace IW4.Render.Shaders;

public enum RsxFragmentOpcode : byte
{
    Nop = 0x00,
    Move = 0x01,
    Multiply = 0x02,
    Add = 0x03,
    MultiplyAdd = 0x04,
    Dot3 = 0x05,
    Dot4 = 0x06,
    Distance = 0x07,
    Minimum = 0x08,
    Maximum = 0x09,
    SetLessThan = 0x0A,
    SetGreaterThanOrEqual = 0x0B,
    SetLessThanOrEqual = 0x0C,
    SetGreaterThan = 0x0D,
    SetNotEqual = 0x0E,
    SetEqual = 0x0F,
    Fraction = 0x10,
    Floor = 0x11,
    Kill = 0x12,
    PackFourSignedBytes = 0x13,
    UnpackFourSignedBytes = 0x14,
    DerivativeX = 0x15,
    DerivativeY = 0x16,
    Texture = 0x17,
    TextureProjective = 0x18,
    TextureGradients = 0x19,
    Reciprocal = 0x1A,
    ReciprocalSquareRoot = 0x1B,
    ExponentBase2 = 0x1C,
    LogarithmBase2 = 0x1D,
    Lighting = 0x1E,
    LinearInterpolate = 0x1F,
    SetTrue = 0x20,
    SetFalse = 0x21,
    Cosine = 0x22,
    Sine = 0x23,
    PackTwoHalfFloats = 0x24,
    UnpackTwoHalfFloats = 0x25,
    Power = 0x26,
    PackBytes = 0x27,
    UnpackBytes = 0x28,
    Pack16 = 0x29,
    Unpack16 = 0x2A,
    BumpEnvironmentMap = 0x2B,
    PackGamma = 0x2C,
    UnpackGamma = 0x2D,
    Dot2Add = 0x2E,
    TextureLod = 0x2F,
    Reserved30 = 0x30,
    TextureBias = 0x31,
    Reserved32 = 0x32,
    TextureBumpEnvironmentMap = 0x33,
    TextureProjectiveBumpEnvironmentMap = 0x34,
    BumpEnvironmentLuminance = 0x35,
    Reflection = 0x36,
    TimesWithTexture = 0x37,
    Dot2 = 0x38,
    Normalize = 0x39,
    Divide = 0x3A,
    DivideBySquareRoot = 0x3B,
    LightingFinal = 0x3C,
    FenceT = 0x3D,
    FenceB = 0x3E,
    Reserved3F = 0x3F,
    Break = 0x40,
    Call = 0x41,
    If = 0x42,
    Loop = 0x43,
    Repeat = 0x44,
    Return = 0x45
}

public enum RsxVertexVectorOpcode : byte
{
    Nop = 0x00,
    Move = 0x01,
    Multiply = 0x02,
    Add = 0x03,
    MultiplyAdd = 0x04,
    Dot3 = 0x05,
    DotHomogeneous = 0x06,
    Dot4 = 0x07,
    Distance = 0x08,
    Minimum = 0x09,
    Maximum = 0x0A,
    SetLessThan = 0x0B,
    SetGreaterThanOrEqual = 0x0C,
    AddressLoad = 0x0D,
    Fraction = 0x0E,
    Floor = 0x0F,
    SetEqual = 0x10,
    SetFalse = 0x11,
    SetGreaterThan = 0x12,
    SetLessThanOrEqual = 0x13,
    SetNotEqual = 0x14,
    SetTrue = 0x15,
    SetSign = 0x16,
    Unknown17 = 0x17,
    Unknown18 = 0x18,
    TextureLod = 0x19
}

public enum RsxVertexScalarOpcode : byte
{
    Nop = 0x00,
    Move = 0x01,
    Reciprocal = 0x02,
    ReciprocalClamped = 0x03,
    ReciprocalSquareRoot = 0x04,
    ExponentBase2LowPrecision = 0x05,
    LogarithmBase2LowPrecision = 0x06,
    Lighting = 0x07,
    BranchAddress = 0x08,
    BranchImmediate = 0x09,
    CallImmediate = 0x0A,
    CallImmediateConditional = 0x0B,
    Return = 0x0C,
    LogarithmBase2 = 0x0D,
    ExponentBase2 = 0x0E,
    Sine = 0x0F,
    Cosine = 0x10,
    BranchBoolean = 0x11,
    CallBoolean = 0x12,
    Push = 0x13,
    Pop = 0x14
}

public enum RsxFragmentRegisterType : byte
{
    Temporary = 0,
    Input = 1,
    InlineConstant = 2,
    Unknown3 = 3
}

public enum RsxVertexRegisterType : byte
{
    Unknown0 = 0,
    Temporary = 1,
    Input = 2,
    Constant = 3
}

/// <summary>
/// Two-bit source and condition swizzle component shared by NV40 vertex and
/// fragment instructions.
/// </summary>
public enum RsxSwizzleComponent : byte
{
    X = 0,
    Y = 1,
    Z = 2,
    W = 3
}

/// <summary>
/// Three-bit LT/EQ/GT condition selector shared by NV40 vertex and fragment
/// instructions.
/// </summary>
public enum RsxConditionTest : byte
{
    False = 0,
    LessThan = 1,
    Equal = 2,
    LessThanOrEqual = 3,
    GreaterThan = 4,
    NotEqual = 5,
    GreaterThanOrEqual = 6,
    True = 7
}

/// <summary>NV40 fragment result scale encoded in source word 1.</summary>
public enum RsxFragmentResultScale : byte
{
    None = 0,
    MultiplyBy2 = 1,
    MultiplyBy4 = 2,
    MultiplyBy8 = 3,
    Reserved4 = 4,
    DivideBy2 = 5,
    DivideBy4 = 6,
    DivideBy8 = 7
}

/// <summary>
/// RSX fragment source/destination precision modifier. Destination words
/// encode the first four values; source word 1 uses the complete three-bit
/// domain.
/// </summary>
public enum RsxFragmentPrecision : byte
{
    Real = 0,
    Half = 1,
    Fixed12 = 2,
    Fixed9 = 3,
    Saturate = 4,
    Unknown5 = 5,
    Reserved6 = 6,
    Reserved7 = 7
}

/// <summary>RSX rasterizer input selected by a fragment instruction.</summary>
public enum RsxFragmentInputAttribute : byte
{
    WindowPosition = 0,
    Color0 = 1,
    Color1 = 2,
    Fog = 3,
    TextureCoordinate0 = 4,
    TextureCoordinate1 = 5,
    TextureCoordinate2 = 6,
    TextureCoordinate3 = 7,
    TextureCoordinate4 = 8,
    TextureCoordinate5 = 9,
    TextureCoordinate6 = 10,
    TextureCoordinate7 = 11,
    TextureCoordinate8 = 12,
    TextureCoordinate9 = 13,
    SignedSideArea = 14,
    Unknown15 = 15
}

/// <summary>RSX vertex-array attribute selected by a vertex instruction.</summary>
public enum RsxVertexInputAttribute : byte
{
    Position = 0,
    Weight = 1,
    Normal = 2,
    Color0 = 3,
    Color1 = 4,
    Fog = 5,
    PointSize = 6,
    Unknown7 = 7,
    TextureCoordinate0 = 8,
    TextureCoordinate1 = 9,
    TextureCoordinate2 = 10,
    TextureCoordinate3 = 11,
    TextureCoordinate4 = 12,
    TextureCoordinate5 = 13,
    TextureCoordinate6 = 14,
    TextureCoordinate7 = 15
}

/// <summary>
/// RSX vertex result register semantics. Results 5 and 6 multiplex fog,
/// point-size, user-clip, and texture-coordinate components in hardware.
/// </summary>
public enum RsxVertexResult : byte
{
    Position = 0,
    FrontColor0 = 1,
    FrontColor1 = 2,
    BackColor0 = 3,
    BackColor1 = 4,
    FogAndUserClip0To2 = 5,
    PointSizeUserClip3To5AndTextureCoordinate9 = 6,
    TextureCoordinate0 = 7,
    TextureCoordinate1 = 8,
    TextureCoordinate2 = 9,
    TextureCoordinate3 = 10,
    TextureCoordinate4 = 11,
    TextureCoordinate5 = 12,
    TextureCoordinate6 = 13,
    TextureCoordinate7 = 14,
    TextureCoordinate8 = 15,
    None = 31
}

[Flags]
public enum RsxFragmentProgramControlFlags : uint
{
    None = 0,
    DepthExportMask = 0x0000_000e,
    Exports32Bit = 0x0000_0040,
    UsesKill = 0x0000_0080,
    UnknownAlwaysSet0400 = 0x0000_0400,
    TexturePerspectiveCorrection = 0x0000_8000
}

[Flags]
public enum RsxFragmentWriteMask : byte
{
    None = 0,
    X = 0x1,
    Y = 0x2,
    Z = 0x4,
    W = 0x8,
    All = X | Y | Z | W
}

[Flags]
public enum RsxVertexWriteMask : byte
{
    None = 0,
    W = 0x1,
    Z = 0x2,
    Y = 0x4,
    X = 0x8,
    All = X | Y | Z | W
}

[Flags]
public enum RsxSourceSlotMask : byte
{
    None = 0,
    Source0 = 0x1,
    Source1 = 0x2,
    Source2 = 0x4
}

/// <summary>
/// Recovered instruction-shape metadata shared by dependency routing,
/// translation, and backend lowering. Unknown or hardware-unproven opcodes
/// conservatively retain all encoded source dependencies.
/// </summary>
public static class RsxShaderInstructionSet
{
    public static int FragmentOperandCount(byte opcode) =>
        FragmentOperandCount((RsxFragmentOpcode)opcode);

    public static int FragmentOperandCount(RsxFragmentOpcode opcode) =>
        opcode switch
        {
            RsxFragmentOpcode.Nop or
            RsxFragmentOpcode.Kill or
            RsxFragmentOpcode.SetTrue or
            RsxFragmentOpcode.SetFalse or
            RsxFragmentOpcode.Reserved30 or
            RsxFragmentOpcode.Reserved32 or
            RsxFragmentOpcode.FenceT or
            RsxFragmentOpcode.FenceB or
            RsxFragmentOpcode.Reserved3F or
            RsxFragmentOpcode.Break or
            RsxFragmentOpcode.Call or
            RsxFragmentOpcode.If or
            RsxFragmentOpcode.Loop or
            RsxFragmentOpcode.Repeat or
            RsxFragmentOpcode.Return => 0,
            RsxFragmentOpcode.Move or
            RsxFragmentOpcode.Fraction or
            RsxFragmentOpcode.Floor or
            RsxFragmentOpcode.PackFourSignedBytes or
            RsxFragmentOpcode.UnpackFourSignedBytes or
            RsxFragmentOpcode.DerivativeX or
            RsxFragmentOpcode.DerivativeY or
            RsxFragmentOpcode.Texture or
            RsxFragmentOpcode.TextureProjective or
            RsxFragmentOpcode.Reciprocal or
            RsxFragmentOpcode.ReciprocalSquareRoot or
            RsxFragmentOpcode.ExponentBase2 or
            RsxFragmentOpcode.LogarithmBase2 or
            RsxFragmentOpcode.Lighting or
            RsxFragmentOpcode.Cosine or
            RsxFragmentOpcode.Sine or
            RsxFragmentOpcode.PackTwoHalfFloats or
            RsxFragmentOpcode.UnpackTwoHalfFloats or
            RsxFragmentOpcode.PackBytes or
            RsxFragmentOpcode.UnpackBytes or
            RsxFragmentOpcode.Pack16 or
            RsxFragmentOpcode.Unpack16 or
            RsxFragmentOpcode.PackGamma or
            RsxFragmentOpcode.UnpackGamma or
            RsxFragmentOpcode.Normalize or
            RsxFragmentOpcode.LightingFinal => 1,
            RsxFragmentOpcode.Multiply or
            RsxFragmentOpcode.Add or
            RsxFragmentOpcode.Dot3 or
            RsxFragmentOpcode.Dot4 or
            RsxFragmentOpcode.Distance or
            RsxFragmentOpcode.Minimum or
            RsxFragmentOpcode.Maximum or
            RsxFragmentOpcode.SetLessThan or
            RsxFragmentOpcode.SetGreaterThanOrEqual or
            RsxFragmentOpcode.SetLessThanOrEqual or
            RsxFragmentOpcode.SetGreaterThan or
            RsxFragmentOpcode.SetNotEqual or
            RsxFragmentOpcode.SetEqual or
            RsxFragmentOpcode.Power or
            RsxFragmentOpcode.TextureLod or
            RsxFragmentOpcode.TextureBias or
            RsxFragmentOpcode.Reflection or
            RsxFragmentOpcode.Dot2 or
            RsxFragmentOpcode.Divide or
            RsxFragmentOpcode.DivideBySquareRoot => 2,
            RsxFragmentOpcode.MultiplyAdd or
            RsxFragmentOpcode.LinearInterpolate or
            RsxFragmentOpcode.BumpEnvironmentMap or
            RsxFragmentOpcode.TextureBumpEnvironmentMap or
            RsxFragmentOpcode.TextureProjectiveBumpEnvironmentMap or
            RsxFragmentOpcode.Dot2Add or
            RsxFragmentOpcode.TextureGradients => 3,
            _ => 3
        };

    public static bool IsFragmentTexture(RsxFragmentOpcode opcode) =>
        opcode is RsxFragmentOpcode.Texture or
            RsxFragmentOpcode.TextureProjective or
            RsxFragmentOpcode.TextureGradients or
            RsxFragmentOpcode.TextureLod or
            RsxFragmentOpcode.TextureBias or
            RsxFragmentOpcode.TextureBumpEnvironmentMap or
            RsxFragmentOpcode.TextureProjectiveBumpEnvironmentMap;

    public static bool IsFragmentTexture(byte opcode) =>
        IsFragmentTexture((RsxFragmentOpcode)opcode);

    public static RsxSourceSlotMask VertexSourceMask(byte opcode) =>
        VertexSourceMask((RsxVertexVectorOpcode)opcode);

    public static RsxSourceSlotMask VertexSourceMask(
        RsxVertexVectorOpcode opcode) => opcode switch
        {
            RsxVertexVectorOpcode.Nop or
            RsxVertexVectorOpcode.SetFalse or
            RsxVertexVectorOpcode.SetTrue => RsxSourceSlotMask.None,
            RsxVertexVectorOpcode.Move or
            RsxVertexVectorOpcode.AddressLoad or
            RsxVertexVectorOpcode.Fraction or
            RsxVertexVectorOpcode.Floor or
            RsxVertexVectorOpcode.SetSign or
            RsxVertexVectorOpcode.TextureLod => RsxSourceSlotMask.Source0,
            RsxVertexVectorOpcode.Add =>
                RsxSourceSlotMask.Source0 | RsxSourceSlotMask.Source2,
            RsxVertexVectorOpcode.MultiplyAdd =>
                RsxSourceSlotMask.Source0 |
                RsxSourceSlotMask.Source1 |
                RsxSourceSlotMask.Source2,
            RsxVertexVectorOpcode.Unknown17 or
            RsxVertexVectorOpcode.Unknown18 => RsxSourceSlotMask.None,
            _ => RsxSourceSlotMask.Source0 | RsxSourceSlotMask.Source1
        };

    public static int VertexScalarOperandCount(byte opcode) =>
        VertexScalarOperandCount((RsxVertexScalarOpcode)opcode);

    public static int VertexScalarOperandCount(
        RsxVertexScalarOpcode opcode) => (byte)opcode switch
        {
            0x00 or >= 0x08 and <= 0x0C or >= 0x11 and <= 0x14 => 0,
            _ => 1
        };

    public static bool IsVertexScalarControlFlow(
        RsxVertexScalarOpcode opcode) => opcode is
            RsxVertexScalarOpcode.BranchAddress or
            RsxVertexScalarOpcode.BranchImmediate or
            RsxVertexScalarOpcode.CallImmediate or
            RsxVertexScalarOpcode.CallImmediateConditional or
            RsxVertexScalarOpcode.Return or
            RsxVertexScalarOpcode.BranchBoolean or
            RsxVertexScalarOpcode.CallBoolean or
            RsxVertexScalarOpcode.Push or
            RsxVertexScalarOpcode.Pop;

    public static bool IsVertexScalarControlFlow(byte opcode) =>
        IsVertexScalarControlFlow((RsxVertexScalarOpcode)opcode);

    public static bool IsFragmentControlFlow(RsxFragmentOpcode opcode) =>
        opcode is >= RsxFragmentOpcode.Break and <= RsxFragmentOpcode.Return;

    public static bool IsFragmentControlFlow(byte opcode) =>
        IsFragmentControlFlow((RsxFragmentOpcode)opcode);
}
