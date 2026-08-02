using IW4.FastFiles.Streaming.Database.Streaming;
using IW4.Runtime.Database;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.Strings;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Explicit managed dependencies for one in-progress XZone load. This is a
/// C# implementation aid, not an original engine structure and not a synonym
/// for XZoneMemory; the latter owns only the seven per-zone block allocations.
/// Its type-specific DB_AddXAsset methods cover canonical pool registrations;
/// other supported asset readers remain block-addressed.
/// </summary>
public sealed class DbLoadContext : DbLoadExecutionContext, IDbZoneLoadRuntimeContext
{
    private int _nextGfxImageStreamIndex;
    private readonly Dictionary<XBlockAddress, GfxImageAsset> _gfxImagesByAddress = new();
    private XAssetRowMaterializationScope? _activeMaterializationScope;

    public DbLoadContext(
        XAssetPool? assetPool = null,
        ScriptStringTable? scriptStrings = null,
        MaterialTechniqueStateCache? materialTechniqueStateCache = null,
        IGfxImageRuntimeRegistrationHooks? gfxImageRuntimeRegistrationHooks = null,
        ManagedXAssetRuntimeLifecycle? assetRuntimeLifecycle = null)
        : this(
            CreateLoadDependencies(assetPool, scriptStrings, assetRuntimeLifecycle),
            materialTechniqueStateCache,
            gfxImageRuntimeRegistrationHooks)
    {
    }

    private DbLoadContext(
        LoadDependencies dependencies,
        MaterialTechniqueStateCache? materialTechniqueStateCache,
        IGfxImageRuntimeRegistrationHooks? gfxImageRuntimeRegistrationHooks)
        : base(dependencies.AssetLoadSession, materialTechniqueStateCache)
    {
        GfxImageRuntimeRegistrationHooks = gfxImageRuntimeRegistrationHooks;
        AssetRuntimeLifecycle = dependencies.AssetRuntimeLifecycle;
    }

    public uint SelectedLanguageMask { get; set; }
    public DbHeader? Header { get; set; }
    public byte[]? DecodedZoneBytes { get; set; }

    // "CurrentFastFile" is an external stream-source category used to
    // distinguish the active .ff from imagefile%d.pak; it is not the old load
    // result type or another model of DBFile.
    public StreamFileRef CurrentFastFile { get; set; } = new(0, "<current fastfile>", StreamFileKind.CurrentFastFile);

    public XAssetLoadSession AssetLoadSession => AssetLoadSessionCore;
    public XAssetPool AssetPool => AssetLoadSession.AssetPool;
    public ScriptStringTable ScriptStrings => AssetLoadSession.ScriptStrings;
    public MaterialTechniqueStateCache MaterialTechniqueStateCache =>
        MaterialTechniqueStateCacheCore;
    public IGfxImageRuntimeRegistrationHooks? GfxImageRuntimeRegistrationHooks { get; }
    public ManagedXAssetRuntimeLifecycle AssetRuntimeLifecycle { get; }
    public Action<XAssetLoadProgress>? AssetProgress { get; set; }
    public IReadOnlyDictionary<XBlockAddress, GfxImageAsset> GfxImagesByAddress => _gfxImagesByAddress;
    public IReadOnlyDictionary<XBlockAddress, MaterialAsset> MaterialsByAddress =>
        MaterialsByAddressCore;
    public DbZoneHandle ZoneOwner => AssetLoadSession.ZoneOwner;

    IXZoneRuntimeMemory IDbZoneLoadRuntimeContext.Blocks => Blocks;

    private static LoadDependencies CreateLoadDependencies(
        XAssetPool? assetPool,
        ScriptStringTable? scriptStrings,
        ManagedXAssetRuntimeLifecycle? assetRuntimeLifecycle)
    {
        ManagedXAssetRuntimeLifecycle lifecycle =
            assetRuntimeLifecycle ?? new ManagedXAssetRuntimeLifecycle();
        var session = new XAssetLoadSession(
            assetPool ?? new XAssetPool(),
            scriptStrings ?? new ScriptStringTable(),
            lifecycle);
        return new LoadDependencies(session, lifecycle);
    }

    private sealed record LoadDependencies(
        XAssetLoadSession AssetLoadSession,
        ManagedXAssetRuntimeLifecycle AssetRuntimeLifecycle);

    public ScriptStringTableEntry InternZoneString(
        string text,
        ScriptStringUser user = ScriptStringUser.XZone) =>
        AssetLoadSession.InternZoneString(text, user);

    internal void BindZoneOwner(DbZoneHandle zone) =>
        AssetLoadSession.BindZoneOwner(zone);

    internal DbZoneContributions FreezeZoneContributions() =>
        AssetLoadSession.FreezeZoneContributions();

    public override int? AllocateGfxImageStreamIndex(bool hasStreamingData)
    {
        return hasStreamingData ? _nextGfxImageStreamIndex++ : null;
    }

    public override IReadOnlyList<DbHeaderImageStreamEntry> GetGfxImageStreamEntries(int? imageIndex)
    {
        if (imageIndex is not { } index)
            return [];

        DbHeader header = Header
            ?? throw new InvalidDataException("Cannot bind GfxImage stream entries before the DB header is loaded.");
        int firstEntry = checked(index * GfxImageStreamData.EntryCount);
        int endEntry = checked(firstEntry + GfxImageStreamData.EntryCount);
        if (endEntry > header.ImageStreamEntries.Length)
        {
            throw new InvalidDataException(
                $"GfxImage stream index 0x{index:X} requires DB header entries 0x{firstEntry:X}..0x{endEntry - 1:X}, " +
                $"but the table contains 0x{header.ImageStreamEntries.Length:X} entries.");
        }

        return header.ImageStreamEntries.Slice(firstEntry, endEntry - firstEntry);
    }

