using IW4.Runtime.Assets.Lifecycle;

namespace IW4.Runtime.Assets;

/// <summary>
/// Translates canonical-slot mutations into managed release, replacement, and
/// pool-retirement operations.
/// </summary>
public static class XAssetRetirementPlanner
{
    public static IReadOnlyList<XAssetRetirementOperation> Build(
        IEnumerable<XAssetSlotChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var operations = new List<XAssetRetirementOperation>();
        foreach (XAssetSlotChange change in changes)
        {
            XAssetTypeRuntimeMetadata metadata =
                XAssetTypeRuntimeMetadataCatalog.Get(change.AssetType);
            if (!metadata.HasCanonicalRegistration)
            {
                throw new InvalidOperationException(
                    $"Native-no-op type {change.AssetType} cannot own a canonical provider retirement.");
            }

            XAssetProviderContribution? outgoingActive =
                change.Kind == XAssetSlotChangeKind.Promoted
                    ? change.PreviousActiveProvider
                    : null;

            if (outgoingActive is not null)
            {
                AddReleaseIfPresent(
                    operations,
                    change,
                    outgoingActive,
                    XAssetRuntimeAllocationKind.StableSlot,
                    metadata);
            }

            if (change.Kind == XAssetSlotChangeKind.Promoted)
            {
                if (!metadata.AllowsFallbackPromotion)
                {
                    throw new InvalidOperationException(
                        $"Canonical singleton type {change.AssetType} cannot promote a duplicate provider for " +
                        $"'{change.Name}'; its runtime pool capacity is one.");
                }

                XAssetProviderContribution previous = change.PreviousActiveProvider
                    ?? throw new InvalidOperationException(
                        $"Promoted slot {change.Address} has no previous active provider.");
                XAssetProviderContribution replacement = change.ActiveProvider
                    ?? throw new InvalidOperationException(
                        $"Promoted slot {change.Address} has no fallback provider.");
                operations.Add(Create(
                    operations.Count,
                    XAssetRetirementOperationKind.ReplaceActiveProvider,
                    change,
                    previous,
                    replacement,
                    allocationKind: null));

                // Native mode-1 replacement copies fallback state into the
                // stable head, then retires the fallback provider node's old
                // typed-pool allocation. The semantic provider survives in
                // the stable slot; consumers must not mutate its root here.
                AddPoolRetirement(
                    operations,
                    change,
                    replacement,
                    XAssetRuntimeAllocationKind.Provider,
                    metadata);
            }

            foreach (XAssetProviderContribution removed in change.RemovedProviders)
            {
                if (ReferenceEquals(removed, outgoingActive))
                    continue;

                XAssetRuntimeAllocationKind allocationKind =
                    change.Kind == XAssetSlotChangeKind.Released &&
                    ReferenceEquals(removed, change.PreviousActiveProvider)
                        ? XAssetRuntimeAllocationKind.StableSlot
                        : XAssetRuntimeAllocationKind.Provider;
                AddReleaseIfPresent(
                    operations,
                    change,
                    removed,
                    allocationKind,
                    metadata);
                AddPoolRetirement(
                    operations,
                    change,
                    removed,
                    allocationKind,
                    metadata);
            }
        }

        return Array.AsReadOnly(operations.ToArray());
    }

    private static void AddReleaseIfPresent(
        List<XAssetRetirementOperation> operations,
        XAssetSlotChange change,
        XAssetProviderContribution provider,
        XAssetRuntimeAllocationKind allocationKind,
        XAssetTypeRuntimeMetadata metadata)
    {
        if (!metadata.HasReleaseLifecycle)
            return;

        operations.Add(Create(
            operations.Count,
            XAssetRetirementOperationKind.InvokeReleaseCallback,
            change,
            provider,
            replacement: null,
            allocationKind: allocationKind));
    }

    private static void AddPoolRetirement(
        List<XAssetRetirementOperation> operations,
        XAssetSlotChange change,
        XAssetProviderContribution provider,
        XAssetRuntimeAllocationKind allocationKind,
        XAssetTypeRuntimeMetadata metadata)
    {
        operations.Add(Create(
            operations.Count,
            XAssetRetirementOperationKind.RetirePoolAllocation,
            change,
            provider,
            replacement: null,
            allocationKind: allocationKind));
    }

    private static XAssetRetirementOperation Create(
        int sequence,
        XAssetRetirementOperationKind kind,
        XAssetSlotChange change,
        XAssetProviderContribution provider,
        XAssetProviderContribution? replacement,
        XAssetRuntimeAllocationKind? allocationKind) =>
        new(
            sequence,
            kind,
            change.Address,
            change.AssetType,
            change.Name,
            kind is XAssetRetirementOperationKind.InvokeReleaseCallback or
                XAssetRetirementOperationKind.ReplaceActiveProvider
                    ? provider
                    : null,
            kind == XAssetRetirementOperationKind.ReplaceActiveProvider
                ? replacement
                : null,
            kind == XAssetRetirementOperationKind.RetirePoolAllocation
                ? provider
                : null,
            allocationKind);
}
