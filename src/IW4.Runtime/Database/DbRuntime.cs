using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.GfxMap;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Strings;

namespace IW4.Runtime.Database;

/// <summary>
/// Owns the global DB asset pool while keeping each loaded XZone's memory and
/// stream state independent. A loader stages one complete zone, then invokes
/// the engine-shaped, no-argument DB_PostLoadXZone operation.
/// </summary>
public sealed class DbRuntime
{
    private readonly List<DbLoadedXZone> _zones = [];
    private readonly IReadOnlyList<DbLoadedXZone> _zoneView;
    private DbLoadedXZone? _stagedZone;
    private Exception? _postLoadFailure;
    private long _nextZoneHandle = 1;
    private DbRuntimeBatchTransaction? _activeBatch;
    private XAssetRuntimeAddressJournal? _activeBatchRuntimeAddressJournal;
    private readonly List<DbLoadedXZone> _pendingZoneMemoryReleases = [];
    private XAssetRuntimeLifecycleTransaction? _activeLifecycleTransaction;
    private Exception? _batchFailure;
    private bool _batchZoneMemoryReleaseCannotRollback;

    public DbRuntime(
        XAssetPool? assetPool = null,
        ScriptStringTable? scriptStrings = null,
        MaterialTechniqueStateCache? materialTechniqueStateCache = null,
        IGfxImageRuntimeRegistrationHooks? gfxImageRuntimeRegistrationHooks = null,
        ManagedXAssetRuntimeLifecycle? assetRuntimeLifecycle = null)
    {
        AssetPool = assetPool ?? new XAssetPool();
        ScriptStrings = scriptStrings ?? new ScriptStringTable();
        MaterialTechniqueStateCache = materialTechniqueStateCache ?? new MaterialTechniqueStateCache();
        GfxImageRuntimeRegistrationHooks = gfxImageRuntimeRegistrationHooks;
        AssetRuntimeLifecycle = assetRuntimeLifecycle ?? new ManagedXAssetRuntimeLifecycle();
        ZoneMemoryAllocator = new DbZoneMemoryAllocator();
        _zoneView = _zones.AsReadOnly();
    }

    public XAssetPool AssetPool { get; }
    public ScriptStringTable ScriptStrings { get; }
    public MaterialTechniqueStateCache MaterialTechniqueStateCache { get; }
    public IGfxImageRuntimeRegistrationHooks? GfxImageRuntimeRegistrationHooks { get; }
    public ManagedXAssetRuntimeLifecycle AssetRuntimeLifecycle { get; }

    /// <summary>
    /// Runtime-wide XZone allocator authority. Every loader attached to this
    /// runtime shares its Main/Local logical RSX address domains, just as all
    /// simultaneously resident native zones share one process allocator.
    /// </summary>
    public DbZoneMemoryAllocator ZoneMemoryAllocator { get; }
    public IReadOnlyList<DbLoadedXZone> Zones => _zoneView;
    public DbLoadedXZone? CurrentZone { get; private set; }
    public DbLoadedXZone? StagedZone => _stagedZone;
    public bool IsFaulted => _postLoadFailure is not null;
    public Exception? PostLoadFailure => _postLoadFailure;
    internal bool HasActiveBatchTransaction => _activeBatch is not null;

    public void ThrowIfFaulted()
    {
        if (_postLoadFailure is not { } failure)
            return;

        throw new InvalidOperationException(
            "DbRuntime cannot load or mutate another XZone after an unrecoverable runtime operation failed; " +
            "pooled state or zone memory may no longer be safe to roll back.",
            failure);
    }

    public DbRuntimeBatchTransaction BeginBatchTransaction()
    {
        ThrowIfFaulted();
        if (_activeBatch is not null)
            throw new InvalidOperationException("A DbRuntime batch transaction is already active.");
        if (_stagedZone is not null)
            throw new InvalidOperationException("Cannot begin a DB_LoadXAssets batch while an XZone is staged.");
        if (_pendingZoneMemoryReleases.Count != 0)
            throw new InvalidOperationException("A previous DB batch still owns deferred XZone memory releases.");

        DbRuntimeState state = CaptureRuntimeState();
        XAssetRuntimeLifecycleTransaction? lifecycleTransaction = null;
        XAssetRuntimeAddressJournal? runtimeAddressJournal = null;
        try
        {
            lifecycleTransaction =
                AssetRuntimeLifecycle.Dispatcher.BeginTransaction();
            runtimeAddressJournal = AssetPool.BeginRuntimeAddressJournal();
            var transaction = new DbRuntimeBatchTransaction(this, state);
            _activeLifecycleTransaction = lifecycleTransaction;
            _activeBatchRuntimeAddressJournal = runtimeAddressJournal;
            _batchFailure = null;
            _batchZoneMemoryReleaseCannotRollback = false;
            _activeBatch = transaction;
            return transaction;
        }
        catch
        {
            runtimeAddressJournal?.Dispose();
            lifecycleTransaction?.Dispose();
            throw;
        }
    }

