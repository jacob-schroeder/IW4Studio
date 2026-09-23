namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Converts retirement operations into allocation-keyed policy contexts.
/// Role mismatches fail before runtime state is mutated.
/// </summary>
public static class XAssetRuntimeLifecycleContextFactory
{
    public static XAssetReleaseContext CreateRelease(
        XAssetRetirementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireKind(operation, XAssetRetirementOperationKind.InvokeReleaseCallback);
        XAssetProviderContribution provider = RequireOnlyOutgoingProvider(operation);

        return new XAssetReleaseContext(
            operation.Address,
            operation.AssetType,
            operation.Name,
            CreateAllocationKey(operation, provider));
    }

    public static XAssetReplacementContext CreateReplacement(
        XAssetRetirementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireKind(operation, XAssetRetirementOperationKind.ReplaceActiveProvider);
        if (operation.OutgoingProvider is not { } outgoing)
            throw new InvalidOperationException("A replacement operation requires an outgoing stable-head provider.");
        if (operation.IncomingProvider is not { } incoming)
            throw new InvalidOperationException("A replacement operation requires an incoming fallback provider.");
        if (operation.PoolAllocationProvider is not null)
            throw new InvalidOperationException("A replacement operation cannot also carry a pool-free provider role.");
        if (operation.AllocationKind is not null)
            throw new InvalidOperationException("Replacement source and destination allocation kinds are fixed by their roles.");
        if (outgoing.Id == incoming.Id)
            throw new InvalidOperationException("Replacement source and destination providers must be distinct.");

        ValidateOperationIdentity(operation, outgoing);
        ValidateOperationIdentity(operation, incoming);
        return new XAssetReplacementContext(
            operation.Address,
            operation.AssetType,
            operation.Name,
            XAssetRuntimeAllocationKey.ForProvider(operation.Address, incoming.Id.Value),
            XAssetRuntimeAllocationKey.ForStableSlot(operation.Address),
            mode: 1);
    }

    public static XAssetPoolFreeContext CreatePoolFree(
        XAssetRetirementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireKind(operation, XAssetRetirementOperationKind.RetirePoolAllocation);
        if (operation.OutgoingProvider is not null || operation.IncomingProvider is not null)
            throw new InvalidOperationException("A pool-free operation cannot carry outgoing/incoming replacement roles.");
        XAssetProviderContribution provider = operation.PoolAllocationProvider
            ?? throw new InvalidOperationException("A pool-free operation requires its exact allocation provider.");

        ValidateOperationIdentity(operation, provider);
        return new XAssetPoolFreeContext(
            operation.Address,
            operation.AssetType,
            operation.Name,
            CreateAllocationKey(operation, provider));
    }

    private static XAssetProviderContribution RequireOnlyOutgoingProvider(
        XAssetRetirementOperation operation)
    {
        if (operation.OutgoingProvider is not { } outgoing)
            throw new InvalidOperationException("A release operation requires its exact outgoing provider.");
        if (operation.IncomingProvider is not null || operation.PoolAllocationProvider is not null)
            throw new InvalidOperationException("A release operation cannot carry replacement or pool-free provider roles.");

        ValidateOperationIdentity(operation, outgoing);
        return outgoing;
    }

    private static XAssetRuntimeAllocationKey CreateAllocationKey(
        XAssetRetirementOperation operation,
        XAssetProviderContribution provider) =>
        operation.AllocationKind switch
        {
            XAssetRuntimeAllocationKind.StableSlot =>
                XAssetRuntimeAllocationKey.ForStableSlot(operation.Address),
            XAssetRuntimeAllocationKind.Provider =>
                XAssetRuntimeAllocationKey.ForProvider(operation.Address, provider.Id.Value),
            null => throw new InvalidOperationException(
                $"{operation.Kind} requires an explicit runtime allocation kind."),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime allocation kind {operation.AllocationKind}.")
        };

    private static void ValidateOperationIdentity(
        XAssetRetirementOperation operation,
        XAssetProviderContribution provider)
    {
        if (operation.Sequence < 0)
            throw new InvalidOperationException("A retirement operation sequence cannot be negative.");
        if (operation.Address.AssetType != operation.AssetType)
            throw new InvalidOperationException("Retirement operation slot/type identity is inconsistent.");
        if (provider.AssetType != operation.AssetType ||
            !string.Equals(provider.Name, operation.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Provider {provider.Id} identity does not match {operation.AssetType} '{operation.Name}'.");
        }
    }

    private static void RequireKind(
        XAssetRetirementOperation operation,
        XAssetRetirementOperationKind expected)
    {
        if (operation.Kind != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected}, but operation {operation.Sequence} is {operation.Kind}.");
        }
    }
}
