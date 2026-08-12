using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Pointers;

public readonly record struct XPointer<T>
{
    public XPointer(int raw)
        : this(raw, XPointerResolutionMode.None)
    {
    }

    public XPointer(int raw, XPointerResolutionMode resolutionMode)
        : this(raw, resolutionMode, null)
    {
    }

    public XPointer(int raw, XPointerResolutionMode resolutionMode, XBlockAddress? cellAddress)
    {
        Raw = raw;
        ResolutionMode = resolutionMode;
        CellAddress = cellAddress;
    }

    public int Raw { get; }
    public int Value => Raw;
    public XPointerResolutionMode ResolutionMode { get; }
    public XBlockAddress? CellAddress { get; }
    public PointerType Type => XPointerCodec.GetType(Raw);
    public XBlockAddress? PackedAddress =>
        XPointerCodec.TryDecodeBlockAddress(Raw, out XBlockAddress address) ? address : null;
    public bool ConsumesSource => Type is PointerType.Inline or PointerType.Insert;

    public XPointerReference Untyped => XPointerReference.FromRaw(Raw, ResolutionMode, CellAddress);

    public override string ToString()
    {
        string address = PackedAddress is { } packedAddress
            ? $" {ResolutionMode}->{packedAddress}"
            : string.Empty;

        return $"0x{Raw:X8} {Type}{address}";
    }
}
