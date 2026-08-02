using IW4.Runtime.Database.Planning;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.FastFiles.Loaders.Database.Planning;

/// <summary>
/// Executes dependency request batches. Every batch performs its complete
/// free-flag pass before loading the first named row, matching DB_LoadXAssets.
/// </summary>
public static class DbLoadPlanExecutor
{
    public static DbZonePlanExecutionResult Execute(
        DbZoneLoadPlan plan,
        DbLoadSession session,
        Action<DbZonePlanRequest, LoadedXZone>? onZoneLoaded = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(session);

        DbZonePlanRequest? missingRequired = plan.RequestsInScope.FirstOrDefault(
            request => request.IsLoad &&
                       !request.FileExists &&
                       !request.MissingIsNonFatal);
        if (missingRequired is not null)
        {
            throw new FileNotFoundException(
                $"Required zone '{missingRequired.ZoneInfo.Name}' was not found.",
                missingRequired.FastFilePath);
        }

        var loadedZones = new List<LoadedXZone>();
        LoadedXZone? target = null;
        int remainingRequests = plan.RequestsInScope.Count;
        foreach (DbLoadXAssetsBatch batch in plan.Batches)
        {
            if (remainingRequests == 0)
                break;

            // SynchronizeBefore preserves an explicit native DB_SyncXAssets
            // boundary. Managed DB_LoadXZone calls and their enclosing batch
            // transactions complete before this loop advances, so no
            // additional wait operation is necessary here.
            DbZonePlanRequest[] requests = batch.Requests
                .Take(remainingRequests)
                .ToArray();
            remainingRequests -= requests.Length;
            int historyStart = session.LoadHistoryCount;
            using DbRuntimeBatchTransaction transaction = session.Runtime.BeginBatchTransaction();
            try
            {
                // Native DB_LoadXAssets applies every request's freeFlags
                // before it copies or dispatches any non-null name.
                foreach (DbZonePlanRequest request in requests)
                {
                    if (request.ZoneInfo.FreeFlags == XZoneFlags.None)
                        continue;

                    session.Runtime.DB_FreeXZones(request.ZoneInfo.FreeFlags);
                }

                foreach (DbZonePlanRequest request in requests)
                {
                    if (!request.IsLoad || !request.FileExists)
                        continue;

                    LoadedXZone loaded = session.DB_LoadXZone(
                        request.FastFilePath!,
                        new XZoneInfo(
                            request.ZoneInfo.Name,
                            request.ZoneInfo.AllocFlags,
                            XZoneFlags.None));
                    loadedZones.Add(loaded);
                    onZoneLoaded?.Invoke(request, loaded);
                    if (request.IsTarget)
                        target = loaded;
                }

                transaction.Commit();
            }
            catch
            {
                session.RollbackLoadHistory(historyStart);
                throw;
            }
        }

        return new DbZonePlanExecutionResult(
            Array.AsReadOnly(loadedZones.ToArray()),
            target ?? throw new InvalidOperationException("The target request did not produce a loaded zone."));
    }
}
