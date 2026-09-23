using IW4.Render.Shaders;
using IW4.Render.Techniques;

namespace IW4.Render.Execution;

/// <summary>
/// Backend-neutral proof facts used when an opaque normal-camera color replay
/// replaces the standard transform-only depth owner.
/// </summary>
internal static class NormalCameraDepthPrepassElisionCertification
{
    /// <summary>
    /// Structural, not algebraic, equivalence for translated clip position.
    /// The two shaders can use different temporary registers and constant
    /// destinations, but every backwards-sliced operation, source modifier,
    /// bound input route, and constant value/source must still be identical.
    /// Any construct a backend could evaluate conditionally or by indexed
    /// indirection is deliberately outside this certificate.
    /// </summary>
    internal static bool HasEquivalentTranslatedClipPosition(
        bool colorRendererProgramReady,
        RsxVertexProgramIr? colorProgram,
        IReadOnlyList<ShaderVertexInputBinding> colorInputs,
        TranslatedProgramVertexConstantBindingPlan colorConstants,
        bool depthRendererProgramReady,
        RsxVertexProgramIr? depthProgram,
        IReadOnlyList<ShaderVertexInputBinding> depthInputs,
        TranslatedProgramVertexConstantBindingPlan depthConstants)
    {
        ArgumentNullException.ThrowIfNull(colorInputs);
        ArgumentNullException.ThrowIfNull(colorConstants);
        ArgumentNullException.ThrowIfNull(depthInputs);
        ArgumentNullException.ThrowIfNull(depthConstants);
        if (!colorRendererProgramReady ||
            !depthRendererProgramReady ||
            colorProgram is null ||
            depthProgram is null)
        {
            return false;
        }

        return TryBuildPositionExpression(
                   colorProgram,
                   colorInputs,
                   colorConstants,
                   out string colorPosition) &&
               TryBuildPositionExpression(
                   depthProgram,
                   depthInputs,
                   depthConstants,
                   out string depthPosition) &&
               string.Equals(
                   colorPosition,
                   depthPosition,
                   StringComparison.Ordinal);
    }

    internal static bool HasOpaqueColorDepthEquivalentState(
        RenderState colorState,
        bool fragmentDepthExportEnabled,
        bool fragmentUsesKill)
    {
        RenderState effective = colorState.HasState
            ? colorState
            : RenderState.Default;
        return effective.ColorMask == RsxColorMask.Rgba &&
            !effective.AlphaTestEnabled &&
            !effective.BlendEnabled &&
            !effective.StencilEnabled &&
            effective.DepthTestEnabled &&
            effective.DepthFunc == RsxCompareFunction.LessThanOrEqual &&
            effective.PolygonMode == RsxPolygonMode.Fill &&
            effective.PolygonOffsetMode ==
                RenderPolygonOffsetMode.Disabled &&
            !fragmentDepthExportEnabled &&
            !fragmentUsesKill;
    }

    internal static bool HasMatchingStandardDepthRasterState(
        RenderState colorState,
        RenderState depthState)
    {
        RenderState effectiveColor = colorState.HasState
            ? colorState
            : RenderState.Default;
        RenderState effectiveDepth = depthState.HasState
            ? depthState
            : RenderState.Default;
        CullMode? colorCull = Cull.Resolve(effectiveColor);
        CullMode? depthCull = Cull.Resolve(effectiveDepth);
        return effectiveDepth.ColorMask == RsxColorMask.None &&
            !effectiveDepth.AlphaTestEnabled &&
            !effectiveDepth.BlendEnabled &&
            !effectiveDepth.StencilEnabled &&
            effectiveDepth.DepthTestEnabled &&
            effectiveDepth.DepthWriteEnabled &&
            effectiveDepth.DepthFunc ==
                RsxCompareFunction.LessThanOrEqual &&
            effectiveDepth.PolygonMode == RsxPolygonMode.Fill &&
            effectiveDepth.PolygonOffsetMode ==
                RenderPolygonOffsetMode.Disabled &&
            colorCull is CullMode.Front or CullMode.Back &&
            colorCull == depthCull;
    }