    internal XAssetRuntimeLifecycleTransaction? BeginZoneLifecycleTransaction()
    {
        ThrowIfBatchFailed();
        return _activeBatch is null
            ? AssetRuntimeLifecycle.Dispatcher.BeginTransaction()
            : null;
    }

    public DbZoneHandle BeginXZoneLoad(XZone zone, IDbZoneLoadRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfFaulted();
        ThrowIfBatchFailed();
        if (!ReferenceEquals(context.AssetPool, AssetPool) ||
            !ReferenceEquals(context.ScriptStrings, ScriptStrings) ||
            !ReferenceEquals(context.MaterialTechniqueStateCache, MaterialTechniqueStateCache) ||
            !ReferenceEquals(context.GfxImageRuntimeRegistrationHooks, GfxImageRuntimeRegistrationHooks) ||
            !ReferenceEquals(context.AssetRuntimeLifecycle, AssetRuntimeLifecycle))
        {
            throw new InvalidOperationException(
                "An XZone contribution owner can only be bound to this runtime's global asset and script-string registries.");
        }

        var handle = new DbZoneHandle(_nextZoneHandle++);
        context.AssetLoadSession.BindZoneOwner(handle);
        return handle;
    }

    public void StageXZone(
        XZone zone,
        IDbZoneLoadRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfFaulted();

        if (_stagedZone is not null)
            throw new InvalidOperationException("An XZone is already staged for DB_PostLoadXZone.");
        if (!ReferenceEquals(context.AssetPool, AssetPool))
            throw new InvalidOperationException("The staged DB load context does not use this runtime's global XAssetPool.");
        if (!ReferenceEquals(context.ScriptStrings, ScriptStrings))
            throw new InvalidOperationException("The staged DB load context does not use this runtime's global script-string table.");
        if (!ReferenceEquals(context.Blocks.ZoneMemory, zone.Memory))
            throw new InvalidOperationException("The staged XZone and DB stream state do not share the same XZoneMemory.");
        if (_zones.Any(loaded => ReferenceEquals(loaded.Zone, zone)))
            throw new InvalidOperationException($"XZone '{zone.File.Name}' is already registered.");

        DbZoneHandle handle = context.ZoneOwner;
        if (handle.IsNone)
            throw new InvalidOperationException("The staged DbLoadContext has no XZone contribution owner.");
        DbZoneContributions contributions = context.AssetLoadSession.FreezeZoneContributions();
        _stagedZone = new DbLoadedXZone(
            handle,
            zone,
            context,
            contributions);
    }

    /// <summary>
    /// Runs registered material and GfxWorld post-load work for the staged zone.
    /// </summary>
    public void DB_PostLoadXZone()
    {
        DbLoadedXZone staged = _stagedZone
            ?? throw new InvalidOperationException("DB_PostLoadXZone requires a staged XZone.");

        // The old session registered the zone before replaying post-load. This
        // also matches the global-state shape of the no-argument engine symbol:
        // the current zone is already discoverable when post-load begins.
        DbLoadedXZone? previousCurrentZone = CurrentZone;
        _zones.Add(staged);
        CurrentZone = staged;
        _stagedZone = null;
        try
        {
            RebuildDerivedState();
        }
        catch (Exception exception)
        {
            _zones.RemoveAt(_zones.Count - 1);
            CurrentZone = previousCurrentZone;
            _stagedZone = null;
            _postLoadFailure = exception;
            throw;
        }
    }

