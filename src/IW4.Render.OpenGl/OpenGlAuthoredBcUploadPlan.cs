using IW4.Render.Textures;

namespace IW4.Render.OpenGl;

/// <summary>
/// Canonical, complete face/mip plan for authored BC payloads. Creation is the
/// final structural gate before an OpenGL capability check and compressed
/// upload; no format is inferred from a display string.
/// </summary>
internal sealed class OpenGlAuthoredBcUploadPlan
{
    private OpenGlAuthoredBcUploadPlan(
        AuthoredBlockCompression blockCompression,
        int faceCount,
        int mipLevelCount,
        TextureAuthoredSubresource[] subresources,
        long payloadBytes)
    {
        BlockCompression = blockCompression;
        FaceCount = faceCount;
        MipLevelCount = mipLevelCount;
        Subresources = subresources;
        PayloadBytes = payloadBytes;
    }

    internal AuthoredBlockCompression BlockCompression { get; }

    internal int FaceCount { get; }

    internal int MipLevelCount { get; }

    internal IReadOnlyList<TextureAuthoredSubresource>
        Subresources { get; }

    internal long PayloadBytes { get; }

    internal static bool TryCreate(
        Texture? texture,
        out OpenGlAuthoredBcUploadPlan plan)
    {
        plan = null!;
        if (texture is null ||
            texture.Width <= 0 ||
            texture.Height <= 0)
        {
            return false;
        }

        int faceCount = texture.Target switch
        {
            TextureTarget.Texture2D => 1,
            TextureTarget.TextureCube
                when texture.Width == texture.Height => 6,
            _ => 0,
        };
        IReadOnlyList<TextureAuthoredSubresource> authored =
            texture.EffectiveAuthoredSubresources;
        if (faceCount == 0 || authored.Count == 0)
            return false;

        int mipLevelCount;
        try
        {
            mipLevelCount = checked(
                authored.Max(value => value.MipLevel) + 1);
            if (authored.Count != checked(faceCount * mipLevelCount))
                return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        int maximumMipLevelCount = 1;
        int maximumMipWidth = texture.Width;
        int maximumMipHeight = texture.Height;
        while (maximumMipWidth > 1 || maximumMipHeight > 1)
        {
            maximumMipWidth = Math.Max(1, maximumMipWidth / 2);
            maximumMipHeight = Math.Max(1, maximumMipHeight / 2);
            maximumMipLevelCount++;
        }
        if (mipLevelCount > maximumMipLevelCount)
            return false;

        var canonical =
            new TextureAuthoredSubresource[authored.Count];
        AuthoredBlockCompression compression =
            AuthoredBlockCompression.Unknown;
        long payloadBytes = 0;
        foreach (TextureAuthoredSubresource? subresource in authored)
        {
            if (subresource is null ||
                !subresource.IsDirectUploadLayoutProven ||
                subresource.BlockCompression ==
                    AuthoredBlockCompression.Unknown ||
                subresource.FaceOrdinal >= faceCount ||
                subresource.MipLevel >= mipLevelCount)
            {
                return false;
            }

            if (compression == AuthoredBlockCompression.Unknown)
                compression = subresource.BlockCompression;
            else if (compression != subresource.BlockCompression)
                return false;

            int blockBytes = compression ==
                AuthoredBlockCompression.Bc1
                    ? 8
                    : 16;
            int expectedWidth = Math.Max(
                1,
                texture.Width >> subresource.MipLevel);
            int expectedHeight = Math.Max(
                1,
                texture.Height >> subresource.MipLevel);
            int expectedRowPitch;
            int expectedSlicePitch;
            int coordinate;
            try
            {
                expectedRowPitch = checked(
                    Math.Max(1, (expectedWidth + 3) >> 2) *
                    blockBytes);
                expectedSlicePitch = checked(
                    expectedRowPitch *
                    Math.Max(1, (expectedHeight + 3) >> 2));
                coordinate = checked(
                    subresource.FaceOrdinal * mipLevelCount +
                    subresource.MipLevel);
                payloadBytes = checked(
                    payloadBytes + subresource.SlicePitchBytes);
            }
            catch (OverflowException)
            {
                return false;
            }

            if (subresource.Width != expectedWidth ||
                subresource.Height != expectedHeight ||
                subresource.RowPitchBytes != expectedRowPitch ||
                subresource.SlicePitchBytes != expectedSlicePitch ||
                subresource.SharedPayload.Length != expectedSlicePitch ||
                canonical[coordinate] is not null)
            {
                return false;
            }
            canonical[coordinate] = subresource;
        }

        if (canonical.Any(value => value is null))
            return false;

        plan = new OpenGlAuthoredBcUploadPlan(
            compression,
            faceCount,
            mipLevelCount,
            canonical,
            payloadBytes);
        return true;
    }
}

internal readonly record struct MapRenderOpenGlCompressedTextureSupport(
    bool Bc1,
    bool Bc2,
    bool Bc3)
{
    internal bool Supports(
        AuthoredBlockCompression compression) =>
        compression switch
        {
            AuthoredBlockCompression.Bc1 => Bc1,
            AuthoredBlockCompression.Bc2 => Bc2,
            AuthoredBlockCompression.Bc3 => Bc3,
            _ => false,
        };
}
