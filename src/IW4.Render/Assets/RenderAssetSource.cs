using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Render.Assets;

/// <summary>
/// Loader-independent boundary for runtime objects consumed by rendering.
/// Collections and runtime services are retained by reference so their identities,
/// ordering, revisions, and lifecycle state remain unchanged.
/// </summary>
public sealed class RenderAssetSource
{
    public RenderAssetSource(
        IXAssetSourceMemory blocks,
        XAssetPool assetPool,
        IReadOnlyDictionary<XBlockAddress, GfxImageAsset> gfxImagesByAddress,
        IReadOnlyList<XAssetLoadResult> loadedAssets,
        IReadOnlyList<XAssetListEntrySnapshot> assetListEntries)
    {
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        AssetPool = assetPool ?? throw new ArgumentNullException(nameof(assetPool));
        GfxImagesByAddress = gfxImagesByAddress ?? throw new ArgumentNullException(nameof(gfxImagesByAddress));
        LoadedAssets = loadedAssets ?? throw new ArgumentNullException(nameof(loadedAssets));
        AssetListEntries = assetListEntries ?? throw new ArgumentNullException(nameof(assetListEntries));
    }

    public IXAssetSourceMemory Blocks { get; }

    public XAssetPool AssetPool { get; }

    public IReadOnlyDictionary<XBlockAddress, GfxImageAsset> GfxImagesByAddress { get; }

    public IReadOnlyList<XAssetLoadResult> LoadedAssets { get; }

    public IReadOnlyList<XAssetListEntrySnapshot> AssetListEntries { get; }
}