    /// <summary>
    /// Retires every resident zone whose allocation flags intersect
    /// <paramref name="freeFlags"/>, scanning newest-to-oldest. Canonical slot
    /// addresses survive provider promotion, and script strings are collected
    /// only after their final zone claim is released.
    /// </summary>
    public DbZoneRetirementResult DB_FreeXZones(XZoneFlags freeFlags)
    {
        ThrowIfFaulted();
        ThrowIfBatchFailed();
        if (freeFlags == XZoneFlags.None)
            throw new ArgumentOutOfRangeException(nameof(freeFlags));
        if (_stagedZone is not null)
            throw new InvalidOperationException("Cannot retire XZones while one is staged for DB_PostLoadXZone.");

        DbLoadedXZone[] selected = _zones
            .AsEnumerable()
            .Reverse()
            .Where(zone => (zone.Zone.Flags & freeFlags) != 0)
            .ToArray();
        if (selected.Length == 0)
            return new DbZoneRetirementResult(Array.Empty<DbLoadedXZone>());

        XAssetPoolState assetState = AssetPool.CaptureState();
        ScriptStringTableState stringState = ScriptStrings.CaptureState();
        DbLoadedXZone[] zoneState = _zones.ToArray();
        DbLoadedXZone? currentState = CurrentZone;
        var slotChanges = new List<XAssetSlotChange>();
        var retirementPlans = new List<(DbLoadedXZone Zone, XAssetProviderRetirementPlan Plan)>();
        XAssetRuntimeLifecycleTransaction? localLifecycleTransaction =
            _activeBatch is null
                ? AssetRuntimeLifecycle.Dispatcher.BeginTransaction()
                : null;
        XAssetRuntimeLifecycleTransaction lifecycleTransaction =
            localLifecycleTransaction ?? _activeLifecycleTransaction
            ?? throw new InvalidOperationException("XAsset retirement has no lifecycle transaction.");
        bool zoneMemoryReleaseAttempted = false;
        bool releasedZoneMemoryObserved = false;
        bool localLifecycleCommitAttempted = false;
        bool localLifecycleCommitCompleted = false;
        bool preserveUncommittedLocalLifecycle = false;

        try
        {
            ValidateZoneMemoryReleaseCandidates(
                selected,
                ref releasedZoneMemoryObserved);

            // Preview against an isolated topology so every release guard and
            // ownership ledger is validated before the live registry moves.
            var planningPool = new XAssetPool();
            planningPool.RestoreState(assetState);
            // RestoreState installs state-owned slot objects. Clone once more
            // before simulating retirements so the rollback snapshot remains
            // immutable and can still restore the live pool after a failure.
            planningPool.RestoreState(planningPool.CaptureState());
            foreach (DbLoadedXZone zone in selected)
            {
                if (!ReferenceEquals(zone.Context.Blocks.ZoneMemory, zone.Zone.Memory) ||
                    zone.Zone.Memory.IsReleased)
                {
                    throw new InvalidOperationException(
                        $"XZone '{zone.Zone.Name}' no longer owns a live matching DB stream allocation.");
                }

                IReadOnlyCollection<ScriptStringHandle> liveClaims =
                    ScriptStrings.GetZoneClaims(zone.Handle);
                if (!liveClaims.ToHashSet().SetEquals(zone.Contributions.ScriptStrings))
                {
                    throw new InvalidOperationException(
                        $"XZone '{zone.Zone.Name}' script-string ownership ledger does not match the global claim table.");
                }

                XAssetProviderRetirementPlan plan =
                    planningPool.PlanProviderRetirement(zone.Handle);
                XAssetProviderId[] removedProviders = plan.Changes
                    .SelectMany(change => change.RemovedProviders)
                    .Select(provider => provider.Id)
                    .ToArray();
                if (!removedProviders.ToHashSet().SetEquals(zone.Contributions.AssetProviders))
                {
                    throw new InvalidOperationException(
                        $"XZone '{zone.Zone.Name}' asset-provider ownership ledger does not match the canonical pool.");
                }

                // Building the operations is validation too: unsupported
                // singleton fallback and incomplete callback metadata fail
                // while the live pool, strings, zones, and memory are intact.
                XAssetRetirementPlanner.Build(plan.Changes);
                planningPool.ApplyProviderRetirement(plan);
                retirementPlans.Add((zone, plan));
                slotChanges.AddRange(plan.Changes);
            }

            IReadOnlyList<XAssetRetirementOperation> retirementOperations =
                XAssetRetirementPlanner.Build(slotChanges);

            // Every invariant guard runs before the first lifecycle side
            // effect or canonical-slot mutation.
            foreach (XAssetRetirementOperation operation in retirementOperations
                         .Where(operation => operation.Kind == XAssetRetirementOperationKind.InvokeReleaseCallback))
            {
                AssetRuntimeLifecycle.Dispatcher.ValidateRelease(
                    XAssetRuntimeLifecycleContextFactory.CreateRelease(operation));
            }

            var replacementDecisions = new Dictionary<XAssetProviderId, XAssetReplacementDecision>();
            foreach (XAssetRetirementOperation operation in retirementOperations)
            {
                switch (operation.Kind)
                {
                    case XAssetRetirementOperationKind.InvokeReleaseCallback:
                        lifecycleTransaction.ReleaseRuntimeState(
                            XAssetRuntimeLifecycleContextFactory.CreateRelease(operation));
                        break;

                    case XAssetRetirementOperationKind.ReplaceActiveProvider:
                        XAssetReplacementDecision decision = lifecycleTransaction.ReplaceRuntimeState(
                            XAssetRuntimeLifecycleContextFactory.CreateReplacement(operation));
                        if (decision == XAssetReplacementDecision.Unresolved)
                        {
                            throw new InvalidOperationException(
                                $"Runtime state for {operation.AssetType} '{operation.Name}' cannot prove the mode-1 replacement result.");
                        }

                        XAssetProviderId incomingProviderId = operation.IncomingProvider?.Id
                            ?? throw new InvalidOperationException("Replacement operation has no incoming provider identity.");
                        replacementDecisions.Add(incomingProviderId, decision);
                        break;

                    case XAssetRetirementOperationKind.RetirePoolAllocation:
                        lifecycleTransaction.RetirePoolAllocation(
                            XAssetRuntimeLifecycleContextFactory.CreatePoolFree(operation));
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported XAsset retirement operation {operation.Kind}.");
                }
            }

            foreach ((DbLoadedXZone zone, XAssetProviderRetirementPlan plan) in retirementPlans)
                AssetPool.ApplyProviderRetirement(plan, replacementDecisions);

            HashSet<DbZoneHandle> retiringHandles = selected
                .Select(zone => zone.Handle)
                .ToHashSet();
            XAssetProviderContribution? danglingProvider = AssetPool.Slots
                .SelectMany(slot => slot.Providers)
                .FirstOrDefault(provider => retiringHandles.Contains(provider.Owner));
            if (danglingProvider is not null)
            {
                throw new InvalidOperationException(
                    $"Provider {danglingProvider.Id} still references retiring zone {danglingProvider.Owner}.");
            }

            // Rebuild global material/world bindings while every retiring
            // zone's strings and block memory are still available.
            RebuildDerivedState();

            foreach ((DbLoadedXZone zone, _) in retirementPlans)
            {
                ScriptStrings.ReleaseZoneClaims(zone.Handle);
                _zones.Remove(zone);
            }

            CurrentZone = _zones.LastOrDefault();
            if (_activeBatch is null)
            {
                ReleaseZoneMemory(
                    selected,
                    ref zoneMemoryReleaseAttempted,
                    ref releasedZoneMemoryObserved);
                localLifecycleCommitAttempted = true;
                localLifecycleTransaction?.Commit();
                localLifecycleCommitCompleted = true;
            }
            else
            {
                _pendingZoneMemoryReleases.AddRange(selected);
            }

            return new DbZoneRetirementResult(Array.AsReadOnly(selected));
        }
        catch (Exception exception)
        {
            if (zoneMemoryReleaseAttempted || releasedZoneMemoryObserved)
            {
                Exception fault = exception;
                if (_activeBatch is null)
                {
                    if (!localLifecycleCommitAttempted)
                    {
                        localLifecycleCommitAttempted = true;
                        try
                        {
                            localLifecycleTransaction?.Commit();
                            localLifecycleCommitCompleted = true;
                        }
                        catch (Exception lifecycleFailure)
                        {
                            preserveUncommittedLocalLifecycle = true;
                            fault = new AggregateException(
                                "XZone memory release and lifecycle finalization both failed.",
                                exception,
                                lifecycleFailure);
                        }
                    }
                    else if (!localLifecycleCommitCompleted)
                    {
                        // A lifecycle commit attempted after memory release is
                        // not safe to roll back even if its completion path
                        // itself failed.
                        preserveUncommittedLocalLifecycle = true;
                    }
                }
                else
                {
                    _batchZoneMemoryReleaseCannotRollback = true;
                    _batchFailure = exception;
                    try
                    {
                        _activeLifecycleTransaction?.Commit();
                    }
                    catch (Exception lifecycleFailure)
                    {
                        fault = new AggregateException(
                            "XZone memory release and batch lifecycle finalization both failed.",
                            exception,
                            lifecycleFailure);
                    }
                    finally
                    {
                        // Never dispose an uncommitted lifecycle snapshot after
                        // release may have invalidated the addresses it owns.
                        _activeLifecycleTransaction = null;
                    }
                }

                _postLoadFailure = fault;
                throw;
            }

            localLifecycleTransaction?.Dispose();
            if (_activeBatch is not null)
                _batchFailure = exception;

            AssetPool.RestoreState(assetState);
            ScriptStrings.RestoreState(stringState);
            _zones.Clear();
            _zones.AddRange(zoneState);
            CurrentZone = currentState;
            try
            {
                RebuildDerivedState();
            }
            catch (Exception recoveryFailure)
            {
                _postLoadFailure = recoveryFailure;
            }

            throw;
        }
        finally
        {
            if (!preserveUncommittedLocalLifecycle)
                localLifecycleTransaction?.Dispose();
        }
    }

