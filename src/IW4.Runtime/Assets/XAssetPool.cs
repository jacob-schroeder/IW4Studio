using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Process-wide canonical XAsset registry. Each identity owns one stable slot
/// address and an ordered chain of zone-owned providers.
/// </summary>
public sealed class XAssetPool
{
    // Synthetic runtime pointers deliberately occupy an invalid XFile block
    // nibble. Serialized packed pointers use only blocks 0..6.
    private const uint SyntheticBaseAddress = 0x80010000;

    private readonly Dictionary<(XAssetType Type, string Name), XAssetSlot> _slotsByIdentity =
        new(new XAssetIdentityComparer());
    private readonly Dictionary<int, XAssetSlot> _slotsByRawPointer = new();
    private XAssetPoolTransaction? _activeTransaction;
    private XAssetRuntimeAddressJournal? _activeRuntimeAddressJournal;
    private int _nextSlot;
    private uint _nextOffset;
    private long _nextProviderId = 1;
    private long _nextRegistrationSequence = 1;
    private long _revision;

    /// <summary>
    /// Monotonic canonical-provider revision. Frame snapshots retain this
    /// value so provider replacement cannot silently change captured inputs.
    /// </summary>
    public long Revision => _revision;

    public IReadOnlyCollection<XAssetPoolEntry> Entries =>
        Array.AsReadOnly(_slotsByRawPointer.Values
            .OrderBy(slot => slot.Address.Slot)
            .Select(slot => slot.ToActiveEntry())
            .ToArray());

    public IReadOnlyCollection<XAssetSlot> Slots =>
        Array.AsReadOnly(_slotsByRawPointer.Values
            .OrderBy(slot => slot.Address.Slot)
            .ToArray());

    public XAssetPoolTransaction BeginTransaction()
    {
        if (_activeTransaction is not null)
            throw new InvalidOperationException("The XAssetPool already has an active load transaction.");

        var transaction = new XAssetPoolTransaction(this, CaptureState());
        _activeTransaction = transaction;
        return transaction;
    }

    internal XAssetRuntimeAddressJournal BeginRuntimeAddressJournal()
    {
        if (_activeRuntimeAddressJournal is not null)
        {
            throw new InvalidOperationException(
                "The XAssetPool already has an active runtime-address journal.");
        }

        var journal = new XAssetRuntimeAddressJournal(this);
        _activeRuntimeAddressJournal = journal;
        return journal;
    }

    /// <summary>
    /// Registers an unowned provider. This compatibility overload is used by
    /// isolated loader tests and tooling; DbRuntime zone loads use the owned
    /// overload so free flags can later retire their exact contributions.
    /// </summary>
    public XAssetPoolEntry DB_AddXAsset(
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        out bool added,
        IXAssetSourceMemory? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null)
    {
        return DB_AddXAsset(
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            owner: default,
            out added,
            out _,
            sourceBlocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);
    }

    public XAssetPoolEntry DB_AddXAsset(
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        DbZoneHandle owner,
        out bool added,
        out XAssetProviderId providerId,
        IXAssetSourceMemory? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(name);

        bool isReference = IsReferenceName(name);
        string canonicalName = CanonicalizeName(name);
        var key = (assetType, NormalizeName(canonicalName));
        byte[] copiedHeader = headerBytes.ToArray();
        byte[] copiedPoolBytes = nativePoolCopyBytes.IsEmpty
            ? copiedHeader
            : nativePoolCopyBytes.ToArray();
        int capturedLength = ValidateNativePoolCopyCapturedLength(
            nativePoolCopyCapturedLength,
            copiedPoolBytes.Length);

        if (_slotsByIdentity.TryGetValue(key, out XAssetSlot? existingSlot))
        {
            XAssetProviderContribution previousActive = existingSlot.ActiveProvider;
            XAssetProviderContribution provider = CreateProvider(
                owner,
                assetType,
                canonicalName,
                asset,
                stagingAddress,
                copiedHeader,
                copiedPoolBytes,
                capturedLength,
                isReference,
                sourceBlocks);
            TrackRuntimeAddressMutation(asset);
            asset.SetCanonicalRuntimeAddress(existingSlot.Address);
            existingSlot.AddProvider(provider);
            _revision++;

            providerId = provider.Id;
            added = previousActive.Id != existingSlot.ActiveProvider.Id;
            return existingSlot.ToActiveEntry();
        }

        uint alignedOffset = (_nextOffset + 3U) & ~3U;
        uint raw = checked(SyntheticBaseAddress + alignedOffset);
        if (raw is 0 or uint.MaxValue or 0xfffffffeU)
            throw new InvalidOperationException("Synthetic XAsset-pool pointer collided with a pointer sentinel.");

        var address = new XAssetPoolAddress(assetType, _nextSlot++, unchecked((int)raw));
        XAssetProviderContribution firstProvider = CreateProvider(
            owner,
            assetType,
            canonicalName,
            asset,
            stagingAddress,
            copiedHeader,
            copiedPoolBytes,
            capturedLength,
            isReference,
            sourceBlocks);
        TrackRuntimeAddressMutation(asset);
        asset.SetCanonicalRuntimeAddress(address);

        var slot = new XAssetSlot(address, assetType, canonicalName, [firstProvider]);
        _slotsByIdentity.Add(key, slot);
        _slotsByRawPointer.Add(address.RawValue, slot);
        _nextOffset = checked(alignedOffset + (uint)Math.Max(sizeof(int), copiedPoolBytes.Length));
        _revision++;
        providerId = firstProvider.Id;
        added = true;
        return slot.ToActiveEntry();
    }

