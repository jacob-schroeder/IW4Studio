using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Canonical SHA-256 identity of one collision compiler invocation. This is
/// tooling metadata, not an IW4 serialized checksum.
/// </summary>
public sealed record CollisionCompilerBuildIdentity
{
    internal CollisionCompilerBuildIdentity(
        CollisionCompilerSha256Digest digest) =>
        Digest = digest ??
            throw new ArgumentNullException(nameof(digest));

    public CollisionCompilerSha256Digest Digest { get; }

    public override string ToString() => Digest.Value;
}

/// <summary>
/// Produces the versioned, cross-platform build identity defined by the M0
/// compiler contract. Every value is tag-delimited and length-prefixed; all
/// integers use big-endian byte order and the document ID uses lowercase
/// GUID-N text. Changing this encoding is a compiler-contract change.
/// </summary>
public static class CollisionCompilerBuildIdentityCalculator
{
    private const string Domain =
        "iw4-studio.map-editor.colmap-build-identity/v1";

    public static CollisionCompilerBuildIdentity Compute(
        CollisionCompilerBuildIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendUtf8(hash, "domain", Domain);
        AppendUtf8(hash, "contract-id", input.Contract.ContractId);
        AppendInt32(
            hash,
            "contract-major",
            input.Contract.Version.Major);
        AppendInt32(
            hash,
            "contract-minor",
            input.Contract.Version.Minor);
        AppendInt32(
            hash,
            "contract-patch",
            input.Contract.Version.Patch);
        AppendUtf8(
            hash,
            "document-id",
            input.DocumentId.Value.ToString("N"));
        AppendInt64(
            hash,
            "document-revision",
            input.DocumentRevision);
        AppendDigest(
            hash,
            "semantic-source-sha256",
            input.SemanticSourceDigest);
        AppendDigest(
            hash,
            "settings-sha256",
            input.SettingsDigest);
        AppendDigest(
            hash,
            "dependencies-sha256",
            input.DependencyDigest);

        return new CollisionCompilerBuildIdentity(
            new CollisionCompilerSha256Digest(
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant()));
    }

    private static void AppendDigest(
        IncrementalHash hash,
        string tag,
        CollisionCompilerSha256Digest digest) =>
        Append(hash, tag, Convert.FromHexString(digest.Value));

    private static void AppendUtf8(
        IncrementalHash hash,
        string tag,
        string value) =>
        Append(hash, tag, Encoding.UTF8.GetBytes(value));

    private static void AppendInt32(
        IncrementalHash hash,
        string tag,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        Append(hash, tag, bytes);
    }

    private static void AppendInt64(
        IncrementalHash hash,
        string tag,
        long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        Append(hash, tag, bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        ReadOnlySpan<byte> value)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        AppendLength(hash, tagBytes.Length);
        hash.AppendData(tagBytes);
        AppendLength(hash, value.Length);
        hash.AppendData(value);
    }

    private static void AppendLength(
        IncrementalHash hash,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
