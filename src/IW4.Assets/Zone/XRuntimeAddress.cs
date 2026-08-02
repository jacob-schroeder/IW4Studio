using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Zone;

public readonly record struct XRuntimeAddress
{
    private XRuntimeAddress(
        XRuntimeAddressKind kind,
        XBlockAddress? blockAddress,
        XAssetPoolAddress? assetPoolAddress)
    {
        Kind = kind;
        BlockAddress = blockAddress;
        AssetPoolAddress = assetPoolAddress;
    }

    public XRuntimeAddressKind Kind { get; }
    public XBlockAddress? BlockAddress { get; }
    public XAssetPoolAddress? AssetPoolAddress { get; }

    public int RawValue => Kind switch
    {
        XRuntimeAddressKind.BlockStream when BlockAddress is { } address => XPointerCodec.Encode(address),
        XRuntimeAddressKind.AssetPool when AssetPoolAddress is { } address => address.RawValue,
        _ => throw new InvalidOperationException("Runtime address has no backing address value.")
    };

    public static XRuntimeAddress FromBlock(XBlockAddress address) =>
        new(XRuntimeAddressKind.BlockStream, address, null);

    public static XRuntimeAddress FromAssetPool(XAssetPoolAddress address) =>
        new(XRuntimeAddressKind.AssetPool, null, address);

    public static implicit operator XRuntimeAddress(XBlockAddress address) => FromBlock(address);

    public override string ToString() => Kind switch
    {
        XRuntimeAddressKind.BlockStream => BlockAddress?.ToString() ?? "BLOCK:<missing>",
        XRuntimeAddressKind.AssetPool => AssetPoolAddress?.ToString() ?? "ASSET_POOL:<missing>",
        _ => "<invalid runtime address>"
    };
}