    public bool TryResolve(int rawPointer, out XAssetPoolEntry entry)
    {
        if (_slotsByRawPointer.TryGetValue(rawPointer, out XAssetSlot? slot))
        {
            entry = slot.ToActiveEntry();
            return true;
        }

        entry = null!;
        return false;
    }

    public bool TryGetSlot(XAssetPoolAddress address, out XAssetSlot? slot)
    {
        if (_slotsByRawPointer.TryGetValue(address.RawValue, out XAssetSlot? candidate) &&
            candidate.Address == address)
        {
            slot = candidate;
            return true;
        }

        slot = null;
        return false;
    }

    internal XAssetProviderContribution GetProvider(
        XAssetPoolAddress address,
        XAssetProviderId providerId)
    {
        if (!TryGetSlot(address, out XAssetSlot? slot) || slot is null)
        {
            throw new InvalidDataException(
                $"XAsset pool address {address} has no registered slot.");
        }

        XAssetProviderContribution? provider = slot.Providers
            .SingleOrDefault(candidate => candidate.Id == providerId);
        return provider ?? throw new InvalidDataException(
            $"XAsset pool slot {address} has no provider {providerId}.");
    }

    public bool TryResolve<TAsset>(int rawPointer, XAssetType expectedType, out TAsset? asset)
        where TAsset : BaseAsset
    {
        if (_slotsByRawPointer.TryGetValue(rawPointer, out XAssetSlot? slot) &&
            slot.AssetType == expectedType &&
            slot.CanonicalAsset is TAsset typed)
        {
            asset = typed;
            return true;
        }

        asset = null;
        return false;
    }

    public bool TryResolve<TAsset>(XAssetType expectedType, string name, out TAsset? asset)
        where TAsset : BaseAsset
    {
        var key = (expectedType, NormalizeName(CanonicalizeName(name)));
        if (_slotsByIdentity.TryGetValue(key, out XAssetSlot? slot) &&
            slot.CanonicalAsset is TAsset typed)
        {
            asset = typed;
            return true;
        }

        asset = null;
        return false;
    }

    /// <summary>
    /// Captures the stable slot identity for a canonical provider. The handle
    /// remains valid across fallback promotion and becomes unresolved only
    /// after the final provider for the slot is retired.
    /// </summary>
    public XAssetHandle<TAsset> CreateHandle<TAsset>(TAsset asset)
        where TAsset : BaseAsset
    {
        ArgumentNullException.ThrowIfNull(asset);
        XAssetPoolAddress address = asset.RuntimeAddress?.AssetPoolAddress
            ?? throw new InvalidOperationException(
                $"{typeof(TAsset).Name} has no canonical XAsset slot identity.");
        if (!_slotsByRawPointer.TryGetValue(address.RawValue, out XAssetSlot? slot) ||
            slot.AssetType != address.AssetType ||
            !slot.Providers.Any(provider => ReferenceEquals(provider.Asset, asset)))
        {
            throw new InvalidOperationException(
                $"{typeof(TAsset).Name} points at {address}, but is not a provider of that canonical slot.");
        }

        return new XAssetHandle<TAsset>(address);
    }

