using IW4.Game.Assets.Image;

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

    public bool TryResolveStreamParts(GfxImageAsset image, out IReadOnlyList<byte[]> parts, out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        parts = [];
        reason = "no external image payload resolver is available";
        return false;
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
