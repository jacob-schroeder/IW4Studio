using System.Collections.Immutable;

using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Classifies the narrow instruction/dataflow surface for which the OpenGL
/// generic-preview fallback must linearize a sampled color input. This is a
/// backend compatibility policy, not a backend-neutral RSX semantic.
/// </summary>
internal static class MapRenderColorInputIrCompatibilityClassifier
{
    private const byte RgbComponentMask = 0x07;

    internal static bool RequiresLinearization(
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
            instruction.Branch ||
            IsConditionSensitive(instruction) ||
            instruction.NoDest ||
            !WritesRgb(instruction.WriteMask) ||
            instruction.Saturate ||
            instruction.Scale is not 0 and not 4)
        {
            return false;
        }

        bool cube = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Cube);
        bool shadow = HasFeature(
            samplerFeatures,
            RsxFragmentSamplerFeatures.Shadow);
        return instruction.Opcode switch
        {
            // These are the only lowerer spellings matched by the independent
            // legacy `texture(?:Lod)?` assignment regex. Projective samples
            // and scalar-wrapped shadow samples intentionally remain hidden.
            0x17 => !shadow || cube,
            0x2f or 0x31 => !shadow,
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
        if (instruction.Branch ||
            IsConditionSensitive(instruction) ||
            instruction.NoDest ||
            instruction.WriteMask == 0 ||
            !TryReadExpressionFacts(
                instruction,
                samplerFeatures,
                out FragmentExpressionFacts expression))
        {
            facts = default;
            return false;
        }

        bool scaled = instruction.Scale is 1 or 2 or 3 or 5 or 6 or 7;
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
        switch (instruction.Opcode)
        {
            case 0x01:
            case 0x10:
            case 0x11:
            case 0x15:
            case 0x16:
            case 0x1a:
            case 0x1b:
            case 0x1c:
            case 0x1d:
            case 0x22:
            case 0x23:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case 0x02:
            case 0x3b:
                occurrences = SourceOccurrencePattern.Source01;
                containsAsterisk = true;
                break;
            case 0x03:
            case 0x05:
            case 0x06:
            case 0x08:
            case 0x09:
            case 0x0a:
            case 0x0b:
            case 0x0c:
            case 0x0d:
            case 0x0e:
            case 0x0f:
            case 0x38:
            case 0x3a:
                occurrences = SourceOccurrencePattern.Source01;
                break;
            case 0x04:
                // Intentional legacy text quirk: the regex sees the MAD
                // asterisk and then counts every register token on the line,
                // including the additive source2 operand.
                occurrences = SourceOccurrencePattern.Source012;
                containsAsterisk = true;
                break;
            case 0x17:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case 0x18 when !cube && !volume:
                occurrences = SourceOccurrencePattern.Source0;
                break;
            case 0x20:
            case 0x21:
                occurrences = SourceOccurrencePattern.None;
                break;
            case 0x2f:
            case 0x31:
                if (shadow)
                {
                    facts = default;
                    return false;
                }
                occurrences = SourceOccurrencePattern.Source01;
                break;
            case 0x39:
                // The lowerer spells source0 twice in normalize(vec.xyz),
                // vec.w. The legacy regex counts both only when an outer scale
                // contributes an actual '*' character to the assignment.
                occurrences = SourceOccurrencePattern.Source00;
                break;
            default:
                facts = default;
                return false;
        }

        // Dot products deliberately do not set ContainsAsterisk: the retained
        // classifier inspects generated text, where `dot(...)` has no '*'.
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
        operand.RegisterType == 0 &&
        operand.Fp16 == target.Fp16 &&
        operand.RegisterIndex == target.Index &&
        (operand.SwizzleX != 3 ||
         operand.SwizzleY != 3 ||
         operand.SwizzleZ != 3 ||
         operand.SwizzleW != 3);

    private static bool IsConditionSensitive(
        RsxFragmentInstruction instruction) =>
        instruction.CondWriteEnabled ||
        instruction.ConditionTest != RsxFragmentConditionTest.True ||
        instruction.ConditionWriteRegister1 ||
        instruction.ConditionReadRegister1;

    private static bool WritesRgb(int componentMask) =>
        (componentMask & RgbComponentMask) != 0;

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
