using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

using IW4.Render.Textures;

namespace IW4.Render.Shaders;

/// <summary>
/// Stable semantic identity for one shader binding ABI. It is independent of
/// translated program identity and never names a backend pipeline layout.
/// </summary>
public readonly record struct RenderShaderAbiIdentity
{
    private static readonly Encoding CanonicalUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public RenderShaderAbiIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        try
        {
            _ = CanonicalUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A shader ABI identity must contain well-formed Unicode text.",
                nameof(value),
                exception);
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    internal static int GetCanonicalUtf8ByteCount(string value) =>
        CanonicalUtf8.GetByteCount(value);

    internal static int WriteCanonicalUtf8(
        string value,
        Span<byte> destination) =>
        CanonicalUtf8.GetBytes(value, destination);
}

[Flags]
public enum RenderShaderStage : byte
{
    Vertex = 1 << 0,
    Fragment = 1 << 1
}

/// <summary>
/// One semantic shader-stage destination. A binding belongs to exactly one
/// stage; combined stage masks are deliberately rejected.
/// </summary>
public readonly record struct RenderShaderBindingPoint
{
    public RenderShaderBindingPoint(
        RenderShaderStage stage,
        int destination)
    {
        if (stage != RenderShaderStage.Vertex &&
            stage != RenderShaderStage.Fragment)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A shader binding point requires exactly one shader stage.");
        }
        if (destination < 0)
            throw new ArgumentOutOfRangeException(nameof(destination));

        Stage = stage;
        Destination = destination;
    }

    public RenderShaderStage Stage { get; }

    public int Destination { get; }

    internal void Validate(string parameterName)
    {
        if (Stage != RenderShaderStage.Vertex &&
            Stage != RenderShaderStage.Fragment)
        {
            throw new ArgumentException(
                "A shader binding point requires exactly one shader stage.",
                parameterName);
        }
        if (Destination < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                Destination,
                "A shader binding destination cannot be negative.");
        }
    }
}

public enum RenderShaderBindingKind : byte
{
    SampledTexture,
    DynamicConstants
}

public enum RenderDynamicConstantEncoding : byte
{
    Vector4Array,
    Matrix4x4Rows
}

/// <summary>
/// Coordinate convention expected by one dynamic constant binding. Generic
/// values carry no platform convention; Ps3Native values retain the authored
/// RSX coordinate convention for explicit backend lowering.
/// </summary>
public enum RenderShaderCoordinateSpace : byte
{
    Generic,
    Ps3Native
}

/// <summary>
/// Immutable expected shape for one shader ABI binding. The two constructors
/// make sampled textures and dynamic constants disjoint by construction.
/// </summary>
public sealed class RenderShaderBindingRequirement
{
    public RenderShaderBindingRequirement(
        RenderShaderBindingPoint bindingPoint,
        RenderTextureDimension expectedTextureDimension)
    {
        bindingPoint.Validate(nameof(bindingPoint));
        if (!Enum.IsDefined(expectedTextureDimension))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedTextureDimension));
        }

        BindingPoint = bindingPoint;
        Kind = RenderShaderBindingKind.SampledTexture;
        ExpectedTextureDimension = expectedTextureDimension;
    }

    public RenderShaderBindingRequirement(
        RenderShaderBindingPoint bindingPoint,
        RenderDynamicConstantEncoding expectedConstantEncoding,
        RenderShaderCoordinateSpace expectedCoordinateSpace,
        int expectedVectorCount)
    {
        bindingPoint.Validate(nameof(bindingPoint));
        if (!Enum.IsDefined(expectedConstantEncoding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedConstantEncoding));
        }
        if (!Enum.IsDefined(expectedCoordinateSpace))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCoordinateSpace));
        }
        if (expectedVectorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedVectorCount));
        if (expectedConstantEncoding ==
                RenderDynamicConstantEncoding.Matrix4x4Rows &&
            expectedVectorCount % 4 != 0)
        {
            throw new ArgumentException(
                "Matrix4x4 row encoding requires a whole number of four-row matrices.",
                nameof(expectedVectorCount));
        }

        BindingPoint = bindingPoint;
        Kind = RenderShaderBindingKind.DynamicConstants;
        ExpectedConstantEncoding = expectedConstantEncoding;
        ExpectedCoordinateSpace = expectedCoordinateSpace;
        ExpectedVectorCount = expectedVectorCount;
    }

    public RenderShaderBindingPoint BindingPoint { get; }

    public RenderShaderBindingKind Kind { get; }

    public RenderTextureDimension? ExpectedTextureDimension { get; }

    public RenderDynamicConstantEncoding? ExpectedConstantEncoding { get; }

    public RenderShaderCoordinateSpace? ExpectedCoordinateSpace { get; }

    public int? ExpectedVectorCount { get; }
}

