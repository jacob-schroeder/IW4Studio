using System.Collections.Immutable;

using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Carries the color-input transfer behavior visible in the immutable RSX
/// fragment IR into the backend-neutral generic EditorPreview fallback.
/// Unknown or ambiguous dataflow deliberately remains unmodified.
/// </summary>
internal static class MapRenderGenericMaterialColorInputContract
{
    internal static int ResolveLinearizationMask(
        ShaderExecutionContract execution,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        int maximumLayerCount)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(colorLayers);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLayerCount);

        if (!execution.ProgramIrReady || execution.FragmentProgramIr is null)
            return 0;

        int mask = 0;
        int layerCount = Math.Min(
            Math.Min(colorLayers.Count, maximumLayerCount),
            sizeof(int) * 8 - 1);
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            if (RequiresLinearization(
                    execution.FragmentProgramIr,
                    colorLayers[layerIndex].Identity.SamplerDest))
            {
                mask |= 1 << layerIndex;
            }
        }

        return mask;
    }

    private const RsxFragmentWriteMask RgbComponentMask =
        RsxFragmentWriteMask.X |
        RsxFragmentWriteMask.Y |
        RsxFragmentWriteMask.Z;

    private static bool RequiresLinearization(
        RsxFragmentProgramIr program,
        int samplerDestination)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (samplerDestination < 0)
            return false;

        bool hasVisibleRgbSample = false;
        ImmutableArray<RsxFragmentInstruction> instructions =
            program.Instructions;
        for (int ordinal = 0; ordinal < instructions.Length; ordinal++)
        {
            RsxFragmentInstruction instruction = instructions[ordinal];
            RsxFragmentSamplerFeatures samplerFeatures =
                program.SamplerFeatureProfile.FeaturesFor(
                    instruction.TextureUnit);
            if (!IsCompatibilityVisibleSample(
                    instruction,
                    samplerFeatures,
                    samplerDestination))
            {
                continue;
            }

            hasVisibleRgbSample = true;
            var target = new FragmentRegister(
                instruction.DestFp16,
                instruction.DestRegister);
            if (!TargetHasRepeatedMultiplicativeRgbTextBeforeOverwrite(
                    instructions,
                    ordinal,
                    target,
                    program.SamplerFeatureProfile))
            {
                return false;
            }
        }

        return hasVisibleRgbSample;
    }

    private static bool IsCompatibilityVisibleSample(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatures samplerFeatures,
        int samplerDestination)
    {
        if (instruction.TextureUnit != samplerDestination ||
            instruction.IsControlFlow ||
            IsConditionSensitive(instruction) ||
            instruction.NoDest ||
            !WritesRgb(instruction.WriteMask) ||
            instruction.Saturate ||
            instruction.Scale is not RsxFragmentResultScale.None and
                not RsxFragmentResultScale.Reserved4)
        {
            return false;
        }

        bool cube = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Cube);
        bool shadow = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Shadow);
        return instruction.OpcodeType switch
        {
            RsxFragmentOpcode.Texture => !shadow || cube,
            RsxFragmentOpcode.TextureLod or
                RsxFragmentOpcode.TextureBias => !shadow,
            _ => false
        };
    }

    private static bool
        TargetHasRepeatedMultiplicativeRgbTextBeforeOverwrite(
            ImmutableArray<RsxFragmentInstruction> instructions,
            int sampleOrdinal,
            FragmentRegister target,
            RsxFragmentSamplerFeatureProfile samplerProfile)
    {
        int accumulatedUses = 0;
        for (int ordinal = sampleOrdinal + 1;
             ordinal < instructions.Length;
             ordinal++)
        {
            RsxFragmentInstruction instruction = instructions[ordinal];
            RsxFragmentSamplerFeatures features =
                samplerProfile.FeaturesFor(instruction.TextureUnit);
            if (!TryReadDirectAssignmentFacts(
                    instruction,
                    features,
                    out FragmentAssignmentFacts assignment) ||
                !WritesRgb(instruction.WriteMask))
            {
                continue;
            }

            int usesOnInstruction = assignment.ContainsAsterisk
                ? CountLegacyRegexRgbRegisterOccurrences(
                    instruction,
                    assignment.SourceOccurrences,
                    target)
                : 0;
            bool overwritesTarget =
                instruction.DestFp16 == target.Fp16 &&
                instruction.DestRegister == target.Index;
            if (overwritesTarget)
                return usesOnInstruction >= 2;

            accumulatedUses += usesOnInstruction;
            if (accumulatedUses >= 2)
                return true;
        }

        return false;
    }

    private static bool TryReadDirectAssignmentFacts(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatures samplerFeatures,
        out FragmentAssignmentFacts facts)
    {
        if (instruction.IsControlFlow ||
            IsConditionSensitive(instruction) ||
            instruction.NoDest ||
            instruction.WriteMask == RsxFragmentWriteMask.None ||
            !TryReadExpressionFacts(
                instruction,
                samplerFeatures,
                out FragmentExpressionFacts expression))
        {
            facts = default;
            return false;
        }

        bool scaled = instruction.Scale is
            RsxFragmentResultScale.MultiplyBy2 or
            RsxFragmentResultScale.MultiplyBy4 or
            RsxFragmentResultScale.MultiplyBy8 or
            RsxFragmentResultScale.DivideBy2 or
            RsxFragmentResultScale.DivideBy4 or
            RsxFragmentResultScale.DivideBy8;
        facts = new FragmentAssignmentFacts(
            expression.SourceOccurrences,
            expression.ContainsAsterisk || scaled);
        return true;
    }

    private static bool TryReadExpressionFacts(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatures samplerFeatures,
        out FragmentExpressionFacts facts)
    {
        bool cube = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Cube);
        bool shadow = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Shadow);
        bool volume = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Volume);

        SourceOccurrencePattern occurrences;
        bool containsAsterisk = false;
        switch (instruction.OpcodeType)
        {
            case RsxFragmentOpcode.Move:
            case RsxFragmentOpcode.Fraction:
            case RsxFragmentOpcode.Floor:
            case RsxFragmentOpcode.DerivativeX:
            case RsxFragmentOpcode.DerivativeY:
            case RsxFragmentOpcode.Reciprocal:
            case RsxFragmentOpcode.ReciprocalSquareRoot:
            case RsxFragmentOpcode.ExponentBase2:
            case RsxFragmentOpcode.LogarithmBase2:
            case RsxFragmentOpcode.Cosine:
            case RsxFragmentOpcode.Sine:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case RsxFragmentOpcode.Multiply:
            case RsxFragmentOpcode.DivideBySquareRoot:
                occurrences = SourceOccurrencePattern.Source01;
                containsAsterisk = true;
                break;
            case RsxFragmentOpcode.Add:
            case RsxFragmentOpcode.Dot3:
            case RsxFragmentOpcode.Dot4:
            case RsxFragmentOpcode.Minimum:
            case RsxFragmentOpcode.Maximum:
            case RsxFragmentOpcode.SetLessThan:
            case RsxFragmentOpcode.SetGreaterThanOrEqual:
            case RsxFragmentOpcode.SetLessThanOrEqual:
            case RsxFragmentOpcode.SetGreaterThan:
            case RsxFragmentOpcode.SetNotEqual:
            case RsxFragmentOpcode.SetEqual:
            case RsxFragmentOpcode.Dot2:
            case RsxFragmentOpcode.Divide:
                occurrences = SourceOccurrencePattern.Source01;
                break;
            case RsxFragmentOpcode.MultiplyAdd:
                occurrences = SourceOccurrencePattern.Source012;
                containsAsterisk = true;
                break;
            case RsxFragmentOpcode.Texture:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case RsxFragmentOpcode.TextureProjective
                when !cube && !volume:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case RsxFragmentOpcode.SetTrue:
            case RsxFragmentOpcode.SetFalse:
                occurrences = SourceOccurrencePattern.None;
                break;
            case RsxFragmentOpcode.TextureLod:
            case RsxFragmentOpcode.TextureBias:
                if (shadow)
                {
                    facts = default;
                    return false;
                }
                occurrences = SourceOccurrencePattern.Source01;
                break;
            case RsxFragmentOpcode.Normalize:
                occurrences = SourceOccurrencePattern.Source00;
                break;
            default:
                facts = default;
                return false;
        }

        facts = new FragmentExpressionFacts(
            occurrences,
            containsAsterisk);
        return true;
    }

    private static int CountLegacyRegexRgbRegisterOccurrences(
        RsxFragmentInstruction instruction,
        SourceOccurrencePattern occurrences,
        FragmentRegister target) => occurrences switch
    {
        SourceOccurrencePattern.None => 0,
        SourceOccurrencePattern.Source0 =>
            IsLegacyRegexRgbRegisterToken(
                instruction.Source0Operand,
                target) ? 1 : 0,
        SourceOccurrencePattern.Source01 =>
            (IsLegacyRegexRgbRegisterToken(
                instruction.Source0Operand,
                target) ? 1 : 0) +
            (IsLegacyRegexRgbRegisterToken(
                instruction.Source1Operand,
                target) ? 1 : 0),
        SourceOccurrencePattern.Source012 =>
            (IsLegacyRegexRgbRegisterToken(
                instruction.Source0Operand,
                target) ? 1 : 0) +
            (IsLegacyRegexRgbRegisterToken(
                instruction.Source1Operand,
                target) ? 1 : 0) +
            (IsLegacyRegexRgbRegisterToken(
                instruction.Source2Operand,
                target) ? 1 : 0),
        SourceOccurrencePattern.Source00 =>
            IsLegacyRegexRgbRegisterToken(
                instruction.Source0Operand,
                target) ? 2 : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(occurrences))
    };

    private static bool IsLegacyRegexRgbRegisterToken(
        RsxFragmentOperand operand,
        FragmentRegister target) =>
        operand.RegisterKind == RsxFragmentRegisterType.Temporary &&
        operand.Fp16 == target.Fp16 &&
        operand.RegisterIndex == target.Index &&
        (operand.SwizzleX != RsxSwizzleComponent.W ||
         operand.SwizzleY != RsxSwizzleComponent.W ||
         operand.SwizzleZ != RsxSwizzleComponent.W ||
         operand.SwizzleW != RsxSwizzleComponent.W);

    private static bool IsConditionSensitive(
        RsxFragmentInstruction instruction) =>
        instruction.CondWriteEnabled ||
        instruction.ConditionTest != RsxConditionTest.True ||
        instruction.ConditionWriteRegister1 ||
        instruction.ConditionReadRegister1;

    private static bool WritesRgb(RsxFragmentWriteMask componentMask) =>
        (componentMask & RgbComponentMask) != RsxFragmentWriteMask.None;

    private static bool HasFeature(
        RsxFragmentSamplerFeatures features,
        RsxFragmentSamplerFeatures feature) =>
        (features & feature) != 0;

    private readonly record struct FragmentRegister(bool Fp16, int Index);

    private enum SourceOccurrencePattern
    {
        None = 0,
        Source0,
        Source01,
        Source012,
        Source00
    }

    private readonly record struct FragmentExpressionFacts(
        SourceOccurrencePattern SourceOccurrences,
        bool ContainsAsterisk);

    private readonly record struct FragmentAssignmentFacts(
        SourceOccurrencePattern SourceOccurrences,
        bool ContainsAsterisk);
}
