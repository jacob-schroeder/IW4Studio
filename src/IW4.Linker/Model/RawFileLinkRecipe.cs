using System.IO.Compression;
using System.Text;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen RawFile schema recipe. It contains only semantic wire data and owns
/// the native traversal order used for both authored and detached providers.
/// </summary>
internal sealed class RawFileLinkRecipe
{
    private readonly byte[] _nameBytes;
    private readonly byte[]? _payload;

    private RawFileLinkRecipe(
        string originalSerializedName,
        byte[] nameBytes,
        int compressedLength,
        int uncompressedLength,
        byte[]? payload)
    {
        OriginalSerializedName = originalSerializedName;
        _nameBytes = nameBytes;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _payload = payload;
        IsReferencePlaceholder = originalSerializedName[0] == ',';
    }

    public string OriginalSerializedName { get; }
    public int CompressedLength { get; }
    public int UncompressedLength { get; }
    public bool IsReferencePlaceholder { get; }

    public static RawFileLinkRecipe Freeze(
        AssetKey key,
        RawFileAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string name = definition.Name ?? throw new InvalidDataException(
            "RawFile provider definition has no name.");
        return Create(
            key,
            name,
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

    public void Emit(ZoneEmissionWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
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

            output.Allocate(
                XFileBlockType.LARGE,
                _nameBytes.Length,
                alignment: 1);
            output.WriteBytes(_nameBytes);

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
        ValidateName(key, originalSerializedName);
        if (compressedLength < 0)
            throw new InvalidDataException("RawFile compressed length cannot be negative.");
        if (uncompressedLength < 0)
            throw new InvalidDataException("RawFile uncompressed length cannot be negative.");

        byte[]? copiedPayload = payload?.ToArray();
        bool referencePlaceholder = originalSerializedName[0] == ',';
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

        byte[] nameBytes = EncodeCString(originalSerializedName);
        return new RawFileLinkRecipe(
            originalSerializedName,
            nameBytes,
            compressedLength,
            uncompressedLength,
            copiedPayload);
    }

    private static void ValidateName(
        AssetKey key,
        string originalSerializedName)
    {
        if (string.IsNullOrEmpty(originalSerializedName))
            throw new InvalidDataException("RawFile name cannot be null or empty.");
        if (originalSerializedName.Contains('\0'))
            throw new InvalidDataException("RawFile name cannot contain NUL.");
        if (originalSerializedName.Any(character => character > byte.MaxValue))
            throw new InvalidDataException("RawFile name must be representable as Latin-1.");

        AssetKey wireKey = AssetKey.FromWireName(
            key.Family,
            originalSerializedName);
        if (wireKey != key)
        {
            throw new InvalidDataException(
                $"RawFile name '{originalSerializedName}' does not normalize to {key}.");
        }
    }

    private static byte[] EncodeCString(string value)
    {
        byte[] result = new byte[checked(value.Length + 1)];
        int written = Encoding.Latin1.GetBytes(value, result);
        if (written != value.Length)
            throw new InvalidDataException("RawFile name could not be encoded as Latin-1.");
        return result;
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