    // Canonicalize the GfxImage root as XAsset type 9. Streamed mip bytes
    // remain runtime data referenced by the canonical image; they are not
    // separate XAssets.
    public override GfxImageAsset DB_AddXAsset(
        GfxImageAsset image,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = image.StagingAddress
            ?? throw new InvalidDataException("GfxImage has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, GfxImageAsset.SerializedSize);
        bool isPictureFrames = GfxImageRegistrationPolicy.IsPictureFrames(image);
        bool appliedHardwarePixelsOffset = GfxImageRegistrationPolicy.ApplyIncomingNullPayloadHeader(
            image,
            headerBytes,
            GfxImageRuntimeRegistrationHooks);
        XAssetPoolEntry entry = DB_AddXAssetToPool(
            XAssetType.Image,
            image.Name ?? string.Empty,
            image,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (GfxImageAsset)entry.Asset;

        GfxImageAsset callerImage = canonical;
        if (isPictureFrames)
        {
            GfxImageAsset? redirect = GfxImageRegistrationPolicy.ResolvePictureFramesRedirect(
                image,
                GfxImageRuntimeRegistrationHooks);
            if (redirect is not null)
            {
                callerImage = redirect;
            }
        }

        int callerRaw = callerImage.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException(
                $"GfxImage caller target '{callerImage.Name}' has no runtime address.");
        Blocks.WriteInt32(pointerCellAddress, callerRaw);
        RegisterGfxImage(image, pointerCellAddress: null);
        RegisterGfxImage(callerImage, pointerCellAddress);
        return callerImage;
    }

    public void RegisterGfxImage(GfxImageAsset? image, XBlockAddress? pointerCellAddress)
    {
        if (image is null)
            return;

        if (image.StagingAddress is { } stagingAddress)
            _gfxImagesByAddress.TryAdd(stagingAddress, image);

        if (pointerCellAddress is { } cellAddress)
            _gfxImagesByAddress[cellAddress] = image;
    }

    public override GfxImageAsset? ResolveGfxImage(XPointerReference pointer)
    {
        if (AssetPool.TryResolve(pointer.Raw, XAssetType.Image, out GfxImageAsset? directCanonical))
            return directCanonical;

        if (pointer.PackedAddress is { } packedAddress && _gfxImagesByAddress.TryGetValue(packedAddress, out GfxImageAsset? image))
            return image;

        if (pointer.Type == PointerType.Offset && pointer.ResolutionMode == XPointerResolutionMode.AliasCell)
        {
            if (pointer.PackedAddress is not { } aliasCell)
                return null;

            int raw = Blocks.ReadInt32(aliasCell);
            if (AssetPool.TryResolve(raw, XAssetType.Image, out GfxImageAsset? canonical))
                return canonical;

            if (XPointerCodec.TryDecodeBlockAddress(raw, out XBlockAddress targetAddress) &&
                _gfxImagesByAddress.TryGetValue(targetAddress, out image))
            {
                return image;
            }
        }

        return null;
    }

    public bool TryResolveAssetPoolPointer<TAsset>(
        int rawPointer,
        XAssetType expectedType,
        out TAsset? asset)
        where TAsset : BaseAsset
    {
        return AssetPool.TryResolve(rawPointer, expectedType, out asset);
    }

    /// <summary>
    /// Shared body of the typed PS3 DB_AddXAsset wrappers. The native generic
    /// path registers canonical asset names with SL user mask 4 before it
    /// publishes or resolves the asset-pool entry. Keeping that work here lets
    /// dependencies seed the same global handle space used by later zones.
    /// </summary>
    private XAssetPoolEntry DB_AddXAssetToPool(
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        XBlockAddress pointerCellAddress,
        out bool added,
        DbStreamState? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null)
    {
        return RegisterAsset(
            assetType,
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out added,
            sourceBlocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);
    }

    internal XAssetRowMaterializationScope BeginAssetRowMaterialization(
        XAssetListEntrySnapshot row,
        int sourceStartOffset)
    {
        if (_activeMaterializationScope is not null)
            throw new InvalidOperationException("Nested top-level XAsset materialization scopes are not supported.");

        var scope = new XAssetRowMaterializationScope(row, sourceStartOffset);
        _activeMaterializationScope = scope;
        return scope;
    }

    internal void EndAssetRowMaterialization(
        XAssetRowMaterializationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(_activeMaterializationScope, scope))
        {
            throw new InvalidOperationException(
                "The completed XAsset materialization scope does not belong to this load context.");
        }

        _activeMaterializationScope = null;
    }

    protected override void OnAssetProviderRegistered(
        XBlockAddress pointerCellAddress,
        XAssetProviderMaterialization provider,
        XAssetProviderId activeProviderId)
    {
        _activeMaterializationScope?.RecordRegistration(
            pointerCellAddress,
            provider,
            activeProviderId);
    }

    private TAsset? ResolveSimpleCanonicalAsset<TAsset>(
        XPointerReference pointer,
        XAssetType assetType)
        where TAsset : BaseAsset
    {
        if (AssetPool.TryResolve(pointer.Raw, assetType, out TAsset? directCanonical))
            return directCanonical;

        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.AliasCell ||
            pointer.PackedAddress is not { } aliasCell)
        {
            return null;
        }

        int raw = Blocks.ReadInt32(aliasCell);
        return AssetPool.TryResolve(raw, assetType, out TAsset? canonical)
            ? canonical
            : null;
    }
}
