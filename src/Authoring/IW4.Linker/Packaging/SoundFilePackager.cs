using System.Security.Cryptography;
using IW4.Game.Assets.Sound;

namespace IW4.Linker.Packaging;

/// <summary>
/// Collects unchanged streamed sound payloads for a named, headerless sound package.
/// </summary>
public sealed class SoundFilePackager
{
    private const int PayloadAlignment = 2048;

    private readonly MemoryStream _stream = new();
    private readonly Dictionary<string, List<(byte[] Bytes, int Offset)>> _payloads = new(StringComparer.Ordinal);

    public int PayloadCount { get; private set; }

    public StreamedSound AddPayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0 || payload.Length > SoundFile.MaxInMemoryPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), "A streamed sound payload must be nonempty and within the supported payload limit.");

        string hash = Convert.ToHexString(SHA256.HashData(payload));
        if (_payloads.TryGetValue(hash, out List<(byte[] Bytes, int Offset)>? matches))
        {
            foreach ((byte[] bytes, int existingOffset) in matches)
            {
                if (payload.AsSpan().SequenceEqual(bytes))
                    return CreateReference(existingOffset, bytes.Length);
            }
        }

        long offset = PayloadCount == 0
            ? 0
            : (_stream.Length + PayloadAlignment - 1) / PayloadAlignment * PayloadAlignment;
        if (offset + payload.Length > int.MaxValue)
            throw new InvalidDataException("The sound package exceeds the signed 32-bit stream offset range.");

        _stream.SetLength(offset);
        _stream.Position = offset;
        _stream.Write(payload);
        byte[] copy = payload.ToArray();
        if (matches is null)
            _payloads.Add(hash, new List<(byte[] Bytes, int Offset)> { (copy, checked((int)offset)) });
        else
            matches.Add((copy, checked((int)offset)));
        PayloadCount++;
        return CreateReference(checked((int)offset), payload.Length);
    }

    public byte[] ToArray() => _stream.ToArray();

    private static StreamedSound CreateReference(int offset, int length) => new()
    {
        FileIndex = StreamedSound.NamedFileIndex,
        Source = new StreamedSoundFileSource
        {
            StreamFileOffset = offset,
            StreamFileLength = length
        }
    };
}
