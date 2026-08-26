using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Streaming.Sound;
using IW4.Runtime.Assets.Sound;

namespace IW4.FastFiles.Loaders.Streaming.Sound;

/// <summary>
/// Loader-owned binding between the Runtime sound contract and the
/// packfile*.pak implementation owned by FastFiles.Streaming.
/// </summary>
public sealed class StreamedSoundPayloadResolver : ISoundPayloadResolver
{
    private readonly StreamedSoundResolver _streams;

    public StreamedSoundPayloadResolver(StreamedSoundResolver streams)
    {
        _streams = streams ?? throw new ArgumentNullException(nameof(streams));
    }

    public bool TryResolvePayload(
        StreamedSound sound,
        out byte[] payload,
        out string reason) =>
        _streams.TryReadPayload(sound, out payload, out reason);
}