    public bool TryResolve<TAsset>(XAssetHandle<TAsset> handle, out TAsset? asset)
        where TAsset : BaseAsset
    {
        if (!handle.IsNone &&
            _slotsByRawPointer.TryGetValue(handle.Address.RawValue, out XAssetSlot? slot) &&
            slot.Address == handle.Address &&
            slot.CanonicalAsset is TAsset typed)
        {
            asset = typed;
            return true;
        }

        asset = null;
        return false;
    }

    public TAsset Resolve<TAsset>(XAssetHandle<TAsset> handle)
        where TAsset : BaseAsset
    {
        if (TryResolve(handle, out TAsset? asset) && asset is not null)
            return asset;

        throw new KeyNotFoundException(
            $"Canonical XAsset handle {handle} has no active compatible provider.");
    }

    /// <summary>
    /// Resolves a possibly stale provider object through its stable slot
    /// identity. Callers that retain semantic objects across free flags should
    /// use this boundary instead of assuming the original provider remains
    /// active.
    /// </summary>
    public TAsset ResolveCurrent<TAsset>(TAsset asset)
        where TAsset : BaseAsset
    {
        ArgumentNullException.ThrowIfNull(asset);
        XAssetPoolAddress address = asset.RuntimeAddress?.AssetPoolAddress
            ?? throw new InvalidOperationException(
                $"{typeof(TAsset).Name} has no canonical XAsset slot identity.");
        if (!TryResolve(address.RawValue, address.AssetType, out TAsset? current) || current is null)
        {
            throw new InvalidOperationException(
                $"Canonical XAsset slot {address} no longer has a compatible active provider.");
        }

        return current;
    }