    private static bool TryBuildPositionExpression(
        RsxVertexProgramIr program,
        IReadOnlyList<ShaderVertexInputBinding> inputBindings,
        TranslatedProgramVertexConstantBindingPlan constantPlan,
        out string position)
    {
        position = string.Empty;
        if (!program.HasValidUpload || program.Instructions.IsEmpty)
            return false;

        string?[] inputRoutes = new string?[16];
        for (int bindingIndex = 0;
             bindingIndex < inputBindings.Count;
             bindingIndex++)
        {
            ShaderVertexInputBinding binding = inputBindings[bindingIndex];
            int destination = (byte)binding.Destination;
            if ((uint)destination >= (uint)inputRoutes.Length ||
                binding.RouteIndex < 0 ||
                binding.IsDisabledDefaultAttribute ||
                binding.ComponentCount is < 1 or > 4 ||
                inputRoutes[destination] is not null)
            {
                return false;
            }

            inputRoutes[destination] = CreateInputRouteIdentity(binding);
        }

        var constants = new Dictionary<ushort, string>();
        foreach (TranslatedProgramVertexConstantBinding binding in
                 constantPlan.Bindings)
        {
            if (!constants.TryAdd(
                    binding.Destination,
                    CreateConstantIdentity(binding)))
            {
                return false;
            }
        }

        string?[][] temporary = CreateRegisterBank();
        string?[][] output = CreateRegisterBank();
        foreach (RsxVertexInstruction instruction in program.Instructions)
        {
            if (instruction.HasControlFlow ||
                instruction.CondTestEnabled ||
                instruction.CondUpdateEnabled ||
                instruction.IndexConst)
            {
                return false;
            }

            // Both slot values are read before either slot writes, just as a
            // translated lowerer materializes them before its vector/scalar
            // assignment sequence.
            bool vectorSupported = IsSupportedVectorSlot(in instruction);
            bool scalarSupported = IsSupportedScalarSlot(in instruction);
            string?[]? vectorValue = vectorSupported
                ? TryBuildSlotValue(
                    in instruction,
                    scalar: false,
                    temporary,
                    inputRoutes,
                    constants)
                : null;
            string?[]? scalarValue = scalarSupported
                ? TryBuildSlotValue(
                    in instruction,
                    scalar: true,
                    temporary,
                    inputRoutes,
                    constants)
                : null;
            if (vectorSupported)
            {
                ApplySlotWrite(
                    in instruction,
                    scalar: false,
                    vectorValue,
                    temporary,
                    output);
            }
            else
            {
                InvalidateSlotWrite(
                    in instruction,
                    scalar: false,
                    temporary,
                    output);
            }

            if (scalarSupported)
            {
                ApplySlotWrite(
                    in instruction,
                    scalar: true,
                    scalarValue,
                    temporary,
                    output);
            }
            else
            {
                InvalidateSlotWrite(
                    in instruction,
                    scalar: true,
                    temporary,
                    output);
            }
        }

        string?[] clip = output[(byte)RsxVertexResult.Position];
        if (clip.Any(component => component is null))
            return false;

        position = string.Concat(
            "P(", clip[0], '|', clip[1], '|', clip[2], '|', clip[3], ')');
        return true;
    }

    private static bool IsSupportedVectorSlot(
        in RsxVertexInstruction instruction)
    {
        if (instruction.VectorWriteMask == RsxVertexWriteMask.None)
            return instruction.VectorOpcode == RsxVertexVectorOpcode.Nop;

        return instruction.VectorOpcode is
            RsxVertexVectorOpcode.Move or
            RsxVertexVectorOpcode.Multiply or
            RsxVertexVectorOpcode.Add or
            RsxVertexVectorOpcode.MultiplyAdd or
            RsxVertexVectorOpcode.Dot3 or
            RsxVertexVectorOpcode.DotHomogeneous or
            RsxVertexVectorOpcode.Dot4 or
            RsxVertexVectorOpcode.Distance or
            RsxVertexVectorOpcode.Minimum or
            RsxVertexVectorOpcode.Maximum or
            RsxVertexVectorOpcode.SetLessThan or
            RsxVertexVectorOpcode.SetGreaterThanOrEqual or
            RsxVertexVectorOpcode.Fraction or
            RsxVertexVectorOpcode.Floor or
            RsxVertexVectorOpcode.SetEqual or
            RsxVertexVectorOpcode.SetFalse or
            RsxVertexVectorOpcode.SetGreaterThan or
            RsxVertexVectorOpcode.SetLessThanOrEqual or
            RsxVertexVectorOpcode.SetNotEqual or
            RsxVertexVectorOpcode.SetTrue or
            RsxVertexVectorOpcode.SetSign;
    }

