using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

/// <summary>
/// Exact immutable key for one input-router operation. Hashes are only a fast
/// prefilter: equality also compares every captured program byte and vertex
/// route, so a digest collision cannot return a stale route.
/// </summary>
internal readonly struct SamplerRouteCacheKey :
    IEquatable<SamplerRouteCacheKey>
{
    private readonly int _hashCode;

    internal SamplerRouteCacheKey(
        MaterialVertexDeclarationAsset vertexDeclarationReference,
        VertexDeclarationCacheIdentity vertexDeclaration,
        MaterialShaderAsset? vertexShaderReference,
        string? vertexShaderName,
        ProgramDataCacheIdentity vertexProgram,
        MaterialShaderAsset? pixelShaderReference,
        string? pixelShaderName,
        ProgramDataCacheIdentity pixelProgram,
        ushort samplerDestination,
        int textureSemantic)
    {
        ArgumentNullException.ThrowIfNull(vertexDeclarationReference);
        ArgumentNullException.ThrowIfNull(vertexDeclaration);
        ArgumentNullException.ThrowIfNull(vertexProgram);
        ArgumentNullException.ThrowIfNull(pixelProgram);

        VertexDeclarationReference = vertexDeclarationReference;
        VertexDeclaration = vertexDeclaration;
        VertexShaderReference = vertexShaderReference;
        VertexShaderName = vertexShaderName;
        VertexProgram = vertexProgram;
        PixelShaderReference = pixelShaderReference;
        PixelShaderName = pixelShaderName;
        PixelProgram = pixelProgram;
        SamplerDestination = samplerDestination;
        TextureSemantic = textureSemantic;
        _hashCode = HashCode.Combine(
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(vertexDeclarationReference),
                vertexDeclaration.GetHashCode(),
                ReferenceHash(vertexShaderReference),
                StringComparer.Ordinal.GetHashCode(vertexShaderName ?? string.Empty)),
            HashCode.Combine(
                vertexProgram.GetHashCode(),
                ReferenceHash(pixelShaderReference),
                StringComparer.Ordinal.GetHashCode(pixelShaderName ?? string.Empty),
                pixelProgram.GetHashCode()),
            samplerDestination,
            textureSemantic);
    }

    internal MaterialVertexDeclarationAsset VertexDeclarationReference
    {
        get;
    }

    internal VertexDeclarationCacheIdentity VertexDeclaration { get; }

    internal MaterialShaderAsset? VertexShaderReference { get; }

    internal string? VertexShaderName { get; }

    internal ProgramDataCacheIdentity VertexProgram { get; }

    internal MaterialShaderAsset? PixelShaderReference { get; }

    internal string? PixelShaderName { get; }

    internal ProgramDataCacheIdentity PixelProgram { get; }

    internal ushort SamplerDestination { get; }

    internal int TextureSemantic { get; }

    public bool Equals(SamplerRouteCacheKey other) =>
        ReferenceEquals(
            VertexDeclarationReference,
            other.VertexDeclarationReference) &&
        VertexDeclaration.Equals(other.VertexDeclaration) &&
        ReferenceEquals(VertexShaderReference, other.VertexShaderReference) &&
        string.Equals(
            VertexShaderName,
            other.VertexShaderName,
            StringComparison.Ordinal) &&
        VertexProgram.Equals(other.VertexProgram) &&
        ReferenceEquals(PixelShaderReference, other.PixelShaderReference) &&
        string.Equals(
            PixelShaderName,
            other.PixelShaderName,
            StringComparison.Ordinal) &&
        PixelProgram.Equals(other.PixelProgram) &&
        SamplerDestination == other.SamplerDestination &&
        TextureSemantic == other.TextureSemantic;

    public override bool Equals(object? obj) =>
        obj is SamplerRouteCacheKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private static int ReferenceHash(object? value) =>
        value is null ? 0 : RuntimeHelpers.GetHashCode(value);
}

/// <summary>
/// Owned exact program-byte snapshot. Every lookup must copy and hash the
/// current mutable asset bytes (O(vertex bytes + pixel bytes)); this is the
/// deliberate correctness cost that prevents stale hits after in-place edits.
/// </summary>
internal sealed class ProgramDataCacheIdentity :
    IEquatable<ProgramDataCacheIdentity>
{
    private readonly byte[]? _data;
    private readonly int _hashCode;

    private ProgramDataCacheIdentity(byte[]? data)
    {
        _data = data;
        ByteCount = data?.Length ?? -1;
        if (data is null)
        {
            _hashCode = -1;
            return;
        }

        ContentDigest = RsxProgramContentDigest.Compute(data);
        _hashCode = HashCode.Combine(
            ByteCount,
            ContentDigest.GetHashCode());
    }

    internal int ByteCount { get; }

    internal bool HasData => _data is not null;

    internal RsxProgramContentDigest ContentDigest { get; }

    internal byte[] CloneData()
    {
        if (_data is null)
        {
            throw new InvalidOperationException(
                "The captured program-data cell is null.");
        }

        return _data.ToArray();
    }

    internal static ProgramDataCacheIdentity Capture(byte[]? source)
    {
        // Read the asset cell once, then own an exact copy. Downstream keying,
        // decoding and translation all consume this same array.
        return new ProgramDataCacheIdentity(source?.ToArray());
    }

    public bool Equals(ProgramDataCacheIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null ||
            ByteCount != other.ByteCount ||
            ContentDigest != other.ContentDigest)
        {
            return false;
        }

        return _data is null
            ? other._data is null
            : other._data is not null &&
              _data.AsSpan().SequenceEqual(other._data);
    }

    public override bool Equals(object? obj) =>
        obj is ProgramDataCacheIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;
}

/// <summary>
/// Owned structural vertex-declaration snapshot used by both key equality and
/// route selection. The full routing table is retained so inactive-row edits
/// cannot alias a previously captured declaration state.
/// </summary>
internal sealed class VertexDeclarationCacheIdentity :
    IEquatable<VertexDeclarationCacheIdentity>
{
    private readonly int _hashCode;

    private VertexDeclarationCacheIdentity(
        byte streamCount,
        ImmutableArray<MaterialVertexStreamRouting> routes)
    {
        StreamCount = streamCount;
        Routes = routes;
        var hash = new HashCode();
        hash.Add(streamCount);
        hash.Add(routes.Length);
        foreach (MaterialVertexStreamRouting route in routes)
            hash.Add(route);
        _hashCode = hash.ToHashCode();
    }

    internal byte StreamCount { get; }

    internal ImmutableArray<MaterialVertexStreamRouting> Routes { get; }

    internal int ActiveRouteCount => Math.Min(StreamCount, Routes.Length);

    internal static VertexDeclarationCacheIdentity Capture(
        MaterialVertexDeclarationAsset declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        byte streamCount = declaration.StreamCount;
        IReadOnlyList<MaterialVertexStreamRouting> sourceRoutes =
            declaration.Routing;
        return new VertexDeclarationCacheIdentity(
            streamCount,
            sourceRoutes.ToImmutableArray());
    }

    public bool Equals(VertexDeclarationCacheIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null ||
            StreamCount != other.StreamCount ||
            Routes.Length != other.Routes.Length ||
            _hashCode != other._hashCode)
        {
            return false;
        }

        return Routes.AsSpan().SequenceEqual(other.Routes.AsSpan());
    }

    public override bool Equals(object? obj) =>
        obj is VertexDeclarationCacheIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;
}