    public bool TryGetEntry(BaseAsset asset, out XAssetPoolEntry entry)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !_slotsByRawPointer.TryGetValue(address.RawValue, out XAssetSlot? slot) ||
            slot.AssetType != address.AssetType)
        {
            entry = null!;
            return false;
        }

        entry = slot.ToActiveEntry();
        return true;
    }

    public IReadOnlyList<XAssetSlotChange> RemoveProviders(DbZoneHandle owner)
    {
        XAssetProviderRetirementPlan plan = PlanProviderRetirement(owner);
        ApplyProviderRetirement(plan);
        return plan.Changes;
    }

    public XAssetProviderRetirementPlan PlanProviderRetirement(DbZoneHandle owner)
    {
        if (owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(owner));

        var changes = new List<XAssetSlotChange>();
        foreach (XAssetSlot slot in _slotsByRawPointer.Values.OrderBy(slot => slot.Address.Slot))
        {
            XAssetProviderContribution previousActive = slot.ActiveProvider;
            XAssetProviderContribution[] removed = slot.Providers
                .Where(provider => provider.Owner == owner)
                .ToArray();
            if (removed.Length == 0)
                continue;

            XAssetProviderContribution[] remaining = slot.Providers
                .Where(provider => provider.Owner != owner)
                .ToArray();
            if (remaining.Length == 0)
            {
                changes.Add(new XAssetSlotChange(
                    XAssetSlotChangeKind.Released,
                    slot.Address,
                    slot.AssetType,
                    slot.Name,
                    previousActive,
                    null,
                    Array.AsReadOnly(removed)));
                continue;
            }

            XAssetProviderContribution active = SelectActiveProvider(remaining);
            XAssetSlotChangeKind kind = previousActive.Id == active.Id
                ? XAssetSlotChangeKind.Unchanged
                : XAssetSlotChangeKind.Promoted;
            changes.Add(new XAssetSlotChange(
                kind,
                slot.Address,
                slot.AssetType,
                slot.Name,
                previousActive,
                active,
                Array.AsReadOnly(removed)));
        }

        return new XAssetProviderRetirementPlan(
            owner,
            _revision,
            Array.AsReadOnly(changes.ToArray()));
    }

    public void ApplyProviderRetirement(XAssetProviderRetirementPlan plan)
    {
        ApplyProviderRetirement(
            plan,
            new Dictionary<XAssetProviderId, XAssetReplacementDecision>());
    }

    public void ApplyProviderRetirement(
        XAssetProviderRetirementPlan plan,
        IReadOnlyDictionary<XAssetProviderId, XAssetReplacementDecision> replacementDecisions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(replacementDecisions);
        if (plan.Owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.PoolRevision != _revision)
        {
            throw new InvalidOperationException(
                $"Retirement plan revision {plan.PoolRevision} is stale; canonical pool revision is {_revision}.");
        }

        // Validate the complete immutable plan first. Public callers must not
        // observe a partially applied plan merely because a later slot became
        // stale between preview and commit.
        foreach (XAssetSlotChange change in plan.Changes)
        {
            if (!_slotsByRawPointer.TryGetValue(change.Address.RawValue, out XAssetSlot? slot) ||
                slot.Address != change.Address ||
                slot.AssetType != change.AssetType)
            {
                throw new InvalidOperationException(
                    $"Retirement plan slot {change.Address} no longer matches the canonical pool.");
            }

            XAssetProviderId[] expectedIds = change.RemovedProviders
                .Select(provider => provider.Id)
                .ToArray();
            XAssetProviderContribution[] actualRemoved = slot.Providers
                .Where(provider => provider.Owner == plan.Owner)
                .ToArray();
            if (!actualRemoved.Select(provider => provider.Id).SequenceEqual(expectedIds))
            {
                throw new InvalidOperationException(
                    $"Retirement plan provider set for {change.Address} changed before commit.");
            }

            XAssetProviderContribution[] remaining = slot.Providers
                .Where(provider => provider.Owner != plan.Owner)
                .ToArray();
            if (remaining.Length == 0)
            {
                if (change.Kind != XAssetSlotChangeKind.Released)
                    throw new InvalidOperationException($"Retirement plan kind for empty slot {change.Address} is inconsistent.");
                continue;
            }

            if (change.ActiveProvider is not { } expectedActive ||
                SelectActiveProvider(remaining).Id != expectedActive.Id)
            {
                throw new InvalidOperationException(
                    $"Retirement plan active provider for {change.Address} changed before commit.");
            }

            if (change.Kind != XAssetSlotChangeKind.Promoted)
                continue;

            XAssetReplacementDecision decision = replacementDecisions.GetValueOrDefault(
                expectedActive.Id,
                XAssetReplacementDecision.CopySource);
            if (decision == XAssetReplacementDecision.Unresolved)
            {
                throw new InvalidOperationException(
                    $"Retirement plan for {change.Address} has an unresolved provider replacement.");
            }
            if (decision == XAssetReplacementDecision.KeepDestinationWithSourceName &&
                change.AssetType != XAssetType.Image)
            {
                throw new InvalidOperationException(
                    $"KeepDestinationWithSourceName is not valid for {change.AssetType}.");
            }
        }

        foreach (XAssetSlotChange change in plan.Changes)
        {
            XAssetSlot slot = _slotsByRawPointer[change.Address.RawValue];
            slot.RemoveProviders(plan.Owner);
            if (slot.Providers.Count != 0)
            {
                if (change.Kind == XAssetSlotChangeKind.Promoted)
                {
                    XAssetReplacementDecision decision = replacementDecisions.GetValueOrDefault(
                        change.ActiveProvider!.Id,
                        XAssetReplacementDecision.CopySource);
                    if (decision == XAssetReplacementDecision.CopySource)
                        slot.CopyActiveProviderToProjection();
                    else
                        slot.KeepImageDestinationWithSourceName();
                }

                continue;
            }

            _slotsByRawPointer.Remove(slot.Address.RawValue);
            _slotsByIdentity.Remove((slot.AssetType, NormalizeName(slot.Name)));
        }

        if (plan.Changes.Count != 0)
            _revision++;
    }

    public static bool IsReferenceName(string name) =>
        XAssetStableIdentity.IsReferenceName(name);

    public static string CanonicalizeName(string name) =>
        XAssetStableIdentity.GetLookupSpelling(name);

    private XAssetProviderContribution CreateProvider(
        DbZoneHandle owner,
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        byte[] headerBytes,
        byte[] nativePoolCopyBytes,
        int nativePoolCopyCapturedLength,
        bool isReferencePlaceholder,
        IXAssetSourceMemory? sourceBlocks)
    {
        var id = new XAssetProviderId(_nextProviderId++);
        return new XAssetProviderContribution(
            id,
            owner,
            _nextRegistrationSequence++,
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength,
            isReferencePlaceholder,
            sourceBlocks);
    }

    private static string NormalizeName(string name) =>
        XAssetStableIdentity.NormalizeLookupName(name);

    private static XAssetProviderContribution SelectActiveProvider(
        IReadOnlyList<XAssetProviderContribution> providers) =>
        providers.FirstOrDefault(provider => !provider.IsReferencePlaceholder)
        ?? providers[0];

    private static int ValidateNativePoolCopyCapturedLength(
        int? requestedLength,
        int nativePoolCopyLength)
    {
        int capturedLength = requestedLength ?? nativePoolCopyLength;
        if ((uint)capturedLength > nativePoolCopyLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLength),
                capturedLength,
                $"Captured native pool bytes must be within 0..0x{nativePoolCopyLength:X}.");
        }

        return capturedLength;
    }

    internal XAssetPoolState CaptureState()
    {
        var clones = new Dictionary<XAssetSlot, XAssetSlot>(ReferenceEqualityComparer.Instance);
        foreach (XAssetSlot slot in _slotsByRawPointer.Values.OrderBy(slot => slot.Address.Slot))
            clones.Add(slot, slot.Clone());

        var identity = new Dictionary<(XAssetType Type, string Name), XAssetSlot>(
            new XAssetIdentityComparer());
        foreach (((XAssetType Type, string Name) key, XAssetSlot slot) in _slotsByIdentity)
            identity.Add(key, clones[slot]);

        var pointers = new Dictionary<int, XAssetSlot>();
        foreach ((int raw, XAssetSlot slot) in _slotsByRawPointer)
            pointers.Add(raw, clones[slot]);

        return new XAssetPoolState(
            identity,
            pointers,
            _nextSlot,
            _nextOffset,
            _nextProviderId,
            _nextRegistrationSequence,
            _revision);
    }

    internal void RestoreState(XAssetPoolState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _slotsByIdentity.Clear();
        foreach (((XAssetType Type, string Name) key, XAssetSlot slot) in state.SlotsByIdentity)
            _slotsByIdentity.Add(key, slot);

        _slotsByRawPointer.Clear();
        foreach ((int rawPointer, XAssetSlot slot) in state.SlotsByRawPointer)
            _slotsByRawPointer.Add(rawPointer, slot);

        _nextSlot = state.NextSlot;
        _nextOffset = state.NextOffset;
        _nextProviderId = state.NextProviderId;
        _nextRegistrationSequence = state.NextRegistrationSequence;
        _revision = state.Revision;
    }

    internal void CommitTransaction(XAssetPoolTransaction transaction)
    {
        EnsureActiveTransaction(transaction);
        _activeTransaction = null;
    }

    internal void RollbackTransaction(
        XAssetPoolTransaction transaction,
        XAssetPoolState state,
        IReadOnlyDictionary<BaseAsset, XRuntimeAddress?> originalRuntimeAddresses)
    {
        EnsureActiveTransaction(transaction);
        try
        {
            RestoreState(state);
            foreach ((BaseAsset asset, XRuntimeAddress? address) in originalRuntimeAddresses)
                asset.RestoreRuntimeAddress(address);
        }
        finally
        {
            _activeTransaction = null;
        }
    }

    private void TrackRuntimeAddressMutation(BaseAsset asset)
    {
        _activeTransaction?.TrackRuntimeAddress(asset, asset.RuntimeAddress);
        _activeRuntimeAddressJournal?.Track(asset, asset.RuntimeAddress);
    }

    internal void CommitRuntimeAddressJournal(
        XAssetRuntimeAddressJournal journal)
    {
        EnsureActiveRuntimeAddressJournal(journal);
        _activeRuntimeAddressJournal = null;
    }

    internal void RollbackRuntimeAddressJournal(
        XAssetRuntimeAddressJournal journal,
        IReadOnlyDictionary<BaseAsset, XRuntimeAddress?> originalAddresses)
    {
        EnsureActiveRuntimeAddressJournal(journal);
        try
        {
            foreach ((BaseAsset asset, XRuntimeAddress? address) in
                     originalAddresses)
            {
                asset.RestoreRuntimeAddress(address);
            }
        }
        finally
        {
            _activeRuntimeAddressJournal = null;
        }
    }

    private void EnsureActiveRuntimeAddressJournal(
        XAssetRuntimeAddressJournal journal)
    {
        if (!ReferenceEquals(_activeRuntimeAddressJournal, journal))
        {
            throw new InvalidOperationException(
                "XAssetPool runtime-address journal ownership is inconsistent.");
        }
    }

    private void EnsureActiveTransaction(XAssetPoolTransaction transaction)
    {
        if (!ReferenceEquals(_activeTransaction, transaction))
            throw new InvalidOperationException("XAssetPool transaction ownership is inconsistent.");
    }
}
