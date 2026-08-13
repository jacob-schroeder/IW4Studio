using System.Security.Cryptography;

using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

/// <summary>
/// Exact, owned input to one backend-neutral RSX translation. The digest is
/// only a dictionary prefilter: equality also compares every canonical byte.
/// No source asset, pass, program-resolution, collection, or backend object is
/// retained.
/// </summary>
internal sealed class RsxShaderTranslationRequestSnapshot :
    IEquatable<RsxShaderTranslationRequestSnapshot>
{
    internal const string CanonicalEncodingVersion =
        "rsx-shader-translation-request/v1";

    private readonly byte[] _vertexProgramData;
    private readonly byte[] _pixelProgramData;
    private readonly ArgumentSnapshot[] _arguments;
    private readonly MaterialConstantSnapshot[] _materialConstants;
    private readonly int[] _cubeSamplerDestinations;
    private readonly int[] _shadowSamplerDestinations;
    private readonly int[] _volumeSamplerDestinations;
    private readonly RsxShaderTranslationRequestKey _cacheKey;

    private RsxShaderTranslationRequestSnapshot(
        ReadOnlySpan<byte> vertexProgramData,
        ReadOnlySpan<byte> pixelProgramData,
        byte perPrimArgCount,
        byte perObjArgCount,
        byte stableArgCount,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        MaterialAsset? material,
        IReadOnlySet<int>? cubeSamplerDestinations,
        IReadOnlySet<int>? shadowSamplerDestinations,
        IReadOnlySet<int>? volumeSamplerDestinations,
        RsxShaderTranslationVersionProfile versions)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        versions.Validate();

        _vertexProgramData = vertexProgramData.ToArray();
        _pixelProgramData = pixelProgramData.ToArray();
        PerPrimArgCount = perPrimArgCount;
        PerObjArgCount = perObjArgCount;
        StableArgCount = stableArgCount;
        _arguments = SnapshotArguments(arguments);
        _materialConstants = SnapshotMaterialConstants(material);
        _cubeSamplerDestinations = SnapshotSet(cubeSamplerDestinations);
        _shadowSamplerDestinations = SnapshotSet(
            shadowSamplerDestinations);
        _volumeSamplerDestinations = SnapshotSet(
            volumeSamplerDestinations);
        Versions = versions;

        _cacheKey = new RsxShaderTranslationRequestKey(
            BuildCanonicalContent());
    }

    internal byte PerPrimArgCount { get; }

    internal byte PerObjArgCount { get; }

    internal byte StableArgCount { get; }

    internal RsxShaderTranslationVersionProfile Versions { get; }

    internal string ContentDigest => _cacheKey.ContentDigest;

    internal int RetainedCanonicalByteCount =>
        _cacheKey.RetainedCanonicalByteCount;

    internal RsxShaderTranslationRequestKey CacheKey => _cacheKey;

    internal static RsxShaderTranslationRequestSnapshot Capture(
        MaterialAsset? material,
        MaterialPassAsset sourcePass,
        SelectedPassProgramSources programSources,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        IReadOnlySet<int>? volumeSamplerDestinations = null,
        RsxShaderTranslationVersionProfile? versions = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(programSources);

        return new RsxShaderTranslationRequestSnapshot(
            programSources.VertexProgram.Data.Span,
            programSources.PixelProgram.Data.Span,
            sourcePass.PerPrimArgCount,
            sourcePass.PerObjArgCount,
            sourcePass.StableArgCount,
            programSources.Arguments,
            material,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            volumeSamplerDestinations,
            versions ?? RsxShaderTranslationVersionProfile.Current);
    }

    internal static RsxShaderTranslationRequestSnapshot Capture(
        ReadOnlySpan<byte> vertexProgramData,
        ReadOnlySpan<byte> pixelProgramData,
        MaterialPassAsset pass,
        MaterialAsset? material,
        IReadOnlySet<int>? cubeSamplerDestinations = null,
        IReadOnlySet<int>? shadowSamplerDestinations = null,
        IReadOnlySet<int>? volumeSamplerDestinations = null,
        RsxShaderTranslationVersionProfile? versions = null)
    {
        ArgumentNullException.ThrowIfNull(pass);

        return new RsxShaderTranslationRequestSnapshot(
            vertexProgramData,
            pixelProgramData,
            pass.PerPrimArgCount,
            pass.PerObjArgCount,
            pass.StableArgCount,
            pass.Args,
            material,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            volumeSamplerDestinations,
            versions ?? RsxShaderTranslationVersionProfile.Current);
    }

    internal byte[] CloneVertexProgramData() =>
        _vertexProgramData.ToArray();

    internal RsxProgramSemanticSnapshot ResolveProgramSemantics(
        RsxProgramSemanticCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.Resolve(_vertexProgramData, _pixelProgramData);
    }

    internal MaterialPassAsset CreateTranslationPass() => new()
    {
        PerPrimArgCount = PerPrimArgCount,
        PerObjArgCount = PerObjArgCount,
        StableArgCount = StableArgCount,
        Args = Array.AsReadOnly(
            _arguments.Select(argument => argument.ToAsset()).ToArray())
    };

    internal MaterialAsset? CreateTranslationMaterial()
    {
        if (_materialConstants.Length == 0)
            return null;

        return new MaterialAsset
        {
            Constants = Array.AsReadOnly(
                _materialConstants
                    .Select(constant => constant.ToAsset())
                    .ToArray())
        };
    }

    internal IReadOnlySet<int> CreateCubeSamplerDestinations() =>
        _cubeSamplerDestinations.ToHashSet();

    internal IReadOnlySet<int> CreateShadowSamplerDestinations() =>
        _shadowSamplerDestinations.ToHashSet();

    internal IReadOnlySet<int> CreateVolumeSamplerDestinations() =>
        _volumeSamplerDestinations.ToHashSet();

    internal void EnsureCurrentVersions()
    {
        if (Versions != RsxShaderTranslationVersionProfile.Current)
        {
            throw new InvalidOperationException(
                $"The captured RSX translation version profile '{Versions}' " +
                $"does not match '{RsxShaderTranslationVersionProfile.Current}'.");
        }
    }

    public bool Equals(RsxShaderTranslationRequestSnapshot? other)
    {
        return ReferenceEquals(this, other) ||
               (other is not null && _cacheKey.Equals(other._cacheKey));
    }

    public override bool Equals(object? obj) =>
        obj is RsxShaderTranslationRequestSnapshot other && Equals(other);

    public override int GetHashCode() => _cacheKey.GetHashCode();

    private byte[] BuildCanonicalContent()
    {
        var writer = new RsxCanonicalContentWriter();
        writer.WriteString(CanonicalEncodingVersion);
        writer.WriteString(Versions.TranslatorSemanticVersion);
        writer.WriteString(Versions.VertexDecoderVersion);
        writer.WriteString(Versions.VertexSemanticVersion);
        writer.WriteString(Versions.FragmentDecoderVersion);
        writer.WriteString(Versions.FragmentSemanticVersion);
        writer.WriteBytes(_vertexProgramData);
        writer.WriteBytes(_pixelProgramData);
        writer.WriteByte(PerPrimArgCount);
        writer.WriteByte(PerObjArgCount);
        writer.WriteByte(StableArgCount);

        writer.WriteInt32(_arguments.Length);
        foreach (ArgumentSnapshot argument in _arguments)
            argument.WriteTo(writer);

        writer.WriteInt32(_materialConstants.Length);
        foreach (MaterialConstantSnapshot constant in _materialConstants)
            constant.WriteTo(writer);

        WriteSet(writer, _cubeSamplerDestinations);
        WriteSet(writer, _shadowSamplerDestinations);
        WriteSet(writer, _volumeSamplerDestinations);
        return writer.ToArray();
    }

    private static ArgumentSnapshot[] SnapshotArguments(
        IReadOnlyList<MaterialShaderArgumentAsset> arguments)
    {
        var snapshots = new ArgumentSnapshot[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            MaterialShaderArgumentAsset argument = arguments[index];
            ArgumentNullException.ThrowIfNull(argument);
            snapshots[index] = ArgumentSnapshot.FromAsset(argument);
        }
        return snapshots;
    }

    private static MaterialConstantSnapshot[] SnapshotMaterialConstants(
        MaterialAsset? material)
    {
        if (material is null || material.Constants.Count == 0)
            return [];

        var snapshots = new List<MaterialConstantSnapshot>();
        var encounteredNames = new HashSet<uint>();
        foreach (MaterialConstantDef constant in material.Constants)
        {
            ArgumentNullException.ThrowIfNull(constant);
            if (encounteredNames.Add(constant.NameHash))
                snapshots.Add(MaterialConstantSnapshot.FromAsset(constant));
        }
        return snapshots.ToArray();
    }

    private static int[] SnapshotSet(IReadOnlySet<int>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        int[] sorted = values.ToArray();
        Array.Sort(sorted);
        var uniqueCount = 1;
        for (var index = 1; index < sorted.Length; index++)
        {
            if (sorted[index] == sorted[uniqueCount - 1])
                continue;
            sorted[uniqueCount++] = sorted[index];
        }
        if (uniqueCount != sorted.Length)
            Array.Resize(ref sorted, uniqueCount);
        return sorted;
    }

    private static void WriteSet(
        RsxCanonicalContentWriter writer,
        IReadOnlyList<int> values)
    {
        writer.WriteInt32(values.Count);
        foreach (int value in values)
            writer.WriteInt32(value);
    }

    private readonly record struct ArgumentSnapshot(
        int Offset,
        ushort Type,
        ushort Destination,
        int ArgumentRaw,
        bool HasLiteral,
        uint LiteralX,
        uint LiteralY,
        uint LiteralZ,
        uint LiteralW)
    {
        internal static ArgumentSnapshot FromAsset(
            MaterialShaderArgumentAsset argument)
        {
            MaterialShaderLiteralConstant? literal = argument.LiteralConstant;
            return new ArgumentSnapshot(
                argument.Offset,
                (ushort)argument.Type,
                argument.Dest,
                argument.ArgumentRaw,
                literal.HasValue,
                FloatBits(literal?.X ?? 0),
                FloatBits(literal?.Y ?? 0),
                FloatBits(literal?.Z ?? 0),
                FloatBits(literal?.W ?? 0));
        }

        internal MaterialShaderArgumentAsset ToAsset() => new(
            Offset,
            (MaterialShaderArgumentType)Type,
            Destination,
            ArgumentRaw,
            HasLiteral
                ? new MaterialShaderLiteralConstant(
                    FloatValue(LiteralX),
                    FloatValue(LiteralY),
                    FloatValue(LiteralZ),
                    FloatValue(LiteralW))
                : null);

        internal void WriteTo(RsxCanonicalContentWriter writer)
        {
            writer.WriteInt32(Offset);
            writer.WriteUInt16(Type);
            writer.WriteUInt16(Destination);
            writer.WriteInt32(ArgumentRaw);
            writer.WriteBoolean(HasLiteral);
            if (!HasLiteral)
                return;
            writer.WriteUInt32(LiteralX);
            writer.WriteUInt32(LiteralY);
            writer.WriteUInt32(LiteralZ);
            writer.WriteUInt32(LiteralW);
        }
    }

    private readonly record struct MaterialConstantSnapshot(
        uint NameHash,
        uint X,
        uint Y,
        uint Z,
        uint W)
    {
        internal static MaterialConstantSnapshot FromAsset(
            MaterialConstantDef constant) => new(
                constant.NameHash,
                FloatBits(constant.Literal.X),
                FloatBits(constant.Literal.Y),
                FloatBits(constant.Literal.Z),
                FloatBits(constant.Literal.W));

        internal MaterialConstantDef ToAsset() => new()
        {
            NameHash = NameHash,
            Literal = new MaterialVec4(
                FloatValue(X),
                FloatValue(Y),
                FloatValue(Z),
                FloatValue(W))
        };

        internal void WriteTo(RsxCanonicalContentWriter writer)
        {
            writer.WriteUInt32(NameHash);
            writer.WriteUInt32(X);
            writer.WriteUInt32(Y);
            writer.WriteUInt32(Z);
            writer.WriteUInt32(W);
        }
    }

    private static uint FloatBits(float value) =>
        unchecked((uint)BitConverter.SingleToInt32Bits(value));

    private static float FloatValue(uint bits) =>
        BitConverter.Int32BitsToSingle(unchecked((int)bits));

}

