namespace IW4.Render.Commands;

/// <summary>
/// One contiguous RSX method packet: a count-bearing method header followed
/// by its payload words in command-buffer order.
/// </summary>
public sealed class MapRenderRsxMethodPacket
{
    private const uint MethodMask = 0x0003ffff;
    private const int PayloadCountShift = 18;
    private readonly uint[] _payloads;
    private readonly uint[] _words;

    public MapRenderRsxMethodPacket(
        uint method,
        IReadOnlyList<uint> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if ((method & ~MethodMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(method));
        if (payloads.Count is < 1 or > 0x7ff)
            throw new ArgumentOutOfRangeException(nameof(payloads));

        _payloads = payloads.ToArray();
        Header = ((uint)_payloads.Length << PayloadCountShift) | method;
        _words = new uint[_payloads.Length + 1];
        _words[0] = Header;
        _payloads.CopyTo(_words, 1);

        Method = method;
        Payloads = Array.AsReadOnly(_payloads);
        Words = Array.AsReadOnly(_words);
    }

    public uint Method { get; }

    public uint Header { get; }

    public IReadOnlyList<uint> Payloads { get; }

    public IReadOnlyList<uint> Words { get; }
}
