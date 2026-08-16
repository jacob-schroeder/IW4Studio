using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IW4.Render.Shaders;

/// <summary>
/// Immutable, backend-neutral specialization and semantic decode of one RSX
/// fragment-program asset.
/// </summary>
public sealed class RsxFragmentProgramIr
{
    /// <summary>
    /// Decoder version included in <see cref="Identity"/>. Increment this when
    /// instruction decoding semantics change.
    /// </summary>
    public const string CurrentDecoderVersion =
        "rsx-fragment-semantic-ir/2";

    /// <summary>
    /// Version of the backend-neutral static specialization and dynamic
    /// CodePixel binding rules included in <see cref="Identity"/>.
    /// </summary>
    public const string CurrentSemanticTranslationVersion =
        "rsx-fragment-specialization/4";

    internal RsxFragmentProgramIr(
        ReadOnlySpan<byte> originalProgram,
        ReadOnlySpan<byte> effectiveProgram,
        string decoderVersion,
        string semanticTranslationVersion,
        int uploadOffset,
        int uploadSize,
        ImmutableArray<RsxFragmentInstruction> instructions,
        IReadOnlyList<StaticFragmentConstantPatch> staticPatches,
        IReadOnlyList<CodePixelConstantPatchPlan> codePixelPatchPlans,
        RsxFragmentProgramControl programControl,
        RsxFragmentSamplerFeatureProfile samplerFeatureProfile,
        ImmutableArray<RsxFragmentColorExport> colorExports)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decoderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticTranslationVersion);
        if (uploadOffset < -1)
            throw new ArgumentOutOfRangeException(nameof(uploadOffset));
        if (uploadSize < 0)
            throw new ArgumentOutOfRangeException(nameof(uploadSize));
        if (instructions.IsDefault)
        {
            throw new ArgumentException(
                "The instruction array must be initialized.",
                nameof(instructions));
        }
        if (uploadOffset < 0 && !instructions.IsEmpty)
        {
            throw new ArgumentException(
                "An invalid upload cannot contain decoded instructions.",
                nameof(instructions));
        }
        ArgumentNullException.ThrowIfNull(staticPatches);
        ArgumentNullException.ThrowIfNull(codePixelPatchPlans);
        ArgumentNullException.ThrowIfNull(samplerFeatureProfile);
        if (colorExports.IsDefault)
        {
            throw new ArgumentException(
                "The color-export array must be initialized.",
                nameof(colorExports));
        }

        DecoderVersion = decoderVersion;
        SemanticTranslationVersion = semanticTranslationVersion;
        OriginalProgramBytes = ImmutableArray.CreateRange(
            originalProgram.ToArray());
        EffectiveProgramBytes = ImmutableArray.CreateRange(
            effectiveProgram.ToArray());
        OriginalByteCount = originalProgram.Length;
        EffectiveByteCount = effectiveProgram.Length;
        OriginalSha256 = Convert.ToHexString(
            SHA256.HashData(originalProgram));
        EffectiveSha256 = Convert.ToHexString(
            SHA256.HashData(effectiveProgram));
        UploadOffset = uploadOffset;
        UploadSize = uploadSize;
        Instructions = instructions;
        StaticConstantPatches = staticPatches
            .Select(patch => patch with { })
            .ToImmutableArray();
        DirectCodeConstantBindings = codePixelPatchPlans
            .Select(RsxFragmentDirectCodeConstantBinding.FromPlan)
            .ToImmutableArray();
        ProgramControl = programControl;
        SamplerFeatureProfile = samplerFeatureProfile;
        SamplerUses = instructions
            .Where(instruction => instruction.IsTexture)
            .Select(instruction => new RsxFragmentSamplerUse(
                instruction.Index,
                instruction.TextureUnit,
                instruction.OpcodeType,
                instruction.SourceAttribute,
                instruction.DestRegister,
                instruction.DestFp16,
                instruction.WriteMask))
            .ToImmutableArray();
        ColorExports = colorExports;
        SamplerDataflow = SamplerUses
            .GroupBy(use => use.Destination)
            .OrderBy(group => group.Key)
            .Select(group => new RsxFragmentSamplerDataflow(
                group.Key,
                group.ToImmutableArray()))
            .ToImmutableArray();

        string bindingDigest = HashIdentityMaterial(
            DirectCodeConstantBindings.Select(binding =>
                binding.IdentityMaterial));
        string staticBindingDigest = HashIdentityMaterial(
            Instructions.Select(instruction => string.Create(
                CultureInfo.InvariantCulture,
                $"{instruction.Index}:{instruction.StaticPixelConstantArgumentOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "-"}")));
        Identity = string.Create(
            CultureInfo.InvariantCulture,
            $"rsx-fragment-program-ir:{decoderVersion.Length}:{decoderVersion}:" +
            $"{semanticTranslationVersion.Length}:{semanticTranslationVersion}:" +
            $"{EffectiveByteCount}:{EffectiveSha256}:" +
            $"{bindingDigest}:{staticBindingDigest}:{samplerFeatureProfile.Identity}");
    }

    public string DecoderVersion { get; }

    public string SemanticTranslationVersion { get; }

    /// <summary>
    /// Immutable snapshot of the exact authored asset bytes before static
    /// runtime-table, material, or literal specialization.
    /// </summary>
    public ImmutableArray<byte> OriginalProgramBytes { get; }

    /// <summary>
    /// Immutable snapshot decoded by this IR after runtime-table default
    /// specialization. Material/literal and CodePixel values are represented
    /// as bindings and are intentionally not written into this snapshot.
    /// </summary>
    public ImmutableArray<byte> EffectiveProgramBytes { get; }

    public int OriginalByteCount { get; }

    public int EffectiveByteCount { get; }

    public string OriginalSha256 { get; }

    public string EffectiveSha256 { get; }

    /// <summary>
    /// Stable semantic identity covering decoder/specialization versions,
    /// exact effective bytes, direct CodePixel bindings, and the frozen
    /// sampler feature profile. Original bytes remain separate provenance.
    /// </summary>
    public string Identity { get; }

    public int UploadOffset { get; }

    public int UploadSize { get; }

    public bool HasValidUpload => UploadOffset >= 0;

    public ImmutableArray<RsxFragmentInstruction> Instructions { get; }

    public ImmutableArray<StaticFragmentConstantPatch>
        StaticConstantPatches { get; }

    public ImmutableArray<RsxFragmentDirectCodeConstantBinding>
        DirectCodeConstantBindings { get; }

    public RsxFragmentProgramControl ProgramControl { get; }

    public RsxFragmentSamplerFeatureProfile SamplerFeatureProfile { get; }

    public ImmutableArray<RsxFragmentSamplerUse> SamplerUses { get; }

    public ImmutableArray<RsxFragmentColorExport> ColorExports { get; }

    public ImmutableArray<RsxFragmentSamplerDataflow> SamplerDataflow { get; }

    public bool DepthExportEnabled => ProgramControl.DepthExportEnabled;

    public string? DepthExportSource => DepthExportEnabled ? "R1.z" : null;

    private static string HashIdentityMaterial(IEnumerable<string> values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (string value in values)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, utf8.Length);
            hash.AppendData(length);
            hash.AppendData(utf8);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>
