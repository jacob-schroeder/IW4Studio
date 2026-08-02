using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

public sealed record XAssetPoolEntry(
    XAssetPoolAddress Address,
    XAssetType AssetType,
    string Name,
    BaseAsset Asset,
    XBlockAddress StagingAddress,
    byte[] HeaderBytes,
    bool IsReferencePlaceholder = false,
    IXAssetSourceMemory? SourceBlocks = null)
{
    /// <summary>
    /// Native-sized managed projection of the per-type DB_AddXAsset copy
    /// window. For ordinary assets it is the same byte sequence as
    /// <see cref="HeaderBytes"/>. See <see cref="NativePoolCopyCapturedLength"/>
    /// when the native window extends beyond managed XZoneMemory.
    /// </summary>
    public byte[] NativePoolCopyBytes { get; init; } = HeaderBytes;

    /// <summary>
    /// Prefix length sourced directly from managed staging memory. Any
    /// remaining bytes in <see cref="NativePoolCopyBytes"/> are explicit
    /// zero-fill for native allocator space that managed XZoneMemory does not
    /// retain; they are not source-backed bytes.
    /// </summary>
    public int NativePoolCopyCapturedLength { get; init; } = HeaderBytes.Length;
}
