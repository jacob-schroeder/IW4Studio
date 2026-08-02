using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets.Images;

/// <summary>
/// Explicit resolver for document and scene inputs that have no external image
/// package source. Inline payloads remain available on the asset itself.
/// </summary>
public sealed class UnavailableGfxImagePayloadResolver : IGfxImagePayloadResolver
{
    public static UnavailableGfxImagePayloadResolver Instance { get; } = new();

    private UnavailableGfxImagePayloadResolver()
    {
    }

    public bool TryResolveBestPayload(
        GfxImageAsset image,
        out GfxImagePayload payload,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        payload = default;
        reason = "no external image payload resolver is available";
        return false;
    }

    public bool TryResolveMipPayloads(
        GfxImageAsset image,
        out IReadOnlyList<GfxImagePayload> mips,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        mips = [];
        reason = "no external image payload resolver is available";
        return false;
    }
}