    private static bool IsSupportedScalarSlot(
        in RsxVertexInstruction instruction)
    {
        if (instruction.ScalarWriteMask == RsxVertexWriteMask.None)
            return instruction.ScalarOpcode == RsxVertexScalarOpcode.Nop;

        return instruction.ScalarOpcode is
            RsxVertexScalarOpcode.Move or
            RsxVertexScalarOpcode.Reciprocal or
            RsxVertexScalarOpcode.ReciprocalClamped or
            RsxVertexScalarOpcode.ReciprocalSquareRoot or
            RsxVertexScalarOpcode.LogarithmBase2 or
            RsxVertexScalarOpcode.ExponentBase2 or
            RsxVertexScalarOpcode.Sine or
            RsxVertexScalarOpcode.Cosine;
    }

    private static string?[]? TryBuildSlotValue(
        in RsxVertexInstruction instruction,
        bool scalar,
        string?[][] temporary,
        string?[] inputRoutes,
        IReadOnlyDictionary<ushort, string> constants)
    {
        RsxVertexWriteMask mask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (mask == RsxVertexWriteMask.None)
            return null;

        if (scalar)
        {
            string?[]? source = TryResolveSource(
                in instruction,
                instruction.Source2,
                sourceIndex: 2,
                temporary,
                inputRoutes,
                constants);
            if (source is null)
                return null;
            return CreateScalarValue(
                instruction.ScalarOpcode,
                instruction.Saturate,
                source);
        }

        RsxSourceSlotMask sources =
            RsxVertexInstruction.VectorSourceMask(
                instruction.VectorOpcode);
        string?[]? source0 =
            (sources & RsxSourceSlotMask.Source0) != 0
                ? TryResolveSource(
                    in instruction, instruction.Source0, 0,
                    temporary, inputRoutes, constants)
                : null;
        string?[]? source1 =
            (sources & RsxSourceSlotMask.Source1) != 0
                ? TryResolveSource(
                    in instruction, instruction.Source1, 1,
                    temporary, inputRoutes, constants)
                : null;
        string?[]? source2 =
            (sources & RsxSourceSlotMask.Source2) != 0
                ? TryResolveSource(
                    in instruction, instruction.Source2, 2,
                    temporary, inputRoutes, constants)
                : null;
        return CreateVectorValue(
            instruction.VectorOpcode,
            instruction.Saturate,
            source0,
            source1,
            source2);
    }

    private static string?[]? TryResolveSource(
        in RsxVertexInstruction instruction,
        uint source,
        int sourceIndex,
        string?[][] temporary,
        string?[] inputRoutes,
        IReadOnlyDictionary<ushort, string> constants)
    {
        string?[]? baseValue = RsxVertexInstruction.SourceRegisterKind(
            source) switch
        {
            RsxVertexRegisterType.Temporary =>
                temporary[(source >> 2) & 0x3f],
            RsxVertexRegisterType.Input => ResolveInput(
                inputRoutes,
                (byte)instruction.InputAttribute),
            RsxVertexRegisterType.Constant => ResolveConstant(
                constants,
                checked((ushort)instruction.ConstSource)),
            _ => null
        };
        if (baseValue is null)
            return null;

        var resolved = new string?[4];
        bool absolute = sourceIndex switch
        {
            0 => instruction.Source0Abs,
            1 => instruction.Source1Abs,
            _ => instruction.Source2Abs
        };
        bool negate = (source & 0x10000u) != 0;
        for (int component = 0; component < 4; component++)
        {
            int swizzle = (int)((source >> (14 - component * 2)) & 3);
            string? value = baseValue[swizzle];
            if (value is null)
                continue;
            if (absolute)
                value = "abs(" + value + ')';
            if (negate)
                value = "neg(" + value + ')';
            resolved[component] = value;
        }
        return resolved;
    }

