using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
using IW4.Runtime.Assets.Images;

namespace IW4.Studio.Documents;

/// <summary>Exports native pixels and complete original stream parts during offline extraction.</summary>
public static class NativeImageSourceExport
{
    public static IReadOnlyList<string> Unlink(string directory, GfxImageAsset image,
        IGfxImagePayloadResolver resolver)
    {
        IReadOnlyList<byte[]>? parts = null;
        if (image.StreamData.Any(part => part.HasStreamingData) &&
            !resolver.TryResolveStreamParts(image, out parts, out string reason))
            throw new InvalidDataException($"Image '{image.Name}' needs its complete native stream payload: {reason}");
        return new ImageExchange().UnlinkNative(directory, image, parts);
    }
}