/// Exact raw fragment program-control descriptor and the RSX control word
/// emitted from it by the PS3 path.
/// </summary>
public readonly record struct RsxFragmentProgramControl(
    bool IsValid,
    uint DescriptorOffset,
    byte RegisterCount,
    byte ExportPrecisionRaw,
    byte DepthExportRaw,
    byte ControlFlagsRaw,
    uint EmittedControl)
{
    public RsxFragmentProgramControlFlags EmittedFlags =>
        (RsxFragmentProgramControlFlags)EmittedControl;

    public byte UsedTemporaryRegisterCount =>
        checked((byte)(EmittedControl >> 24));

    public bool UsesFp32ColorExports =>
        IsValid && HasFlag(RsxFragmentProgramControlFlags.Exports32Bit);

    public bool UsesKill =>
        IsValid && HasFlag(RsxFragmentProgramControlFlags.UsesKill);

    public bool TexturePerspectiveCorrectionEnabled =>
        IsValid && HasFlag(
            RsxFragmentProgramControlFlags.TexturePerspectiveCorrection);

    public string ExportPrecision => !IsValid
        ? string.Empty
        : UsesFp32ColorExports ? "Fp32" : "Fp16";

    public bool DepthExportEnabled =>
        IsValid &&
        (EmittedFlags & RsxFragmentProgramControlFlags.DepthExportMask) !=
            RsxFragmentProgramControlFlags.None;

    private bool HasFlag(RsxFragmentProgramControlFlags flag) =>
        (EmittedFlags & flag) == flag;
}

