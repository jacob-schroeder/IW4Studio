using System.Collections.Immutable;
using System.Security.Cryptography;

namespace IW4.Render.Shaders;

/// <summary>
/// Backend-neutral, collision-safe semantic identity for one immutable RSX
/// shader stage and its semantic binding ABI. The digest is only a lookup
/// prefilter; equality always compares the complete versioned canonical
/// content.
/// </summary>
/// <remarks>
/// This is deliberately not a final OpenGL or Vulkan shader-cache key. A
/// backend key MUST compose this identity with that backend's exact lowering
/// version, complete backend feature/profile identity, and explicit clip-space
/// policy. Construct semantic identities only during scene loading or shader
/// prewarming and retain them; construction snapshots and hashes complete
/// shader content and is not a steady-state draw-path operation.
/// </remarks>
public sealed class RsxShaderSemanticIdentity :
    IEquatable<RsxShaderSemanticIdentity>
{
    public const string CanonicalEncodingVersion =
        "rsx-shader-semantic-identity/v1";

    public const string CurrentVertexSemanticTranslationVersion =
        "rsx-vertex-translation/3";

    private readonly ImmutableArray<byte> _canonicalContent;

    private RsxShaderSemanticIdentity(
        RenderShaderStage stage,
        ImmutableArray<byte> canonicalContent,
        string shaderAbiContentDigest)
    {
        Stage = stage;
        _canonicalContent = canonicalContent;
        ContentDigest = Convert.ToHexString(
            SHA256.HashData(canonicalContent.AsSpan()));
        ShaderAbiContentDigest = shaderAbiContentDigest;
    }

    /// <summary>The RSX stage described by this semantic identity.</summary>
    public RenderShaderStage Stage { get; }

    /// <summary>
    /// Cached digest of the complete semantic content. It is diagnostic and a
    /// lookup prefilter only; equality also compares the exact canonical bytes.
    /// </summary>
    public string ContentDigest { get; }

    /// <summary>
    /// Diagnostic digest of the exact shader ABI embedded in this identity.
    /// ABI equality never relies on this digest alone.
    /// </summary>
    public string ShaderAbiContentDigest { get; }

    internal ImmutableArray<byte> CanonicalContent => _canonicalContent;

    /// <summary>Logical retained canonical snapshot size in bytes.</summary>
    public int RetainedSnapshotByteCount => _canonicalContent.Length;

    /// <summary>
    /// Creates a cold-path backend-neutral semantic identity for a vertex
    /// program. A backend cache key must add its own lowering/profile facts.
    /// </summary>
    public static RsxShaderSemanticIdentity FromVertex(
        RsxVertexProgramIr program,
        RenderShaderAbiDescriptor abi) =>
        FromVertex(
            program,
            abi,
            CurrentVertexSemanticTranslationVersion);

    internal static RsxShaderSemanticIdentity FromVertex(
        RsxVertexProgramIr program,
        RenderShaderAbiDescriptor abi,
        string semanticTranslationVersion)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(abi);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticTranslationVersion);

        var writer = Begin(
            RenderShaderStage.Vertex,
            program.DecoderVersion,
            semanticTranslationVersion);
        writer.WriteBytes(program.InputProgramBytes.AsSpan());
        writer.WriteInt32(program.UploadOffset);
        writer.WriteInt32(program.Instructions.Length);
        foreach (RsxVertexInstruction instruction in program.Instructions)
        {
            writer.WriteInt32(instruction.Index);
            writer.WriteInt32(instruction.Offset);
            writer.WriteUInt32(instruction.Word0);
            writer.WriteUInt32(instruction.Word1);
            writer.WriteUInt32(instruction.Word2);
            writer.WriteUInt32(instruction.Word3);
        }
        writer.WriteBytes(abi.CanonicalContent.AsSpan());

        return new RsxShaderSemanticIdentity(
            RenderShaderStage.Vertex,
            writer.ToImmutable(),
            abi.ContentDigest);
    }

    /// <summary>
    /// Creates a cold-path backend-neutral semantic identity for a fragment
    /// program. A backend cache key must add its own lowering/profile facts.
    /// </summary>
    public static RsxShaderSemanticIdentity FromFragment(
        RsxFragmentProgramIr program,
        RenderShaderAbiDescriptor abi)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(abi);

        var writer = Begin(
            RenderShaderStage.Fragment,
            program.DecoderVersion,
            program.SemanticTranslationVersion);
        writer.WriteBytes(program.OriginalProgramBytes.AsSpan());
        writer.WriteBytes(program.EffectiveProgramBytes.AsSpan());
        writer.WriteInt32(program.UploadOffset);
        writer.WriteInt32(program.UploadSize);
        WriteProgramControl(writer, program.ProgramControl);

        writer.WriteInt32(program.Instructions.Length);
        foreach (RsxFragmentInstruction instruction in program.Instructions)
            WriteFragmentInstruction(writer, instruction);

        writer.WriteInt32(program.SamplerFeatureProfile.Entries.Length);
        foreach (RsxFragmentSamplerFeature feature in
                 program.SamplerFeatureProfile.Entries)
        {
            writer.WriteInt32(feature.Destination);
            writer.WriteInt32((int)feature.Features);
        }

        writer.WriteInt32(program.DirectCodeConstantBindings.Length);
        foreach (RsxFragmentDirectCodeConstantBinding binding in
                 program.DirectCodeConstantBindings)
        {
            WriteDirectCodeConstantBinding(writer, binding);
        }

        writer.WriteInt32(program.ColorExports.Length);
        foreach (RsxFragmentColorExport export in program.ColorExports)
        {
            writer.WriteInt32(export.ColorTarget);
            writer.WriteBoolean(export.Fp16);
            writer.WriteInt32(export.RegisterIndex);
            writer.WriteByte((byte)export.WrittenComponentMask);
            writer.WriteString(export.WrittenComponents);
        }

        writer.WriteBytes(abi.CanonicalContent.AsSpan());
        return new RsxShaderSemanticIdentity(
            RenderShaderStage.Fragment,
            writer.ToImmutable(),
            abi.ContentDigest);
    }

    public bool Equals(RsxShaderSemanticIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || Stage != other.Stage)
            return false;

        return CanonicalContentEquals(
            ContentDigest,
            _canonicalContent.AsSpan(),
            other.ContentDigest,
            other._canonicalContent.AsSpan());
    }

    public override bool Equals(object? obj) =>
        obj is RsxShaderSemanticIdentity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Stage, StringComparer.Ordinal.GetHashCode(ContentDigest));

    internal static bool CanonicalContentEquals(
        string leftDigest,
        ReadOnlySpan<byte> leftContent,
        string rightDigest,
        ReadOnlySpan<byte> rightContent) =>
        string.Equals(leftDigest, rightDigest, StringComparison.Ordinal) &&
        leftContent.SequenceEqual(rightContent);

    private static RsxCanonicalContentWriter Begin(
        RenderShaderStage stage,
        string decoderVersion,
        string semanticTranslationVersion)
    {
        var writer = new RsxCanonicalContentWriter();
        writer.WriteString(CanonicalEncodingVersion);
        writer.WriteByte((byte)stage);
        writer.WriteString(decoderVersion);
        writer.WriteString(semanticTranslationVersion);
        return writer;
    }

    private static void WriteProgramControl(
        RsxCanonicalContentWriter writer,
        RsxFragmentProgramControl control)
    {
        writer.WriteBoolean(control.IsValid);
        writer.WriteUInt32(control.DescriptorOffset);
        writer.WriteByte(control.RegisterCount);
        writer.WriteByte(control.ExportPrecisionRaw);
        writer.WriteByte(control.DepthExportRaw);
        writer.WriteByte(control.ControlFlagsRaw);
        writer.WriteUInt32(control.EmittedControl);
    }

    private static void WriteFragmentInstruction(
        RsxCanonicalContentWriter writer,
        RsxFragmentInstruction instruction)
    {
        writer.WriteInt32(instruction.Index);
        writer.WriteInt32(instruction.Offset);
        writer.WriteUInt32(instruction.Dst);
        writer.WriteUInt32(instruction.Src0);
        writer.WriteUInt32(instruction.Src1);
        writer.WriteUInt32(instruction.Src2);
        writer.WriteByte(instruction.Opcode);
        writer.WriteInt32(instruction.ByteCount);
        writer.WriteNullableUInt16(instruction.DirectCodeConstantIndex);
        writer.WriteBoolean(instruction.Constant.HasValue);
        if (instruction.Constant is not { } constant)
            return;

        writer.WriteUInt32(constant.XBits);
        writer.WriteUInt32(constant.YBits);
        writer.WriteUInt32(constant.ZBits);
        writer.WriteUInt32(constant.WBits);
    }

    private static void WriteDirectCodeConstantBinding(
        RsxCanonicalContentWriter writer,
        RsxFragmentDirectCodeConstantBinding binding)
    {
        writer.WriteInt32(binding.ArgumentOrdinal);
        writer.WriteUInt16(binding.Destination);
        writer.WriteInt32(binding.ArgumentRaw);
        writer.WriteUInt16(binding.CodeIndex);
        writer.WriteInt32((int)binding.Status);
        writer.WriteInt32(binding.PatchSites.Length);
        foreach (CodePixelConstantPatchSite site in binding.PatchSites)
        {
            writer.WriteUInt16(site.RelativePatchOffset);
            writer.WriteInt32(site.ProgramByteOffset);
            writer.WriteNullableInt32(site.InstructionIndex);
        }
    }

}