    private static string?[]? ResolveInput(
        string?[] inputRoutes,
        int attribute)
    {
        if ((uint)attribute >= (uint)inputRoutes.Length ||
            inputRoutes[attribute] is not { } route)
        {
            return null;
        }

        return
        [
            route + ".x", route + ".y", route + ".z", route + ".w"
        ];
    }

    private static string?[]? ResolveConstant(
        IReadOnlyDictionary<ushort, string> constants,
        ushort destination)
    {
        if (!constants.TryGetValue(destination, out string? constant))
            return null;
        return
        [
            constant + ".x", constant + ".y", constant + ".z",
            constant + ".w"
        ];
    }

    private static string?[]? CreateScalarValue(
        RsxVertexScalarOpcode opcode,
        bool saturate,
        string?[]? source)
    {
        if (source is null)
            return null;
        var value = new string?[4];
        for (int component = 0; component < 4; component++)
        {
            string? operand = opcode ==
                RsxVertexScalarOpcode.ReciprocalSquareRoot
                ? source[0]
                : source[component];
            if (operand is not null)
            {
                value[component] = ApplySaturate(
                    "S" + (byte)opcode + '(' + operand + ')',
                    saturate);
            }
        }
        return value;
    }

    private static string?[]? CreateVectorValue(
        RsxVertexVectorOpcode opcode,
        bool saturate,
        string?[]? source0,
        string?[]? source1,
        string?[]? source2)
    {
        var value = new string?[4];
        for (int component = 0; component < 4; component++)
        {
            string? expression = opcode switch
            {
                RsxVertexVectorOpcode.Move or
                RsxVertexVectorOpcode.Fraction or
                RsxVertexVectorOpcode.Floor or
                RsxVertexVectorOpcode.SetSign =>
                    CreateOperation(
                        "V" + (byte)opcode,
                        Component(source0, component)),
                RsxVertexVectorOpcode.Multiply or
                RsxVertexVectorOpcode.Minimum or
                RsxVertexVectorOpcode.Maximum or
                RsxVertexVectorOpcode.SetLessThan or
                RsxVertexVectorOpcode.SetGreaterThanOrEqual or
                RsxVertexVectorOpcode.SetEqual or
                RsxVertexVectorOpcode.SetGreaterThan or
                RsxVertexVectorOpcode.SetLessThanOrEqual or
                RsxVertexVectorOpcode.SetNotEqual =>
                    CreateOperation(
                        "V" + (byte)opcode,
                        Component(source0, component),
                        Component(source1, component)),
                RsxVertexVectorOpcode.Add =>
                    CreateOperation(
                        "V" + (byte)opcode,
                        Component(source0, component),
                        Component(source2, component)),
                RsxVertexVectorOpcode.MultiplyAdd =>
                    CreateOperation(
                        "V" + (byte)opcode,
                        Component(source0, component),
                        Component(source1, component),
                        Component(source2, component)),
                RsxVertexVectorOpcode.Dot3 => CreateDotExpression(
                    opcode, source0, source1, 3),
                RsxVertexVectorOpcode.DotHomogeneous =>
                    CreateDotHomogeneousExpression(source0, source1),
                RsxVertexVectorOpcode.Dot4 => CreateDotExpression(
                    opcode, source0, source1, 4),
                RsxVertexVectorOpcode.Distance => component switch
                {
                    0 => "literal:1",
                    1 => CreateOperation(
                        "V8", Component(source0, 1),
                        Component(source1, 1)),
                    2 => CreateOperation("V8", Component(source0, 2)),
                    _ => CreateOperation("V8", Component(source1, 3))
                },
                RsxVertexVectorOpcode.SetFalse => "literal:0",
                RsxVertexVectorOpcode.SetTrue => "literal:1",
                _ => string.Empty
            };
            if (expression is not null)
                value[component] = ApplySaturate(expression, saturate);
        }
        return value;
    }

