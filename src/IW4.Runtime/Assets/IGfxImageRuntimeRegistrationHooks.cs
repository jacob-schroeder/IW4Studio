using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets;

/// <summary>
/// Supplies process-global image state that the DB loader cannot derive from
/// a fastfile. Implementations belong at application/backend composition
/// boundaries; the runtime contract itself remains backend-neutral.
/// </summary>
public interface IGfxImageRuntimeRegistrationHooks
{
    bool TryGetPictureFramesImage(out GfxImageAsset? image);

    bool TryGetNullPayloadPixelsOffset(out uint pixelsOffset);
}
