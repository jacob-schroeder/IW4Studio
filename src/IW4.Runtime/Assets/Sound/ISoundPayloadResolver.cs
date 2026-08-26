using IW4.Assets.Assets.Sound;

namespace IW4.Runtime.Assets.Sound;

/// <summary>
/// Resolves streamed sound payloads without exposing their package or loader
/// implementation.
/// </summary>
/// <remarks>
/// Implementations must support concurrent calls. Each successful call returns
/// stable byte storage that remains valid and unmodified by later calls.
/// </remarks>
public interface ISoundPayloadResolver
{
    bool TryResolvePayload(
        StreamedSound sound,
        out byte[] payload,
        out string reason);
}
