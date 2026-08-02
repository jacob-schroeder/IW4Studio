using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.MapEditor.Compilation.Bundles;

internal static class CompiledMapBaselineDigest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public static string ComputeAsset(
        string mapIdentity,
        CompiledMapAssetDescriptorSeed descriptor,
        IXAssetBuildData source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, mapIdentity);
        Append(hash, descriptor.Kind.ToString());
        Append(hash, descriptor.SerializedType.ToString());
        Append(hash, descriptor.AssetName);
        Append(hash, descriptor.OwnerRow.SerializedIndex.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, descriptor.IsNested ? "nested" : "top-level");
        Append(hash, descriptor.SourcePath);

        using (var sink = new IncrementalHashWriteStream(
                   hash,
                   cancellationToken))
        {
            JsonSerializer.Serialize(
                sink,
                source,
                source.GetType(),
                JsonOptions);
        }

        if (source is IMapEntsBuildData mapEnts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, "mapEnts.entityStringBytes");
            Append(hash, mapEnts.GetEntityStringBytesCopy());
            Append(hash, "mapEnts.pad29To2B");
            Append(hash, mapEnts.GetPad29To2BCopy());
        }
        if (source is IGameWorldMpBuildData
            {
                GlassData: { } glass
            })
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, "gameMapMp.glassData.pad14To7F");
            Append(hash, glass.GetPad14To7FCopy());
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string ComputeBundle(
        string mapIdentity,
        IEnumerable<CompiledMapAssetDescriptor> assets,
        CancellationToken cancellationToken = default)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, mapIdentity);
        foreach (CompiledMapAssetDescriptor asset in assets
                     .OrderBy(value => value.OwnerRow.SerializedIndex)
                     .ThenBy(value => value.IsNested)
                     .ThenBy(value => value.SerializedType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, asset.Kind.ToString());
            Append(hash, asset.SerializedType.ToString());
            Append(hash, asset.AssetName);
            Append(hash, asset.OwnerRow.SerializedIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, asset.SourcePath);
            Append(hash, asset.BaselineDigest);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Append(hash, bytes);
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed class IncrementalHashWriteStream : Stream
    {
        private readonly IncrementalHash _hash;
        private readonly CancellationToken _cancellationToken;

        public IncrementalHashWriteStream(
            IncrementalHash hash,
            CancellationToken cancellationToken)
        {
            _hash = hash ?? throw new ArgumentNullException(nameof(hash));
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _hash.AppendData(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _hash.AppendData(buffer);
        }
    }
}

internal sealed record CompiledMapAssetDescriptorSeed(
    Editing.SavePlanning.MapAssetKind Kind,
    FastFiles.Zone.XAssetType SerializedType,
    string AssetName,
    TargetZoneRowIdentity OwnerRow,
    bool IsNested,
    string SourcePath);
