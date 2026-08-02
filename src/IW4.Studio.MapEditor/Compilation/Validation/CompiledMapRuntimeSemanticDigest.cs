using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;

namespace IW4.Studio.MapEditor.Compilation.Validation;

/// <summary>
/// Relocation-invariant semantic fingerprint used only after a candidate is
/// reopened. Import-time baseline digests remain deliberately strict so they
/// can detect mutation of retained source and linker provenance in memory.
/// </summary>
internal static class CompiledMapRuntimeSemanticDigest
{
    public static string Compute(
        string mapIdentity,
        CompiledMapAssetDescriptorSeed descriptor,
        IXAssetBuildData source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        string assetSemanticDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                source,
                cancellationToken);
        return Compute(
            mapIdentity,
            descriptor,
            assetSemanticDigest,
            cancellationToken);
    }

    public static string Compute(
        string mapIdentity,
        CompiledMapAssetDescriptorSeed descriptor,
        string assetSemanticDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetSemanticDigest);
        cancellationToken.ThrowIfCancellationRequested();

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, mapIdentity);
        Append(hash, descriptor.Kind.ToString());
        Append(hash, descriptor.SerializedType.ToString());
        Append(hash, descriptor.AssetName);
        Append(
            hash,
            descriptor.OwnerRow.SerializedIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, descriptor.IsNested ? "nested" : "top-level");
        Append(hash, descriptor.SourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        Append(hash, assetSemanticDigest);
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static void Append(
        IncrementalHash hash,
        string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(
            length,
            bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
