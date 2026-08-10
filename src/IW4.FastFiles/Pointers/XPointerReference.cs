using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Pointers;

public readonly record struct XPointerReference(
    int Raw,
    PointerType Type,
    XPointerResolutionMode ResolutionMode,
    XBlockAddress? PackedAddress,
    XBlockAddress? CellAddress)
{
    public XPointerOffsetMode OffsetMode => ResolutionMode.ToOffsetMode();
    public bool ConsumesSource => Type is PointerType.Inline or PointerType.Insert;

    public XPointer<T> AsPointer<T>() => new(Raw, ResolutionMode, CellAddress);

    public static XPointerReference FromRaw(
        int raw,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None,
        XBlockAddress? cellAddress = null)
    {
        PointerType type = XPointerCodec.GetType(raw);
        XBlockAddress? packedAddress = XPointerCodec.TryDecodeBlockAddress(raw, out XBlockAddress address)
            ? address
            : null;

        return new XPointerReference(raw, type, resolutionMode, packedAddress, cellAddress);
    }

    public static XPointerReference FromRaw(
        int raw,
        XPointerOffsetMode offsetMode,
        XBlockAddress? cellAddress = null)
    {
        return FromRaw(raw, offsetMode.ToResolutionMode(), cellAddress);
    }
}
