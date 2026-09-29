using IW4.Game.Assets.Image;
using IW4.Runtime.Assets.Images;

namespace Iw4Radiant.Materials;

internal sealed class NativeImageParts(string? name, IReadOnlyList<byte[]>? parts) : IGfxImagePayloadResolver
{
    public bool TryResolveBestPayload(GfxImageAsset image, out GfxImagePayload payload, out string reason)
    {
        payload = default;
        if (name is null || image.Name != name || parts is null)
        {
            reason = $"Image '{image.Name}' has no native stream parts.";
            return false;
        }
        foreach (var item in image.StreamData.Select((data, index) => (data, index))
                     .Where(item => item.data.HasStreamingData)
                     .OrderByDescending(item => (long)item.data.Width * item.data.Height))
        {
            if (item.index >= parts.Count || parts[item.index].Length == 0) continue;
            payload = new GfxImagePayload(item.data.Width, item.data.Height, parts[item.index]);
            reason = string.Empty;
            return true;
        }
        reason = $"Image '{name}' has no populated native stream part.";
        return false;
    }

    public bool TryResolveStreamParts(GfxImageAsset image, out IReadOnlyList<byte[]> resolved, out string reason)
    {
        if (name is not null && image.Name == name && parts is not null)
        {
            resolved = parts;
            reason = string.Empty;
            return true;
        }
        resolved = [];
        reason = $"Image '{image.Name}' has no native stream parts.";
        return false;
    }

    public bool TryResolveMipPayloads(GfxImageAsset image, out IReadOnlyList<GfxImagePayload> mips, out string reason)
    {
        mips = [];
        reason = "Native preview requests only the highest available mip.";
        return false;
    }
}