/// <summary>
/// Retained scene-cache identity. It owns only the exact canonical request,
/// allowing the larger translation input snapshot to be collected after a
/// miss has completed.
/// </summary>
internal sealed class RsxShaderTranslationRequestKey :
    IEquatable<RsxShaderTranslationRequestKey>
{
    private readonly byte[] _canonicalContent;
    private readonly int _hashCode;

    internal RsxShaderTranslationRequestKey(byte[] canonicalContent)
    {
        ArgumentNullException.ThrowIfNull(canonicalContent);
        _canonicalContent = canonicalContent;
        ContentDigest =
            Convert.ToHexString(SHA256.HashData(_canonicalContent));
        _hashCode = StringComparer.Ordinal.GetHashCode(ContentDigest);
    }

    internal string ContentDigest { get; }

    internal int RetainedCanonicalByteCount => _canonicalContent.Length;

    public bool Equals(RsxShaderTranslationRequestKey? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null ||
            !string.Equals(
                ContentDigest,
                other.ContentDigest,
                StringComparison.Ordinal))
        {
            return false;
        }

        return _canonicalContent.AsSpan().SequenceEqual(
            other._canonicalContent);
    }

    public override bool Equals(object? obj) =>
        obj is RsxShaderTranslationRequestKey other && Equals(other);

    public override int GetHashCode() => _hashCode;
}

internal readonly record struct RsxShaderTranslationVersionProfile(
    string TranslatorSemanticVersion,
    string VertexDecoderVersion,
    string VertexSemanticVersion,
    string FragmentDecoderVersion,
    string FragmentSemanticVersion)
{
    internal static RsxShaderTranslationVersionProfile Current => new(
        RsxShaderTranslator.CurrentSemanticTranslationVersion,
        RsxVertexProgramIr.CurrentDecoderVersion,
        RsxShaderSemanticIdentity.CurrentVertexSemanticTranslationVersion,
        RsxFragmentProgramIr.CurrentDecoderVersion,
        RsxFragmentProgramIr.CurrentSemanticTranslationVersion);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TranslatorSemanticVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexDecoderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexSemanticVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentDecoderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentSemanticVersion);
    }
}