    private static string? CreateDotExpression(
        RsxVertexVectorOpcode opcode,
        string?[]? source0,
        string?[]? source1,
        int componentCount)
    {
        var operands = new string?[componentCount * 2];
        for (int component = 0; component < componentCount; component++)
        {
            operands[component * 2] = Component(source0, component);
            operands[component * 2 + 1] = Component(source1, component);
        }
        return CreateOperation("V" + (byte)opcode, operands);
    }

    private static string? CreateDotHomogeneousExpression(
        string?[]? source0,
        string?[]? source1) => CreateOperation(
        "V6", Component(source0, 0), Component(source1, 0),
        Component(source0, 1), Component(source1, 1),
        Component(source0, 2), Component(source1, 2), "literal:1",
        Component(source1, 3));

    private static string? Component(string?[]? source, int component) =>
        source is not null ? source[component] : null;

    private static string? CreateOperation(
        string operation,
        params string?[] operands)
    {
        if (operands.Any(operand => operand is null))
            return null;

        return string.Concat(
            operation,
            '(',
            string.Join(',', operands!),
            ')');
    }

    private static string ApplySaturate(string value, bool saturate) =>
        saturate ? "sat(" + value + ')' : value;

    private static void ApplySlotWrite(
        in RsxVertexInstruction instruction,
        bool scalar,
        string?[]? value,
        string?[][] temporary,
        string?[][] output)
    {
        RsxVertexWriteMask writeMask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (writeMask == RsxVertexWriteMask.None)
            return;
        bool writesResult = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesResult && instruction.Result != RsxVertexResult.None)
        {
            CopyMasked(
                output[(byte)instruction.Result], value, writeMask);
        }

        int temporaryDestination = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        if (temporaryDestination != 0x3f)
            CopyMasked(
                temporary[temporaryDestination], value, writeMask);
    }

    private static void InvalidateSlotWrite(
        in RsxVertexInstruction instruction,
        bool scalar,
        string?[][] temporary,
        string?[][] output) => ApplySlotWrite(
        in instruction,
        scalar,
        value: null,
        temporary,
        output);

    private static void CopyMasked(
        string?[] destination,
        string?[]? source,
        RsxVertexWriteMask writeMask)
    {
        for (int component = 0; component < 4; component++)
        {
            if ((writeMask & (RsxVertexWriteMask)(0x8 >> component)) !=
                RsxVertexWriteMask.None)
            {
                destination[component] = source?[component];
            }
        }
    }

    private static string?[][] CreateRegisterBank()
    {
        var bank = new string?[64][];
        for (int index = 0; index < bank.Length; index++)
            bank[index] = new string?[4];
        return bank;
    }

    private static string CreateInputRouteIdentity(
        ShaderVertexInputBinding binding) => string.Concat(
        "I(", (byte)binding.Source, ';', (byte)binding.Destination, ';',
        binding.StreamIndex, ';',
        binding.Stride, ';', binding.Offset, ';', binding.ComponentCount,
        ';', (byte)binding.RsxType, ')');

    private static string CreateConstantIdentity(
        TranslatedProgramVertexConstantBinding binding)
    {
        string staticValue = binding.StaticValue is { } value
            ? string.Concat(
                BitConverter.SingleToInt32Bits(value.X), ',',
                BitConverter.SingleToInt32Bits(value.Y), ',',
                BitConverter.SingleToInt32Bits(value.Z), ',',
                BitConverter.SingleToInt32Bits(value.W))
            : "none";
        return string.Concat(
            "C(", (byte)binding.Kind, ';', staticValue, ';',
            binding.CodeMatrixSemantic?.ToString() ?? "none", ';',
            (byte)binding.CodeMatrixTransform, ';',
            binding.CodeMatrixRow, ';', binding.DynamicCodeConstantSourceRow
            ?.ToString() ?? "none", ')');
    }
}
