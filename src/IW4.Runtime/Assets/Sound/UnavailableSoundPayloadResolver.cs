using IW4.Assets.Assets.Sound;

namespace IW4.Runtime.Assets.Sound;

/// <summary>
/// Explicit resolver for zone inputs that have no associated streamed-sound
/// package source.
/// </summary>
public sealed class UnavailableSoundPayloadResolver : ISoundPayloadResolver
{
    public static UnavailableSoundPayloadResolver Instance { get; } = new();

    private UnavailableSoundPayloadResolver()
    {
    }

    public bool TryResolvePayload(
        StreamedSound sound,
        out byte[] payload,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(sound);
        payload = [];
        reason = "no external sound payload resolver is available";
        return false;
    }
}
