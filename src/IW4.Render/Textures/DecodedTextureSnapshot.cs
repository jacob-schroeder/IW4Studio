using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IW4.Render.Textures;

/// <summary>
/// Immutable decoded texture allocation input. It contains no GL handle and
/// cannot consult an image package after capture.
/// </summary>
public sealed class DecodedTextureResourceSnapshot
{
    private readonly DecodedTextureSubresourceSnapshot[] _subresources;

    internal DecodedTextureResourceSnapshot(
        string name,
        TextureSamplerShape shape,
        string format,
        bool hasTransparency,
        IReadOnlyList<DecodedTextureSubresourceSnapshot> subresources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (shape is not (TextureSamplerShape.TwoDimensional or
                          TextureSamplerShape.Cube))
        {
            throw new ArgumentOutOfRangeException(nameof(shape));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(subresources);
        _subresources = subresources.ToArray();
        int expectedFaceCount = shape == TextureSamplerShape.Cube
            ? 6
            : 1;
        if (_subresources.Length == 0 ||
            _subresources.Any(resource => resource is null))
        {
            throw new ArgumentException(
                "Decoded texture resources require every expected face.",
                nameof(subresources));
        }
        int mipCount = _subresources.Count(resource => resource.FaceOrdinal == 0);
        if (mipCount == 0 || _subresources.Length != expectedFaceCount * mipCount)
        {
            throw new ArgumentException(
                "Every decoded texture face requires the same nonzero mip count.",
                nameof(subresources));
        }
        (int Face, int Mip)[] expectedOrder = Enumerable.Range(0, expectedFaceCount)
            .SelectMany(face => Enumerable.Range(0, mipCount)
                .Select(mip => (face, mip)))
            .ToArray();
        if (!_subresources.Select(resource =>
                    (resource.FaceOrdinal, resource.MipLevel))
                .SequenceEqual(expectedOrder))
        {
            throw new ArgumentException(
                "Decoded texture subresources must use exact face-major/mip-major upload order.",
                nameof(subresources));
        }
        for (int mip = 0; mip < mipCount; mip++)
        {
            DecodedTextureSubresourceSnapshot reference =
                _subresources[mip];
            int expectedWidth = Math.Max(1, _subresources[0].Width >> mip);
            int expectedHeight = Math.Max(1, _subresources[0].Height >> mip);
            if (reference.Width != expectedWidth ||
                reference.Height != expectedHeight ||
                Enumerable.Range(1, expectedFaceCount - 1).Any(face =>
                {
                    DecodedTextureSubresourceSnapshot candidate =
                        _subresources[face * mipCount + mip];
                    return candidate.Width != reference.Width ||
                        candidate.Height != reference.Height;
                }))
            {
                throw new ArgumentException(
                    "Decoded texture faces and mip dimensions are not allocation-compatible.",
                    nameof(subresources));
            }
        }

        DecodedTextureSubresourceSnapshot top = _subresources[0];
        Name = name;
        Shape = shape;
        Format = format;
        Width = top.Width;
        Height = top.Height;
        HasTransparency = hasTransparency;
        Subresources = Array.AsReadOnly(_subresources);
        ContentSha256 = ComputeContentHash(
            shape,
            format,
            hasTransparency,
            _subresources);
    }

    public string Name { get; }

    public TextureSamplerShape Shape { get; }

    public string Format { get; }

    public int Width { get; }

    public int Height { get; }

    public bool HasTransparency { get; }

    public IReadOnlyList<DecodedTextureSubresourceSnapshot> Subresources { get; }

    public string ContentSha256 { get; }

    private static string ComputeContentHash(
        TextureSamplerShape shape,
        string format,
        bool hasTransparency,
        IReadOnlyList<DecodedTextureSubresourceSnapshot> subresources)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([(byte)shape, hasTransparency ? (byte)1 : (byte)0]);
        hash.AppendData(Encoding.UTF8.GetBytes(format));
        Span<byte> metadata = stackalloc byte[16];
        foreach (DecodedTextureSubresourceSnapshot subresource in subresources)
        {
            BinaryPrimitives.WriteInt32BigEndian(metadata[0..4], subresource.FaceOrdinal);
            BinaryPrimitives.WriteInt32BigEndian(metadata[4..8], subresource.MipLevel);
            BinaryPrimitives.WriteInt32BigEndian(metadata[8..12], subresource.Width);
            BinaryPrimitives.WriteInt32BigEndian(metadata[12..16], subresource.Height);
            hash.AppendData(metadata);
            hash.AppendData(subresource.RgbaSpan);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>
/// One immutable decoded face/mip payload in canonical
/// face-major/mip-major resource order.
/// </summary>
public sealed class DecodedTextureSubresourceSnapshot
{
    private readonly byte[] _rgbaBytes;

    internal DecodedTextureSubresourceSnapshot(
        int faceOrdinal,
        int mipLevel,
        int width,
        int height,
        byte[] rgbaBytes)
    {
        if (faceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(faceOrdinal));
        if (mipLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(rgbaBytes);
        if (rgbaBytes.Length != checked(width * height * 4))
        {
            throw new ArgumentException(
                "Decoded RGBA payload length does not match its dimensions.",
                nameof(rgbaBytes));
        }

        // Factory inputs are newly decoded, uniquely owned arrays. Transfer
        // ownership into the immutable snapshot instead of cloning every mip.
        _rgbaBytes = rgbaBytes;
        FaceOrdinal = faceOrdinal;
        MipLevel = mipLevel;
        Width = width;
        Height = height;
        RgbaBytes = Array.AsReadOnly(_rgbaBytes);
    }

    public int FaceOrdinal { get; }

    public int MipLevel { get; }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<byte> RgbaBytes { get; }

    internal ReadOnlySpan<byte> RgbaSpan => _rgbaBytes;

    // Texture and the canonical snapshot are both retained by the
    // scene. The renderer treats texture payloads as immutable upload input,
    // so sharing this owned array avoids retaining a second full RGBA copy.
    internal byte[] SharedRgbaBytes => _rgbaBytes;
}
