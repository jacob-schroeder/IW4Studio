using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;

namespace IW4.Render.Assets;

/// <summary>
/// Immutable one-revision binding table captured after the frame lightmap
/// refresh. Rendering never consults mutable live texture state.
/// </summary>
public sealed class MapRenderWorldTextureBindingSnapshot :
    IMapRenderWorldTextureBindingResolver
{
    private readonly Dictionary<
        MapRenderWorldRuntimeTextureIdentity,
        MapRenderWorldTextureAssetBinding> _bindingsByIdentity;
    private readonly MapRenderWorldTextureAssetBinding[] _bindings;

    internal MapRenderWorldTextureBindingSnapshot(
        XAssetPoolAddress worldAddress,
        long textureRevision,
        IReadOnlyList<MapRenderWorldTextureAssetBinding> bindings)
        : this(worldAddress, textureRevision, assetPoolRevision: 0, bindings)
    {
    }

    internal MapRenderWorldTextureBindingSnapshot(
        XAssetPoolAddress worldAddress,
        long textureRevision,
        long assetPoolRevision,
        IReadOnlyList<MapRenderWorldTextureAssetBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (worldAddress.AssetType != XAssetType.GfxMap)
            throw new ArgumentException("A world binding snapshot requires a GfxMap slot.", nameof(worldAddress));
        if (textureRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(textureRevision));
        if (assetPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(assetPoolRevision));

        _bindings = bindings.ToArray();
        if (_bindings.Any(binding => binding is null))
            throw new ArgumentException("A world binding snapshot cannot contain null entries.", nameof(bindings));
        _bindingsByIdentity = new Dictionary<
            MapRenderWorldRuntimeTextureIdentity,
            MapRenderWorldTextureAssetBinding>(_bindings.Length);
        int previousKindOrder = -1;
        int[] expectedOrdinalByKind = new int[
            Enum.GetValues<MapRenderWorldRuntimeTextureKind>().Length];
        foreach (MapRenderWorldTextureAssetBinding binding in _bindings)
        {
            int kindOrder = (int)binding.Identity.Kind;
            if (kindOrder < previousKindOrder ||
                binding.Identity.Ordinal != expectedOrdinalByKind[kindOrder])
            {
                throw new ArgumentException(
                    "World bindings must retain contiguous reflection-secondary-primary sampler order.",
                    nameof(bindings));
            }
            if (!_bindingsByIdentity.TryAdd(binding.Identity, binding))
            {
                throw new ArgumentException(
                    $"World binding snapshot contains duplicate identity {binding.Identity}.",
                    nameof(bindings));
            }
            if (binding.IsRenderResourceReady &&
                binding.AssetPoolRevision != assetPoolRevision)
            {
                throw new ArgumentException(
                    "A ready world resource escaped the snapshot's canonical provider revision.",
                    nameof(bindings));
            }

            expectedOrdinalByKind[kindOrder]++;
            previousKindOrder = kindOrder;
        }

        WorldAddress = worldAddress;
        TextureRevision = textureRevision;
        AssetPoolRevision = assetPoolRevision;
        BindingsInSamplerOrder = Array.AsReadOnly(_bindings);
    }

    public XAssetPoolAddress WorldAddress { get; }

    public long TextureRevision { get; }

    public long AssetPoolRevision { get; }

    public IReadOnlyList<MapRenderWorldTextureAssetBinding> BindingsInSamplerOrder { get; }

    public MapRenderWorldTextureAssetBinding ResolveWorldRuntimeTexture(
        GfxWorldAsset world,
        MapRenderWorldRuntimeTextureIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.RuntimeAddress?.AssetPoolAddress != WorldAddress)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.WorldIdentityMismatch);
        }

        return _bindingsByIdentity.TryGetValue(identity, out MapRenderWorldTextureAssetBinding? binding)
            ? binding
            : new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.SlotOutOfRange);
    }
}
