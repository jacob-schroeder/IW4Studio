using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;

namespace IW4.Render;

/// <summary>
/// Loader-independent boundary for the runtime objects consumed by map rendering.
/// Collections and runtime services are retained by reference so their identities,
/// ordering, revisions, and lifecycle state remain unchanged.
/// </summary>
public sealed class MapRenderAssetSource
{
    public MapRenderAssetSource(
        DbHeader header,
        IXAssetSourceMemory blocks,
        XAssetPool assetPool,
        GfxWorldRuntimeState gfxWorldRuntime,
        IReadOnlyDictionary<XBlockAddress, GfxImageAsset> gfxImagesByAddress,
        IReadOnlyList<XAssetLoadResult> loadedAssets,
        IReadOnlyList<XAssetListEntrySnapshot> assetListEntries)
    {
        Header = header;
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        AssetPool = assetPool ?? throw new ArgumentNullException(nameof(assetPool));
        GfxWorldRuntime = gfxWorldRuntime ?? throw new ArgumentNullException(nameof(gfxWorldRuntime));
        GfxImagesByAddress = gfxImagesByAddress ?? throw new ArgumentNullException(nameof(gfxImagesByAddress));
        LoadedAssets = loadedAssets ?? throw new ArgumentNullException(nameof(loadedAssets));
        AssetListEntries = assetListEntries ?? throw new ArgumentNullException(nameof(assetListEntries));
    }

    public DbHeader Header { get; }

    public IXAssetSourceMemory Blocks { get; }

    public XAssetPool AssetPool { get; }

    public GfxWorldRuntimeState GfxWorldRuntime { get; }

    public IReadOnlyDictionary<XBlockAddress, GfxImageAsset> GfxImagesByAddress { get; }

    public IReadOnlyList<XAssetLoadResult> LoadedAssets { get; }

    public IReadOnlyList<XAssetListEntrySnapshot> AssetListEntries { get; }
}
