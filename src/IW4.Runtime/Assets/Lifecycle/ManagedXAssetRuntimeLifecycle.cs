using IW4.Assets.Zone;
using System.Buffers.Binary;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.Policies;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Process-global state updated during asset release, replacement, and pool
/// retirement. Renderer-owned state can be supplied or updated explicitly;
/// absent Image side records remain non-authoritative.
/// </summary>
public sealed class ManagedXAssetRuntimeLifecycle : IXAssetProviderRegistrationSink
{
    public ManagedXAssetRuntimeLifecycle(
        XModelStreamRuntimeState? xmodelStreams = null,
        GfxImageRuntimeState? gfxImages = null,
        ClipMapRuntimeState? clipMap = null,
        ComWorldRuntimeState? comWorld = null,
        GfxWorldRuntimeState? gfxWorld = null)
    {
        XModelStreams = xmodelStreams ?? new XModelStreamRuntimeState();
        GfxImages = gfxImages ?? new GfxImageRuntimeState();
        ClipMap = clipMap ?? new ClipMapRuntimeState();
        ComWorld = comWorld ?? new ComWorldRuntimeState(default);
        GfxWorld = gfxWorld ?? new GfxWorldRuntimeState();

        XAssetType[] noRuntimeStateTypes = XAssetTypeRuntimeMetadataCatalog.All
            .Where(metadata => metadata.HasCanonicalRegistration)
            .Select(metadata => metadata.CanonicalType)
            .Distinct()
            .Except(
            [
                XAssetType.XModel,
                XAssetType.Image,
                XAssetType.ColMapSp,
                XAssetType.ColMapMp,
                XAssetType.ComMap,
                XAssetType.GfxMap
            ])
            .ToArray();

        Dispatcher = new XAssetRuntimeLifecycleDispatcher(
        [
            new XModelStreamLifecyclePolicy(XModelStreams),
            new GfxImageStreamLifecyclePolicy(GfxImages),
            new ClipMapSingletonLifecyclePolicy(ClipMap),
            new ComWorldLifecyclePolicy(ComWorld),
            new GfxWorldGuardLifecyclePolicy(GfxWorld),
            new NoRuntimeLifecyclePolicy(noRuntimeStateTypes)
        ]);
    }

    public XAssetRuntimeLifecycleDispatcher Dispatcher { get; }

    public XModelStreamRuntimeState XModelStreams { get; }

    public GfxImageRuntimeState GfxImages { get; }

    public ClipMapRuntimeState ClipMap { get; }

    public ComWorldRuntimeState ComWorld { get; }

    public GfxWorldRuntimeState GfxWorld { get; }

    /// <summary>
    /// Publishes initial managed side state for one newly registered provider.
    /// Zeroed Image side records are non-authoritative until the renderer
    /// supplies their indexed bytes.
    /// </summary>
    public void RegisterProvider(
        XAssetPool pool,
        XAssetPoolAddress slotAddress,
        XAssetProviderId providerId)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (!pool.TryGetSlot(slotAddress, out XAssetSlot? slot) || slot is null)
            throw new InvalidOperationException($"Cannot register runtime state for missing slot {slotAddress}.");

        XAssetProviderContribution provider = slot.Providers
            .SingleOrDefault(candidate => candidate.Id == providerId)
            ?? throw new InvalidOperationException(
                $"Cannot register runtime state for missing provider {providerId} in {slotAddress}.");
        bool isStableHead = slot.ActiveProvider.Id == provider.Id;
        XAssetRuntimeAllocationKey allocation = isStableHead
            ? XAssetRuntimeAllocationKey.ForStableSlot(slotAddress)
            : XAssetRuntimeAllocationKey.ForProvider(slotAddress, provider.Id.Value);

        if (isStableHead)
            PreservePreviousStableState(slot, provider);

        switch (slot.AssetType)
        {
            case XAssetType.XModel:
                XModelStreams.Set(allocation, default);
                break;

            case XAssetType.Image:
                GfxImageAsset image = provider.Asset as GfxImageAsset
                    ?? throw new InvalidOperationException("Image provider has an incompatible managed model.");
                GfxImages.Set(
                    allocation,
                    new GfxImageRuntimeRecord(
                        GfxImageRuntimeSideRecord.Zero(),
                        IsSideRecordAuthoritative: false,
                        AuxiliaryWord: 0,
                        new GfxImageRuntimeHeaderState(
                            CardMemory: 0,
                            image.BaseWidth,
                            image.BaseHeight,
                            image.BaseDepth,
                            image.BaseLevelCount,
                            image.Cached,
                            Pixels: 0),
                        StreamPart0Marked: false,
                        StreamPart1Marked: false,
                        StreamPart2Marked: false,
                        StreamPart3Marked: false,
                        CardMemoryMarked: false));
                break;

            case XAssetType.ColMapMp when isStableHead:
                ClipMap.Replace(slot.ToActiveEntry().HeaderBytes);
                break;

            case XAssetType.ComMap when isStableHead:
                ReadOnlySpan<byte> header = slot.ToActiveEntry().HeaderBytes;
                if (header.Length < 0x10)
                    throw new InvalidDataException("ComMap canonical projection is shorter than 0x10 bytes.");
                ComWorld.Set(new ComWorldRuntimeRecord(
                    BinaryPrimitives.ReadUInt32BigEndian(header),
                    BinaryPrimitives.ReadInt32BigEndian(header[0x04..]),
                    BinaryPrimitives.ReadInt32BigEndian(header[0x08..]),
                    BinaryPrimitives.ReadUInt32BigEndian(header[0x0c..])));
                break;

            case XAssetType.GfxMap when isStableHead:
                GfxWorld.SetBspInUse(false);
                GfxWorld.MarkTextureInitializationPending(slot.Address);
                break;
        }
    }

    private void PreservePreviousStableState(
        XAssetSlot slot,
        XAssetProviderContribution incoming)
    {
        XAssetProviderContribution? previous = slot.Providers
            .Where(provider => provider.Id != incoming.Id)
            .OrderBy(provider => provider.RegistrationSequence)
            .FirstOrDefault();
        if (previous is null)
            return;

        XAssetRuntimeAllocationKey stable =
            XAssetRuntimeAllocationKey.ForStableSlot(slot.Address);
        XAssetRuntimeAllocationKey providerAllocation =
            XAssetRuntimeAllocationKey.ForProvider(slot.Address, previous.Id.Value);
        if (slot.AssetType == XAssetType.XModel &&
            XModelStreams.TryGet(stable, out XModelStreamRuntimeRecord xmodelState))
        {
            XModelStreams.Set(providerAllocation, xmodelState);
        }
        else if (slot.AssetType == XAssetType.Image &&
                 GfxImages.TryGet(stable, out GfxImageRuntimeRecord? imageState) &&
                 imageState is not null)
        {
            GfxImages.Set(providerAllocation, imageState);
        }
    }
}
