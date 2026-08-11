using System.IO.Compression;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen RawFile schema plan. It contains only semantic wire data and owns
/// the native traversal order used for both authored and detached providers.
/// </summary>
internal sealed class RawFileLinkPlan : AssetLinkPlan
{
    private RawFileLinkPlan(
        AssetKey key,
        string originalSerializedName,
        int compressedLength,
        int uncompressedLength,
        byte[]? payload,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? payloadStorage = payload is null
            ? null
            : LinkStorageSymbol.SourceBytes(
                XFileBlockType.LARGE,
                payload,
                alignment: 1);
        var writer = new LinkTemplateWriter(RawFileAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(compressedLength);
        writer.WriteInt32(uncompressedLength);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => payloadStorage is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    PresenceOperation(root, 0x0c, payloadStorage, "RawFile.Buffer")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        RawFileAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            key,
            originalSerializedName,
            definition.CompressedLen,
            definition.Len,
            definition.Buffer,
            freeze);
    }

    private static AssetLinkPlan Create(
        AssetKey key,
        string originalSerializedName,
        int compressedLength,
        int uncompressedLength,
        byte[]? payload,
        LinkAssetFreezeScope freeze)
    {
        if (compressedLength < 0)
            throw new InvalidDataException("RawFile compressed length cannot be negative.");
        if (uncompressedLength < 0)
            throw new InvalidDataException("RawFile uncompressed length cannot be negative.");

        byte[]? copiedPayload = payload?.ToArray();
        bool referencePlaceholder = originalSerializedName.StartsWith(',');
        if (referencePlaceholder &&
            (compressedLength != 0 || uncompressedLength != 0 || copiedPayload is not null))
        {
            throw new InvalidDataException(
                "A comma-prefixed RawFile provider must be an empty reference placeholder.");
        }
        if (referencePlaceholder)
        {
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.RawFile,
                originalSerializedName,
                freeze);
        }

        if (copiedPayload is null)
        {
            if (compressedLength != 0 || uncompressedLength != 0)
            {
                throw new InvalidDataException(
                    "A null RawFile buffer requires zero compressed and uncompressed lengths.");
            }
        }
        else if (compressedLength > 0)
        {
            if (copiedPayload.Length != compressedLength)
            {
                throw new InvalidDataException(
                    "Compressed RawFile payload length must equal compressedLen.");
            }

            ValidateCompressedPayload(copiedPayload, uncompressedLength);
        }
        else
        {
            int expectedLength = checked(uncompressedLength + 1);
            if (copiedPayload.Length != expectedLength || copiedPayload[^1] != 0)
            {
                throw new InvalidDataException(
                    "Uncompressed RawFile payload must be len + 1 and end in NUL.");
            }
        }

        return new RawFileLinkPlan(
            key,
            originalSerializedName,
            compressedLength,
            uncompressedLength,
            copiedPayload,
            freeze);
    }

    private static void ValidateCompressedPayload(
        byte[] payload,
        int expectedLength)
    {
        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var zlib = new ZLibStream(
                input,
                CompressionMode.Decompress,
                leaveOpen: false);
            Span<byte> buffer = stackalloc byte[4096];
            long inflatedLength = 0;
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                inflatedLength += read;
                if (inflatedLength > expectedLength)
                {
                    throw new InvalidDataException(
                        "Compressed RawFile payload inflates beyond its declared length.");
                }
            }

            if (inflatedLength != expectedLength)
            {
                throw new InvalidDataException(
                    $"Compressed RawFile payload inflates to {inflatedLength} bytes; " +
                    $"expected {expectedLength}.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "Compressed RawFile payload is not a valid zlib stream.",
                exception);
        }
    }
}