/// <summary>
/// Immutable record of one intrinsic texture instruction. Sampler shape is an
/// external feature and remains in <see cref="RsxFragmentSamplerFeatureProfile"/>.
/// </summary>
public readonly record struct RsxFragmentSamplerUse(
    int InstructionIndex,
    int Destination,
    RsxFragmentOpcode Opcode,
    RsxFragmentInputAttribute SourceAttribute,
    int DestinationRegister,
    bool DestinationFp16,
    RsxFragmentWriteMask WrittenComponentMask)
{
    public bool IsProjected => Opcode is
        RsxFragmentOpcode.TextureProjective or
        RsxFragmentOpcode.TextureProjectiveBumpEnvironmentMap;

    public bool HasExplicitLod => Opcode == RsxFragmentOpcode.TextureLod;

    public bool HasBias => Opcode == RsxFragmentOpcode.TextureBias;
}

[Flags]
public enum RsxFragmentSamplerFeatures
{
    None = 0,
    Cube = 1 << 0,
    Shadow = 1 << 1,
    Volume = 1 << 2
}

public readonly record struct RsxFragmentSamplerFeature
{
    public RsxFragmentSamplerFeature(
        int destination,
        RsxFragmentSamplerFeatures features)
    {
        Destination = destination;
        Features = features;
    }

    public int Destination { get; }

    public RsxFragmentSamplerFeatures Features { get; }
}

/// <summary>
/// Frozen external sampler classification supplied to semantic translation.
/// Multiple flags are retained so ambiguous authored classifications remain
/// diagnosable and fail closed in the existing lowerer.
/// </summary>
public sealed class RsxFragmentSamplerFeatureProfile
{
    public RsxFragmentSamplerFeatureProfile(
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        IReadOnlySet<int> volumeSamplerDestinations)
    {
        ArgumentNullException.ThrowIfNull(cubeSamplerDestinations);
        ArgumentNullException.ThrowIfNull(shadowSamplerDestinations);
        ArgumentNullException.ThrowIfNull(volumeSamplerDestinations);

        var features = new SortedDictionary<int, RsxFragmentSamplerFeatures>();
        AddFeatures(
            features,
            cubeSamplerDestinations,
            RsxFragmentSamplerFeatures.Cube);
        AddFeatures(
            features,
            shadowSamplerDestinations,
            RsxFragmentSamplerFeatures.Shadow);
        AddFeatures(
            features,
            volumeSamplerDestinations,
            RsxFragmentSamplerFeatures.Volume);
        Entries = features
            .Select(pair => new RsxFragmentSamplerFeature(
                pair.Key,
                pair.Value))
            .ToImmutableArray();
        Identity = BuildIdentity(Entries);
    }

    public ImmutableArray<RsxFragmentSamplerFeature> Entries { get; }

    public string Identity { get; }

    public RsxFragmentSamplerFeatures FeaturesFor(int destination)
    {
        int low = 0;
        int high = Entries.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            RsxFragmentSamplerFeature candidate = Entries[middle];
            if (candidate.Destination == destination)
                return candidate.Features;
            if (candidate.Destination < destination)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return RsxFragmentSamplerFeatures.None;
    }

    private static void AddFeatures(
        IDictionary<int, RsxFragmentSamplerFeatures> destinationFeatures,
        IEnumerable<int> destinations,
        RsxFragmentSamplerFeatures features)
    {
        foreach (int destination in destinations)
        {
            destinationFeatures.TryGetValue(
                destination,
                out RsxFragmentSamplerFeatures existing);
            destinationFeatures[destination] = existing | features;
        }
    }

    private static string BuildIdentity(
        ImmutableArray<RsxFragmentSamplerFeature> entries)
    {
        string material = string.Join(
            ',',
            entries.Select(entry => string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.Destination}:{(int)entry.Features}")));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(material)));
    }
}

/// <summary>
/// Backend-neutral summary of the intrinsic texture instructions associated
/// with one sampler destination. More detailed color dataflow can be layered
/// on this exact instruction list without parsing generated shader text.
/// </summary>
public sealed class RsxFragmentSamplerDataflow
{
    internal RsxFragmentSamplerDataflow(
        int destination,
        ImmutableArray<RsxFragmentSamplerUse> uses)
    {
        if (uses.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A sampler dataflow summary requires at least one use.",
                nameof(uses));
        }
        if (uses.Any(use => use.Destination != destination))
        {
            throw new ArgumentException(
                "All sampler uses must have the summary destination.",
                nameof(uses));
        }

