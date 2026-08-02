using IW4.Assets.Zone;
using System.Buffers.Binary;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Copy-on-write managed projection of the native stable-head allocation.
/// Provider definitions remain zone-owned and immutable; replacement may move
/// a provider into this projection or preserve destination GfxImage state.
/// </summary>
internal sealed class XAssetCanonicalProjection
{
    private XAssetCanonicalProjection(
        BaseAsset asset,
        byte[] headerBytes,
        byte[] nativePoolCopyBytes,
        int nativePoolCopyCapturedLength,
        IXAssetSourceMemory? sourceBlocks)
    {
        Asset = asset;
        HeaderBytes = headerBytes;
        NativePoolCopyBytes = nativePoolCopyBytes;
        NativePoolCopyCapturedLength = nativePoolCopyCapturedLength;
        SourceBlocks = sourceBlocks;
    }

    public BaseAsset Asset { get; }

    public byte[] HeaderBytes { get; }

    public byte[] NativePoolCopyBytes { get; }

    public int NativePoolCopyCapturedLength { get; }

    public IXAssetSourceMemory? SourceBlocks { get; }

    public static XAssetCanonicalProjection FromProvider(
        XAssetProviderContribution provider) =>
        new(
            provider.Asset,
            (byte[])provider.HeaderBytes.Clone(),
            (byte[])provider.NativePoolCopyBytes.Clone(),
            provider.NativePoolCopyCapturedLength,
            provider.SourceBlocks);

    public XAssetCanonicalProjection Clone() =>
        new(
            Asset,
            (byte[])HeaderBytes.Clone(),
            (byte[])NativePoolCopyBytes.Clone(),
            NativePoolCopyCapturedLength,
            SourceBlocks);

    public XAssetCanonicalProjection KeepImageDestinationWithSourceName(
        XAssetProviderContribution source,
        XAssetPoolAddress slotAddress)
    {
        if (Asset is not GfxImageAsset destination ||
            source.Asset is not GfxImageAsset incoming ||
            HeaderBytes.Length < GfxImageAsset.SerializedSize ||
            source.HeaderBytes.Length < GfxImageAsset.SerializedSize)
        {
            throw new InvalidOperationException(
                "KeepDestinationWithSourceName is valid only for complete GfxImage projections.");
        }

        byte[] projectedHeader = (byte[])HeaderBytes.Clone();
        byte[] projectedPoolCopy = (byte[])NativePoolCopyBytes.Clone();
        bool resetPixels = destination.PayloadPointer.Raw != 0;
        ApplyImageReleaseHeader(projectedHeader, resetPixels);
        ApplyImageReleaseHeader(projectedPoolCopy, resetPixels);
        source.HeaderBytes.AsSpan(0x4c, sizeof(int)).CopyTo(projectedHeader.AsSpan(0x4c, sizeof(int)));
        source.NativePoolCopyBytes.AsSpan(0x4c, sizeof(int)).CopyTo(
            projectedPoolCopy.AsSpan(0x4c, sizeof(int)));

        GfxImageAsset projectedAsset = CreateProjectedImage(
            destination,
            incoming,
            slotAddress,
            resetPixels);
        return new XAssetCanonicalProjection(
            projectedAsset,
            projectedHeader,
            projectedPoolCopy,
            Math.Min(NativePoolCopyCapturedLength, projectedPoolCopy.Length),
            source.SourceBlocks);
    }

    private static void ApplyImageReleaseHeader(byte[] bytes, bool resetPixels)
    {
        if (!resetPixels || bytes.Length < GfxImageAsset.SerializedSize)
            return;

        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c, sizeof(uint)), 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0x20, sizeof(ushort)), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0x22, sizeof(ushort)), 1);
        bytes[0x26] = 1;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x28, sizeof(int)), 0);
    }

    private static GfxImageAsset CreateProjectedImage(
        GfxImageAsset destination,
        GfxImageAsset incoming,
        XAssetPoolAddress slotAddress,
        bool resetPixels) =>
        new()
        {
            Offset = destination.Offset,
            RuntimeAddress = XRuntimeAddress.FromAssetPool(slotAddress),
            Format = destination.Format,
            LevelCount = destination.LevelCount,
            DimensionCount = destination.DimensionCount,
            MultiFaceControl = destination.MultiFaceControl,
            TextureFlags = destination.TextureFlags,
            Width = destination.Width,
            Height = destination.Height,
            Depth = destination.Depth,
            PixelDataBlock = destination.PixelDataBlock,
            Pad0F = destination.Pad0F,
            RenderTargetPitch = destination.RenderTargetPitch,
            PixelsOffset = destination.PixelsOffset,
            MapType = destination.MapType,
            TextureSemantic = destination.TextureSemantic,
            Category = destination.Category,
            Pad1B = destination.Pad1B,
            CardMemory = resetPixels ? 0 : destination.CardMemory,
            BaseWidth = resetPixels ? (ushort)1 : destination.BaseWidth,
            BaseHeight = resetPixels ? (ushort)1 : destination.BaseHeight,
            BaseDepth = destination.BaseDepth,
            BaseLevelCount = resetPixels ? (byte)1 : destination.BaseLevelCount,
            Cached = destination.Cached,
            PayloadPointer = resetPixels
                ? XPointerReference.FromRaw(0, destination.PayloadPointer.ResolutionMode)
                : destination.PayloadPointer,
            StreamData = destination.StreamData,
            StreamImageIndex = destination.StreamImageIndex,
            StreamEntries = destination.StreamEntries,
            PayloadByteCount = resetPixels ? 0 : destination.PayloadByteCount,
            PayloadBytes = resetPixels ? [] : destination.PayloadBytes,
            NamePointer = incoming.NamePointer,
            Name = incoming.Name
        };
}
