using System.Text.Json.Serialization;

namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Six-word PS3 runtime texture descriptor row. It contains no source
/// GfxImage pointer or asset ownership identity.
/// </summary>
public sealed class GfxTexture
{
    public const int SerializedSize = 0x18;
    public const int WordCount = SerializedSize / sizeof(uint);

    private readonly IReadOnlyList<uint> _words;

    [JsonConstructor]
    public GfxTexture(IReadOnlyList<uint> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        uint[] snapshot = words.ToArray();
        if (snapshot.Length != WordCount)
        {
            throw new ArgumentException(
                $"A PS3 GfxTexture descriptor requires exactly {WordCount} words; received {snapshot.Length}.",
                nameof(words));
        }

        _words = Array.AsReadOnly(snapshot);
    }

    public GfxTexture(IEnumerable<uint> words)
        : this(words?.ToArray() ?? throw new ArgumentNullException(nameof(words)))
    {
    }

    public IReadOnlyList<uint> Words => _words;
}
