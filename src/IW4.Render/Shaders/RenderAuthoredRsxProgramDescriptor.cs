using System.Collections.Immutable;
using System.Security.Cryptography;

namespace IW4.Render.Shaders;

/// <summary>
/// Immutable backend-neutral ownership of one exact authored RSX program
/// pair. The descriptor retains semantic IR only; backend shader modules,
/// handles, and lowering policy remain backend-owned.
/// </summary>
public sealed class RenderAuthoredRsxProgramDescriptor :
    IEquatable<RenderAuthoredRsxProgramDescriptor>
{
    private const string CanonicalEncodingVersion =
        "render-authored-rsx-program/v1";

    private readonly ImmutableArray<byte> _canonicalContent;

    public RenderAuthoredRsxProgramDescriptor(
        RsxVertexProgramIr vertexProgram,
        RsxFragmentProgramIr fragmentProgram,
        string programCacheIdentity)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        ArgumentNullException.ThrowIfNull(fragmentProgram);
        ArgumentException.ThrowIfNullOrWhiteSpace(programCacheIdentity);
        ValidateVertexUpload(vertexProgram);
        ValidateFragmentUpload(fragmentProgram);

        VertexProgram = vertexProgram;
        FragmentProgram = fragmentProgram;
        ProgramCacheIdentity = programCacheIdentity;
        _canonicalContent = BuildCanonicalContent(
            vertexProgram,
            fragmentProgram,
            programCacheIdentity);
        ContentDigest = Convert.ToHexString(
            SHA256.HashData(_canonicalContent.AsSpan()));
    }

    public RsxVertexProgramIr VertexProgram { get; }

    public RsxFragmentProgramIr FragmentProgram { get; }

    /// <summary>
    /// Exact cold-path cache identity selected while the authored program was
    /// admitted to the immutable render snapshot.
    /// </summary>
    public string ProgramCacheIdentity { get; }

    /// <summary>
    /// Diagnostic digest of the complete canonical descriptor content.
    /// Equality also compares the canonical bytes and never trusts this
    /// digest alone.
    /// </summary>
    public string ContentDigest { get; }

    internal ImmutableArray<byte> CanonicalContent => _canonicalContent;

    internal bool ContentEquals(RenderAuthoredRsxProgramDescriptor? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || !string.Equals(
                ContentDigest,
                other.ContentDigest,
                StringComparison.Ordinal))
        {
            return false;
        }

        return _canonicalContent.AsSpan().SequenceEqual(
            other._canonicalContent.AsSpan());
    }

    public bool Equals(RenderAuthoredRsxProgramDescriptor? other) =>
        ContentEquals(other);

    public override bool Equals(object? obj) =>
        obj is RenderAuthoredRsxProgramDescriptor other &&
        ContentEquals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(ContentDigest);

    private static void ValidateVertexUpload(RsxVertexProgramIr program)
    {
        if (!program.HasValidUpload || program.Instructions.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The authored RSX vertex program must have a valid non-empty upload.",
                nameof(program));
        }

        long decodedEnd = (long)program.UploadOffset +
            (long)program.Instructions.Length * 16;
        if (program.UploadOffset < 0 || decodedEnd > program.InputByteCount)
        {
            throw new ArgumentException(
                "The authored RSX vertex instruction stream exceeds its exact input snapshot.",
                nameof(program));
        }
    }

    private static void ValidateFragmentUpload(RsxFragmentProgramIr program)
    {
        if (!program.HasValidUpload ||
            program.UploadSize <= 0 ||
            program.Instructions.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The authored RSX fragment program must have a valid non-empty upload.",
                nameof(program));
        }

        long declaredEnd = (long)program.UploadOffset + program.UploadSize;
        long decodedEnd = program.Instructions.Max(instruction =>
            (long)instruction.Offset + instruction.ByteCount);
        if (program.UploadOffset < 0 ||
            declaredEnd > program.EffectiveByteCount ||
            decodedEnd > program.EffectiveByteCount)
        {
            throw new ArgumentException(
                "The authored RSX fragment upload exceeds its exact effective-program snapshot.",
                nameof(program));
        }
    }

    private static ImmutableArray<byte> BuildCanonicalContent(
        RsxVertexProgramIr vertex,
        RsxFragmentProgramIr fragment,
        string programCacheIdentity)
    {
        var writer = new RsxCanonicalContentWriter();
        writer.WriteString(CanonicalEncodingVersion);
        writer.WriteString(programCacheIdentity);

        writer.WriteString(vertex.Identity);
        writer.WriteString(vertex.DecoderVersion);
        writer.WriteBytes(vertex.InputProgramBytes.AsSpan());
        writer.WriteInt32(vertex.UploadOffset);
        writer.WriteInt32(vertex.Instructions.Length);
        foreach (RsxVertexInstruction instruction in vertex.Instructions)
        {
            writer.WriteInt32(instruction.Index);
            writer.WriteInt32(instruction.Offset);
            writer.WriteUInt32(instruction.Word0);
            writer.WriteUInt32(instruction.Word1);
            writer.WriteUInt32(instruction.Word2);
            writer.WriteUInt32(instruction.Word3);
        }

        writer.WriteString(fragment.Identity);
        writer.WriteString(fragment.DecoderVersion);
        writer.WriteString(fragment.SemanticTranslationVersion);
        writer.WriteBytes(fragment.OriginalProgramBytes.AsSpan());
        writer.WriteBytes(fragment.EffectiveProgramBytes.AsSpan());
        writer.WriteInt32(fragment.UploadOffset);
        writer.WriteInt32(fragment.UploadSize);
        writer.WriteInt32(fragment.Instructions.Length);
        foreach (RsxFragmentInstruction instruction in fragment.Instructions)
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
            if (instruction.Constant is { } constant)
            {
                writer.WriteUInt32(constant.XBits);
                writer.WriteUInt32(constant.YBits);
                writer.WriteUInt32(constant.ZBits);
                writer.WriteUInt32(constant.WBits);
            }
        }

        writer.WriteInt32(fragment.StaticConstantPatches.Length);
        foreach (MapRenderStaticFragmentConstantPatch patch in
                 fragment.StaticConstantPatches)
        {
            writer.WriteInt32(patch.ArgumentOrdinal);
            writer.WriteInt32((int)patch.Kind);
            writer.WriteUInt16(patch.Destination);
            writer.WriteInt32(patch.ArgumentRaw);
            writer.WriteSingle(patch.Value.X);
            writer.WriteSingle(patch.Value.Y);
            writer.WriteSingle(patch.Value.Z);
            writer.WriteSingle(patch.Value.W);
            writer.WriteInt32(patch.PatchSiteCount);
        }

        writer.WriteInt32(fragment.DirectCodeConstantBindings.Length);
        foreach (RsxFragmentDirectCodeConstantBinding binding in
                 fragment.DirectCodeConstantBindings)
        {
            writer.WriteInt32(binding.ArgumentOrdinal);
            writer.WriteUInt16(binding.Destination);
            writer.WriteInt32(binding.ArgumentRaw);
            writer.WriteUInt16(binding.CodeIndex);
            writer.WriteInt32((int)binding.Status);
            writer.WriteInt32(binding.PatchSites.Length);
            foreach (MapRenderCodePixelConstantPatchSite site in
                     binding.PatchSites)
            {
                writer.WriteUInt16(site.RelativePatchOffset);
                writer.WriteInt32(site.ProgramByteOffset);
                writer.WriteNullableInt32(site.InstructionIndex);
            }
        }

        RsxFragmentProgramControl control = fragment.ProgramControl;
        writer.WriteBoolean(control.IsValid);
        writer.WriteUInt32(control.DescriptorOffset);
        writer.WriteByte(control.RegisterCount);
        writer.WriteByte(control.ExportPrecisionRaw);
        writer.WriteByte(control.DepthExportRaw);
        writer.WriteByte(control.ControlFlagsRaw);
        writer.WriteUInt32(control.EmittedControl);

        writer.WriteInt32(fragment.SamplerFeatureProfile.Entries.Length);
        foreach (RsxFragmentSamplerFeature feature in
                 fragment.SamplerFeatureProfile.Entries)
        {
            writer.WriteInt32(feature.Destination);
            writer.WriteInt32((int)feature.Features);
        }

        writer.WriteInt32(fragment.ColorExports.Length);
        foreach (RsxFragmentColorExport export in fragment.ColorExports)
        {
            writer.WriteInt32(export.ColorTarget);
            writer.WriteBoolean(export.Fp16);
            writer.WriteInt32(export.RegisterIndex);
            writer.WriteByte(export.WrittenComponentMask);
            writer.WriteString(export.WrittenComponents);
        }

        return writer.ToImmutable();
    }

}