        Destination = destination;
        Uses = uses;
    }

    public int Destination { get; }

    public ImmutableArray<RsxFragmentSamplerUse> Uses { get; }
}

/// <summary>
/// Immutable direct CodePixel row/site ownership retained independently from
/// the effective static-patched byte snapshot.
/// </summary>
public sealed class RsxFragmentDirectCodeConstantBinding
{
    private RsxFragmentDirectCodeConstantBinding(
        CodePixelConstantPatchPlan plan)
    {
        ArgumentOrdinal = plan.ArgumentOrdinal;
        Destination = plan.Destination;
        ArgumentRaw = plan.ArgumentRaw;
        CodeIndex = plan.CodeIndex;
        Status = plan.Status;
        PatchSites = plan.PatchSites
            .Select(site => site with { })
            .ToImmutableArray();
        string patchSiteMaterial = string.Join(
            ',',
            PatchSites.Select(site => string.Create(
                CultureInfo.InvariantCulture,
                $"{site.RelativePatchOffset}:{site.ProgramByteOffset}:" +
                $"{site.InstructionIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}")));
        IdentityMaterial = string.Create(
            CultureInfo.InvariantCulture,
            $"{ArgumentOrdinal}:{Destination}:{unchecked((uint)ArgumentRaw):X8}:" +
            $"{CodeIndex}:{(int)Status}:{patchSiteMaterial}");
    }

    public int ArgumentOrdinal { get; }

    public ushort Destination { get; }

    public int ArgumentRaw { get; }

    public ushort CodeIndex { get; }

    public CodePixelConstantPatchStatus Status { get; }

    public bool IsDirectSourceResolved =>
        Status ==
            CodePixelConstantPatchStatus.DirectSourceResolved;

    public ImmutableArray<CodePixelConstantPatchSite> PatchSites
    {
        get;
    }

    internal string IdentityMaterial { get; }

    internal static RsxFragmentDirectCodeConstantBinding FromPlan(
        CodePixelConstantPatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new RsxFragmentDirectCodeConstantBinding(plan);
    }
}

/// <summary>
/// Exact decoded source operand. Absolute encoding differs for source slot 0,
/// so the operand retains its source-slot ordinal alongside the raw word.
/// </summary>
public readonly record struct RsxFragmentOperand(int SourceIndex, uint Raw)
{
    public RsxFragmentRegisterType RegisterKind =>
        RsxFragmentInstruction.SourceRegisterKind(Raw);

    public int RegisterIndex => (int)((Raw >> 2) & 0x3f);

    public bool Fp16 => ((Raw >> 8) & 1) != 0;

    public bool Negate => ((Raw >> 17) & 1) != 0;

    public bool Absolute => SourceIndex == 0
        ? ((Raw >> 29) & 1) != 0
        : ((Raw >> 18) & 1) != 0;

    public RsxSwizzleComponent SwizzleX =>
        (RsxSwizzleComponent)((Raw >> 9) & 3);

    public RsxSwizzleComponent SwizzleY =>
        (RsxSwizzleComponent)((Raw >> 11) & 3);

    public RsxSwizzleComponent SwizzleZ =>
        (RsxSwizzleComponent)((Raw >> 13) & 3);

    public RsxSwizzleComponent SwizzleW =>
        (RsxSwizzleComponent)((Raw >> 15) & 3);
}

/// <summary>Exact decoded fragment destination word.</summary>
public readonly record struct RsxFragmentDestination(uint Raw)
{
    public bool End => (Raw & 1) != 0;

    public int RegisterIndex => (int)((Raw >> 1) & 0x3f);

    public bool Fp16 => ((Raw >> 7) & 1) != 0;

    public bool ConditionWriteEnabled => ((Raw >> 8) & 1) != 0;

    public RsxFragmentWriteMask WriteMask =>
        (RsxFragmentWriteMask)((Raw >> 9) & 0x0f);

    public RsxFragmentInputAttribute SourceAttribute =>
        (RsxFragmentInputAttribute)((Raw >> 13) & 0x0f);

    public int TextureUnit => (int)((Raw >> 17) & 0x0f);

    public byte OpcodeLowBits => (byte)((Raw >> 24) & 0x3f);

    public bool NoDestination => ((Raw >> 30) & 1) != 0;

    public bool Saturate => ((Raw >> 31) & 1) != 0;
}