    private void RebuildDerivedState()
    {
        // Material ordering and world-surface post-load state are process-
        // global derived state. Always rebuild from active canonical providers
        // so a promoted fallback cannot leave references to retired zone memory.
        MaterialPostLoadProcessor.RebuildDrawSurfs(AssetPool);
        foreach (XAssetPoolEntry entry in AssetPool.Entries)
        {
            if (entry.AssetType != XAssetType.GfxMap ||
                entry.Asset is not GfxWorldAsset world ||
                entry.SourceBlocks is not { } blocks)
            {
                continue;
            }

            GfxWorldSurfacePostLoadProcessor.Process(
                world,
                AssetPool,
                blocks);
        }
    }

    internal void CommitBatch(DbRuntimeBatchTransaction transaction)
    {
        EnsureActiveBatch(transaction);
        ThrowIfBatchFailed();
        bool zoneMemoryReleaseAttempted = false;
        bool releasedZoneMemoryObserved = false;
        bool lifecycleCommitAttempted = false;
        try
        {
            ReleaseZoneMemory(
                _pendingZoneMemoryReleases,
                ref zoneMemoryReleaseAttempted,
                ref releasedZoneMemoryObserved);
            lifecycleCommitAttempted = true;
            _activeLifecycleTransaction?.Commit();
            _activeLifecycleTransaction = null;
            _activeBatchRuntimeAddressJournal?.Commit();
            _activeBatchRuntimeAddressJournal = null;
            _pendingZoneMemoryReleases.Clear();
            _batchFailure = null;
            _batchZoneMemoryReleaseCannotRollback = false;
            _activeBatch = null;
        }
        catch (Exception exception)
        {
            _batchFailure = exception;
            if (zoneMemoryReleaseAttempted || releasedZoneMemoryObserved)
            {
                Exception fault = exception;
                _batchZoneMemoryReleaseCannotRollback = true;
                if (!lifecycleCommitAttempted)
                {
                    try
                    {
                        _activeLifecycleTransaction?.Commit();
                    }
                    catch (Exception lifecycleFailure)
                    {
                        fault = new AggregateException(
                            "XZone memory release and batch lifecycle finalization both failed.",
                            exception,
                            lifecycleFailure);
                    }
                }

                // The current registry is the only safe state once any
                // release implementation has been invoked. Abandon rather
                // than dispose a lifecycle snapshot if commit did not finish.
                _activeLifecycleTransaction = null;
                _postLoadFailure = fault;
            }

            throw;
        }
    }

