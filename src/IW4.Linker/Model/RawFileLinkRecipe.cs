using System.IO.Compression;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen RawFile schema recipe. It contains only semantic wire data and owns
/// the native traversal order used for both authored and detached providers.
/// </summary>
internal sealed class RawFileLinkRecipe : AssetLinkRecipe
{
    private readonly byte[]? _payload;

    private RawFileLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        int compressedLength,
        int uncompressedLength,
        byte[]? payload)
        : base(key, originalSerializedName)
    {
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _payload = payload;
    }

    private int CompressedLength { get; }
    private int UncompressedLength { get; }

    public static RawFileLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        RawFileAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            key,
            originalSerializedName,
            definition.CompressedLen,
            definition.Len,
            definition.Buffer);
    }

    public static RawFileLinkRecipe CreateExternal(
        AssetKey key,
        string originalSerializedName)
    {
        if (string.IsNullOrEmpty(originalSerializedName) ||
            originalSerializedName[0] != ',')
        {
            throw new ArgumentException(
                "An external RawFile name must begin with one comma.",
                nameof(originalSerializedName));
        }

        return Create(
            key,
            originalSerializedName,
            compressedLength: 0,
            uncompressedLength: 0,
            payload: null);
    }

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);
        output.PushTempScope();
        try
        {
            output.Allocate(
                XFileBlockType.TEMP,
                RawFileAsset.SerializedSize,
                alignment: 4);
            output.WriteInt32(-1);
            output.WriteInt32(CompressedLength);
            output.WriteInt32(UncompressedLength);
            output.WriteInt32(_payload is null ? 0 : -1);

            EmitName(output);

            if (_payload is not null)
            {
                output.Allocate(
                    XFileBlockType.LARGE,
                    _payload.Length,
                    alignment: 1);
                output.WriteBytes(_payload);
            }
        }
        finally
        {
            output.PopTempScope();
        }
    }

    private static RawFileLinkRecipe Create(
        AssetKey key,
        string originalSerializedName,
        int compressedLength,
        int uncompressedLength,
        byte[]? payload)
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

        return new RawFileLinkRecipe(
            key,
            originalSerializedName,
            compressedLength,
            uncompressedLength,
            copiedPayload);
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
