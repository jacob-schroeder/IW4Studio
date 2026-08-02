using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets.Images;

/// <summary>
/// Resolves authored image payloads without exposing their package, loader, or
/// graphics-backend implementation. Implementations preserve the compressed
/// source bytes; decoding and GPU upload remain renderer responsibilities.
/// </summary>
/// <remarks>
/// Implementations must support concurrent calls. Successful calls return
/// stable payload objects and byte storage that remain valid and unmodified by
/// later resolver calls; callers must treat that returned storage as read-only.
/// </remarks>
public interface IGfxImagePayloadResolver
{
    bool TryResolveBestPayload(
        GfxImageAsset image,
        out GfxImagePayload payload,
        out string reason);

    bool TryResolveMipPayloads(
        GfxImageAsset image,
        out IReadOnlyList<GfxImagePayload> mips,
        out string reason);
}