/// <summary>
/// Inline fragment constant retaining exact post-lane-transform IEEE-754 bits
/// as well as backend-neutral float views.
/// </summary>
public readonly record struct RsxFragmentInlineConstant(
    uint XBits,
    uint YBits,
    uint ZBits,
    uint WBits)
{
    public float X => BitConverter.Int32BitsToSingle(unchecked((int)XBits));

    public float Y => BitConverter.Int32BitsToSingle(unchecked((int)YBits));

    public float Z => BitConverter.Int32BitsToSingle(unchecked((int)ZBits));

    public float W => BitConverter.Int32BitsToSingle(unchecked((int)WBits));
}

/// <summary>
/// One immutable RSX fragment instruction. Raw lane-transformed words remain
/// available alongside the current semantic interpretations.
/// </summary>
public readonly record struct RsxFragmentInstruction(
    int Index,
    int Offset,
    uint Dst,
    uint Src0,
    uint Src1,
    uint Src2,
    RsxFragmentOpcode OpcodeType,
    int ByteCount,
    RsxFragmentInlineConstant? Constant)
{
    public static RsxFragmentRegisterType SourceRegisterKind(uint source) =>
        (RsxFragmentRegisterType)(source & 3);

    /// <summary>
    /// Direct backend-neutral CodePixel source row associated with this
    /// instruction's inline constant payload, when uniquely resolved.
    /// </summary>
    public ushort? DirectCodeConstantIndex { get; init; }

    /// <summary>
    /// Stable selected-pass material/literal pixel argument owning this
    /// instruction's inline constant payload. Backends bind its exact float4
    /// at draw time rather than specializing it into the program image.
    /// </summary>
    public int? StaticPixelConstantArgumentOrdinal { get; init; }

    public RsxFragmentDestination Destination => new(Dst);

    public RsxFragmentOperand Source0Operand => new(0, Src0);

    public RsxFragmentOperand Source1Operand => new(1, Src1);

    public RsxFragmentOperand Source2Operand => new(2, Src2);

    public RsxFragmentInlineConstant? InlineConstant => Constant;

    public int OperandCount => IsControlFlow
        ? 0
        : RsxShaderInstructionSet.FragmentOperandCount(OpcodeType);

    public byte Opcode => (byte)OpcodeType;

    public bool End => (Dst & 1) != 0;
    public int DestRegister => (int)((Dst >> 1) & 0x3f);
    public bool DestFp16 => ((Dst >> 7) & 1) != 0;
    public bool CondWriteEnabled => ((Dst >> 8) & 1) != 0;
    public RsxFragmentWriteMask WriteMask =>
        (RsxFragmentWriteMask)((Dst >> 9) & 0x0f);
    public RsxFragmentInputAttribute SourceAttribute =>
        (RsxFragmentInputAttribute)((Dst >> 13) & 0x0f);
    public int TextureUnit => (int)((Dst >> 17) & 0x0f);
    public bool NoDest => ((Dst >> 30) & 1) != 0;
    public bool Saturate => ((Dst >> 31) & 1) != 0;
    public RsxConditionTest ConditionTest =>
        (RsxConditionTest)((Src0 >> 18) & 7);
    public RsxSwizzleComponent ConditionSwizzleX =>
        (RsxSwizzleComponent)((Src0 >> 21) & 3);
    public RsxSwizzleComponent ConditionSwizzleY =>
        (RsxSwizzleComponent)((Src0 >> 23) & 3);
    public RsxSwizzleComponent ConditionSwizzleZ =>
        (RsxSwizzleComponent)((Src0 >> 25) & 3);
    public RsxSwizzleComponent ConditionSwizzleW =>
        (RsxSwizzleComponent)((Src0 >> 27) & 3);
    public bool ConditionWriteRegister1 => ((Src0 >> 30) & 1) != 0;
    public bool ConditionReadRegister1 => ((Src0 >> 31) & 1) != 0;
    public bool IsControlFlow =>
        RsxShaderInstructionSet.IsFragmentControlFlow(OpcodeType);
    public RsxFragmentResultScale Scale =>
        (RsxFragmentResultScale)((Src1 >> 28) & 7);
    public bool IsTexture =>
        !IsControlFlow && RsxShaderInstructionSet.IsFragmentTexture(OpcodeType);

    public RsxSwizzleComponent ConditionSwizzle(int component) =>
        component switch
    {
        0 => ConditionSwizzleX,
        1 => ConditionSwizzleY,
        2 => ConditionSwizzleZ,
        3 => ConditionSwizzleW,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };
}