    internal void RollbackBatch(
        DbRuntimeBatchTransaction transaction,
        DbRuntimeState state)
    {
        EnsureActiveBatch(transaction);
        try
        {
            if (_batchZoneMemoryReleaseCannotRollback)
            {
                // Commit reached a zone-memory release implementation. Its
                // effects cannot be made atomic through the runtime contract,
                // so restoring the batch snapshot could resurrect released or
                // partially invalidated memory.
                _pendingZoneMemoryReleases.Clear();
                _activeBatchRuntimeAddressJournal?.Commit();
                _activeBatchRuntimeAddressJournal = null;
                return;
            }

            HashSet<DbZoneHandle> retainedZoneHandles = state.Zones
                .Select(zone => zone.Handle)
                .ToHashSet();
            DbLoadedXZone[] transientZones = _zones
                .Where(zone => !retainedZoneHandles.Contains(zone.Handle))
                .ToArray();

            AssetPool.RestoreState(state.AssetPool);
            _activeBatchRuntimeAddressJournal?.Dispose();
            _activeBatchRuntimeAddressJournal = null;
            ScriptStrings.RestoreState(state.ScriptStrings);
            MaterialTechniqueStateCache.RestoreState(state.MaterialTechniqueStates);
            _zones.Clear();
            _zones.AddRange(state.Zones);
            CurrentZone = state.CurrentZone;
            _stagedZone = state.StagedZone;
            _postLoadFailure = state.PostLoadFailure;
            _nextZoneHandle = state.NextZoneHandle;
            _pendingZoneMemoryReleases.Clear();

            // Zones loaded during the failed batch are absent from the
            // restored registry and can now release their private blocks.
            bool transientReleaseAttempted = false;
            bool releasedTransientMemoryObserved = false;
            try
            {
                ReleaseZoneMemory(
                    transientZones,
                    ref transientReleaseAttempted,
                    ref releasedTransientMemoryObserved);
            }
            catch (Exception releaseFailure)
            {
                // The restored snapshot never contains these transient
                // zones, so it remains safe; fault the runtime because their
                // private allocations may now be partially invalidated.
                _postLoadFailure = releaseFailure;
                throw;
            }

            try
            {
                RebuildDerivedState();
            }
            catch (Exception recoveryFailure)
            {
                _postLoadFailure = recoveryFailure;
            }
        }
        finally
        {
            _activeLifecycleTransaction?.Dispose();
            _activeLifecycleTransaction = null;
            _activeBatchRuntimeAddressJournal?.Dispose();
            _activeBatchRuntimeAddressJournal = null;
            _batchFailure = null;
            _batchZoneMemoryReleaseCannotRollback = false;
            _activeBatch = null;
        }
    }