/// <summary>
/// Immutable ordered shader binding ABI. Requirement order is semantic and is
/// preserved exactly; one stage destination may occur at most once.
/// </summary>
public sealed class RenderShaderAbiDescriptor :
    IEquatable<RenderShaderAbiDescriptor>
{
    internal const string CanonicalEncodingVersion =
        "render-shader-abi/v1";

    private readonly ImmutableArray<byte> _canonicalContent;

    public RenderShaderAbiDescriptor(
        RenderShaderAbiIdentity identity,
        IEnumerable<RenderShaderBindingRequirement> requirements)
    {
        if (string.IsNullOrWhiteSpace(identity.Value))
            throw new ArgumentException(
                "A shader ABI identity is required.",
                nameof(identity));
        ArgumentNullException.ThrowIfNull(requirements);

        ImmutableArray<RenderShaderBindingRequirement> frozenRequirements =
            requirements.ToImmutableArray();
        if (frozenRequirements.Any(requirement => requirement is null))
        {
            throw new ArgumentException(
                "Shader ABI requirements cannot contain null entries.",
                nameof(requirements));
        }
        if (frozenRequirements
                .Select(requirement => requirement.BindingPoint)
                .Distinct()
                .Count() != frozenRequirements.Length)
        {
            throw new ArgumentException(
                "Shader ABI stage destinations must be unique.",
                nameof(requirements));
        }

        Identity = identity;
        Requirements = frozenRequirements;
        _canonicalContent = BuildCanonicalContent(
            identity,
            frozenRequirements);
        ContentDigest = Convert.ToHexString(
            SHA256.HashData(_canonicalContent.AsSpan()));
    }

    public RenderShaderAbiIdentity Identity { get; }

    public ImmutableArray<RenderShaderBindingRequirement> Requirements
        { get; }

    /// <summary>
    /// Cached SHA-256 of the versioned canonical ABI content. Equality uses
    /// this digest only as a prefilter and always compares the exact canonical
    /// bytes before reporting equal content.
    /// </summary>
    public string ContentDigest { get; }

    internal ImmutableArray<byte> CanonicalContent => _canonicalContent;

    public bool ContentEquals(RenderShaderAbiDescriptor? other)
    {
        if (other is null)
            return false;

        return CanonicalContentEquals(
            ContentDigest,
            _canonicalContent.AsSpan(),
            other.ContentDigest,
            other._canonicalContent.AsSpan());
    }

    public bool Equals(RenderShaderAbiDescriptor? other) =>
        ContentEquals(other);

    public override bool Equals(object? obj) =>
        obj is RenderShaderAbiDescriptor other && ContentEquals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(ContentDigest);

    internal static bool CanonicalContentEquals(
        string leftDigest,
        ReadOnlySpan<byte> leftContent,
        string rightDigest,
        ReadOnlySpan<byte> rightContent) =>
        string.Equals(leftDigest, rightDigest, StringComparison.Ordinal) &&
        leftContent.SequenceEqual(rightContent);

    private static ImmutableArray<byte> BuildCanonicalContent(
        RenderShaderAbiIdentity identity,
        ImmutableArray<RenderShaderBindingRequirement> requirements)
    {
        // v1 uses little-endian integers and length-prefixed UTF-8 strings.
        // Each nullable field writes an explicit one-byte presence marker,
        // followed by its Int32 value when present. Requirement order is
        // retained exactly.
        var writer = new CanonicalContentWriter();
        writer.WriteString(CanonicalEncodingVersion);
        writer.WriteString(identity.Value);
        writer.WriteInt32(requirements.Length);
        foreach (RenderShaderBindingRequirement requirement in requirements)
        {
            writer.WriteByte((byte)requirement.BindingPoint.Stage);
            writer.WriteInt32(requirement.BindingPoint.Destination);
            writer.WriteByte((byte)requirement.Kind);
            writer.WriteNullableEnum(requirement.ExpectedTextureDimension);
            writer.WriteNullableEnum(requirement.ExpectedConstantEncoding);
            writer.WriteNullableEnum(requirement.ExpectedCoordinateSpace);
            writer.WriteNullableInt32(requirement.ExpectedVectorCount);
        }

        return ImmutableArray.CreateRange(writer.WrittenSpan.ToArray());
    }

    private sealed class CanonicalContentWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        internal ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

        internal void WriteByte(byte value)
        {
            Span<byte> destination = _buffer.GetSpan(1);
            destination[0] = value;
            _buffer.Advance(1);
        }

        internal void WriteInt32(int value)
        {
            Span<byte> destination = _buffer.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            _buffer.Advance(sizeof(int));
        }

        internal void WriteString(string value)
        {
            int byteCount =
                RenderShaderAbiIdentity.GetCanonicalUtf8ByteCount(value);
            WriteInt32(byteCount);
            Span<byte> destination = _buffer.GetSpan(byteCount);
            int written = RenderShaderAbiIdentity.WriteCanonicalUtf8(
                value,
                destination);
            _buffer.Advance(written);
        }

        internal void WriteNullableInt32(int? value)
        {
            WriteByte(value.HasValue ? (byte)1 : (byte)0);
            if (value.HasValue)
                WriteInt32(value.Value);
        }

        internal void WriteNullableEnum<TEnum>(TEnum? value)
            where TEnum : struct, Enum
        {
            WriteByte(value.HasValue ? (byte)1 : (byte)0);
            if (value.HasValue)
                WriteInt32(Convert.ToInt32(value.Value));
        }
    }
}
