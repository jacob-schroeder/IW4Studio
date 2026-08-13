namespace IW4.Render.Textures;

/// <summary>
/// Block-compressed formats whose PS3 payload representation is proven to use
/// the API-neutral BC block byte order consumed by the shared decoder.
/// </summary>
public enum AuthoredBlockCompression
{
    Unknown,
    Bc1,
    Bc2,
    Bc3
}

/// <summary>
/// Exact authored bytes for one texture face/mip retained beside the decoded
/// fallback. Direct upload requires both the capture-time layout proof and a
/// backend capability check for <see cref="BlockCompression"/>.
/// </summary>
public sealed class TextureAuthoredSubresource
{
    private readonly byte[] _payload;

    public TextureAuthoredSubresource(
        int faceOrdinal,
        int mipLevel,
        int width,
        int height,
        string format,
        int rowPitchBytes,
        int slicePitchBytes,
        byte[] payload,
        AuthoredBlockCompression blockCompression =
            AuthoredBlockCompression.Unknown,
        bool isDirectUploadLayoutProven = false)
    {
        if (faceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(faceOrdinal));
        if (mipLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (rowPitchBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowPitchBytes));
        if (slicePitchBytes < rowPitchBytes)
            throw new ArgumentOutOfRangeException(nameof(slicePitchBytes));
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length != slicePitchBytes)
        {
            throw new ArgumentException(
                "Authored texture payload length must equal its slice pitch.",
                nameof(payload));
        }
        if (!Enum.IsDefined(blockCompression))
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockCompression));
        }
        if (isDirectUploadLayoutProven &&
            blockCompression == AuthoredBlockCompression.Unknown)
        {
            throw new ArgumentException(
                "A direct-upload layout proof requires a known block-compression format.",
                nameof(isDirectUploadLayoutProven));
        }
        if (blockCompression != AuthoredBlockCompression.Unknown)
        {
            int blockBytes = blockCompression ==
                AuthoredBlockCompression.Bc1
                    ? 8
                    : 16;
            int expectedRowPitch = checked(
                Math.Max(1, (width + 3) >> 2) * blockBytes);
            int expectedSlicePitch = checked(
                expectedRowPitch *
                Math.Max(1, (height + 3) >> 2));
            if (rowPitchBytes != expectedRowPitch ||
                slicePitchBytes != expectedSlicePitch)
            {
                throw new ArgumentException(
                    "Block-compressed authored texture pitches are not tightly packed.",
                    nameof(rowPitchBytes));
            }
        }
        FaceOrdinal = faceOrdinal;
        MipLevel = mipLevel;
        Width = width;
        Height = height;
        Format = format;
        RowPitchBytes = rowPitchBytes;
        SlicePitchBytes = slicePitchBytes;
        BlockCompression = blockCompression;
        IsDirectUploadLayoutProven = isDirectUploadLayoutProven;
        _payload = payload;
        Payload = Array.AsReadOnly(_payload);
    }

    public int FaceOrdinal { get; }

    public int MipLevel { get; }

    public int Width { get; }

    public int Height { get; }

    public string Format { get; }

    public int RowPitchBytes { get; }

    public int SlicePitchBytes { get; }

    public AuthoredBlockCompression BlockCompression { get; }

    /// <summary>
    /// True only when capture proved sequential block rows, tight pitches, and
    /// the BC endpoint/index byte representation used by the shared decoder.
    /// A backend must still prove support for the compressed format.
    /// </summary>
    public bool IsDirectUploadLayoutProven { get; }

    public IReadOnlyList<byte> Payload { get; }

    internal byte[] SharedPayload => _payload;
}