    private DbRuntimeState CaptureRuntimeState() =>
        new(
            AssetPool.CaptureState(),
            ScriptStrings.CaptureState(),
            MaterialTechniqueStateCache.CaptureState(),
            _zones.ToArray(),
            CurrentZone,
            _stagedZone,
            _postLoadFailure,
            _nextZoneHandle);

    private void EnsureActiveBatch(DbRuntimeBatchTransaction transaction)
    {
        if (!ReferenceEquals(_activeBatch, transaction))
            throw new InvalidOperationException("DbRuntime batch transaction ownership is inconsistent.");
    }

    private void ThrowIfBatchFailed()
    {
        if (_batchFailure is not { } failure)
            return;

        throw new InvalidOperationException(
            "The active DB_LoadXAssets batch has failed and must be rolled back.",
            failure);
    }

    private static void ValidateZoneMemoryReleaseCandidates(
        IReadOnlyCollection<DbLoadedXZone> zones,
        ref bool releasedZoneMemoryObserved)
    {
        foreach (DbLoadedXZone zone in zones)
        {
            XZoneMemory memory = zone.Zone.Memory;
            if (!ReferenceEquals(zone.Context.Blocks.ZoneMemory, memory))
            {
                throw new InvalidOperationException(
                    $"XZone '{zone.Zone.Name}' no longer owns its DB stream allocation.");
            }

            if (memory.IsReleased)
            {
                releasedZoneMemoryObserved = true;
                throw new InvalidOperationException(
                    $"XZone '{zone.Zone.Name}' memory was released before registry retirement completed.");
            }
        }
    }

    private static void ReleaseZoneMemory(
        IReadOnlyCollection<DbLoadedXZone> zones,
        ref bool releaseAttempted,
        ref bool releasedZoneMemoryObserved)
    {
        ValidateZoneMemoryReleaseCandidates(zones, ref releasedZoneMemoryObserved);
        foreach (DbLoadedXZone zone in zones)
        {
            XZoneMemory memory = zone.Zone.Memory;
            releaseAttempted = true;
            try
            {
                zone.Context.Blocks.ReleaseZoneMemory(memory);
            }
            catch (Exception exception)
            {
                releasedZoneMemoryObserved |= memory.IsReleased;
                throw new InvalidOperationException(
                    $"XZone '{zone.Zone.Name}' memory release failed.",
                    exception);
            }

            if (!memory.IsReleased)
            {
                throw new InvalidOperationException(
                    $"XZone '{zone.Zone.Name}' memory release returned without releasing the allocation.");
            }

            releasedZoneMemoryObserved = true;
        }
    }
}
