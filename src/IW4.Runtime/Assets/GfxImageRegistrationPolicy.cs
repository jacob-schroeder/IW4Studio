using System.Buffers.Binary;
using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets;

internal static class GfxImageRegistrationPolicy
{
    public const string PictureFramesName = "me_pictureframes";

    private const int MemoryLocationOffset = 0x0e;
    private const int PixelsOffsetOffset = 0x14;

    public static bool IsPictureFrames(GfxImageAsset image) =>
        string.Equals(image.Name, PictureFramesName, StringComparison.Ordinal);

    /// <summary>
    /// Applies incoming-provider image registration state before canonical
    /// provider selection, so this method runs on the copied incoming header.
    /// </summary>
    public static bool ApplyIncomingNullPayloadHeader(
        GfxImageAsset image,
        byte[] incomingHeader,
        IGfxImageRuntimeRegistrationHooks? hooks)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(incomingHeader);
        if (incomingHeader.Length < GfxImageAsset.SerializedSize)
            throw new InvalidDataException("Incoming GfxImage header is shorter than 0x50 bytes.");
        if (IsPictureFrames(image) || image.PayloadPointer.Raw != 0)
            return false;

        uint? pixelsOffset = null;
        if (hooks is not null && hooks.TryGetNullPayloadPixelsOffset(out uint suppliedOffset))
            pixelsOffset = suppliedOffset;

        image.ApplyNullPayloadRuntimeHeader(pixelsOffset);
        incomingHeader[MemoryLocationOffset] =
            (byte)GfxImageMemoryLocation.Main;
        if (pixelsOffset.HasValue)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                incomingHeader.AsSpan(PixelsOffsetOffset, sizeof(uint)),
                pixelsOffset.Value);
        }

        return pixelsOffset.HasValue;
    }

    public static GfxImageAsset? ResolvePictureFramesRedirect(
        GfxImageAsset image,
        IGfxImageRuntimeRegistrationHooks? hooks)
    {
        if (!IsPictureFrames(image) || hooks is null)
            return null;

        return hooks.TryGetPictureFramesImage(out GfxImageAsset? redirect)
            ? redirect
            : null;
    }
}
